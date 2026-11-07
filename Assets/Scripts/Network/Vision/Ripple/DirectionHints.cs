using System;
using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Vision
{
    // §5.4 direction hint (client only, switch RippleClientFeatures.DirectionHint, default off). A ring-display sound
    // (§5.5-1) that the server judged audible but sent without its world ring because of occlusion — a wall or the
    // water surface — lights the screen edge in its 8-way direction instead. Scope (11-08): only hidden rings; a sound
    // whose ring is shown gets no hint. Nothing new is sent: the hidden playback point the server already sends is the
    // listener's position plus 2 m in the snapped direction (SoundWorld.HearResolved), so only the direction is known.
    //
    // Fed from the end of three client RPC bodies, after their own playback and inside try/catch there:
    //   SoundTarget          valve · underwater valve · reflow · bandage · treat · moan · drowning · ring-only pulses
    //   PlayVoiceTarget      blocked voice packets (~50/s per speaker: no allocation, one dictionary lookup)
    //   SoundCanFrameTarget  blocked sound can frames
    // Not shown (state D): dead or spectating, Echo, escaped, outside a running round (lobby, result), own sounds.
    public static class DirectionHints
    {
        // One screen-edge channel per meaning. The §3.4 Seeker direction gauge can add its own channel (own colour)
        // to DirectionHintOverlay later; Phase C only has this one.
        public sealed class Channel
        {
            public readonly Color Color;
            public readonly float[] Start = new float[8], HoldUntil = new float[8];
            public Channel(Color color) { Color = color; Clear(); }
            public void Clear() { for (int i = 0; i < 8; i++) { Start[i] = -1f; HoldUntil[i] = -1f; } }
            public bool Any(float now)
            {
                for (int i = 0; i < 8; i++) if (DirectionHintRules.Alpha(Start[i], HoldUntil[i], now) > 0f) return true;
                return false;
            }
        }

        // Environment ripple colour #E8E8E8 (§16.2) — the same in colour-blind mode; no role colour (§5.5-1: hidden
        // sounds carry only direction, perceived radius and duration).
        public static readonly Channel Hint = new(RippleVisualSettings.EnvironmentColor);

        static readonly Dictionary<int, DirectionHintRules.SourceState> sources = new(64);
        static readonly Dictionary<int, float> loopRings = new(16);
        static readonly HashSet<string> faults = new();

        // Counters for the checks.
        public static int Shown { get; private set; }
        public static int Extended { get; private set; }
        public static int Lookups { get; private set; }
        public static int SoundCalls { get; private set; }
        public static int VoiceCalls { get; private set; }
        public static int LastOctant { get; private set; } = -1;
        // Checks only: make the hint path throw, to prove playback is not affected.
        public static bool FaultForChecks;

        public static void OnSound(string clip, Vector3 point, bool muffled, int loopKey, int actionId, bool fromOther,
            float pulseRadius, float pulseSeconds)
        {
            SoundCalls++;
            if (!RippleClientFeatures.DirectionHint) return;
            if (FaultForChecks) throw new InvalidOperationException("DirectionHint fault (checks)");
            int kind = DirectionHintRules.RingKind(clip);
            if (kind == 0) return;
            DirectionHintOverlay.Ensure();
            float now = Time.time;
            // The ring is shown: no hint. Loops remember it, because an underwater valve's later 0-radius ticks are its gap.
            if (pulseRadius > 0f) { if (loopKey >= 0) loopRings[loopKey] = now; return; }
            if (!muffled || !fromOther || !CanShowNow()) return;
            if (kind == DirectionHintRules.KindValveUnderwater && loopKey >= 0 &&
                loopRings.TryGetValue(loopKey, out float ringAt) && !DirectionHintRules.LoopHidden(ringAt, now)) return;
            int octant = DirectionHintRules.Octant(ListenerPosition(now), point);
            if (octant < 0) return;
            bool loop = loopKey >= 0;
            int key = loop ? (3 << 24) | (loopKey & 0xFFFFFF)
                : (4 << 24) | (kind << 16) | (actionId >= 0 ? actionId & 0x7FFF : 0x8000 | octant);
            Report(key, octant, now, Mathf.Max(pulseSeconds, DirectionHintRules.FadeIn),
                loop ? DirectionHintRules.LoopPeriod : DirectionHintRules.SourceCooldown, false);
        }

        // Voice packet path: no allocation, at most one dictionary lookup (plus one Add the first time a speaker is heard).
        public static void OnVoice(int speakerId, Vector3 point, bool blocked)
        {
            VoiceCalls++;
            if (!RippleClientFeatures.DirectionHint || !blocked) return;
            if (FaultForChecks) throw new InvalidOperationException("DirectionHint fault (checks)");
            DirectionHintOverlay.Ensure();
            if (!CanShowNow()) return;
            float now = Time.time;
            int octant = DirectionHintRules.Octant(ListenerPosition(now), point);
            if (octant < 0) return;
            Report((1 << 24) | (speakerId & 0xFFFFFF), octant, now, DirectionHintRules.PacketHold, DirectionHintRules.SourceCooldown, true);
        }

        public static void OnSoundCan(int canId, Vector3 point, bool blocked)
        {
            if (!RippleClientFeatures.DirectionHint || !blocked) return;
            if (FaultForChecks) throw new InvalidOperationException("DirectionHint fault (checks)");
            DirectionHintOverlay.Ensure();
            if (!CanShowNow()) return;
            float now = Time.time;
            int octant = DirectionHintRules.Octant(ListenerPosition(now), point);
            if (octant < 0) return;
            Report((2 << 24) | (canId & 0xFFFFFF), octant, now, DirectionHintRules.PacketHold, DirectionHintRules.SourceCooldown, true);
        }

        // Exceptions from the hint path, logged once per place and exception type.
        public static void ReportFault(string where, Exception exception)
        {
            try
            {
                if (faults.Count < 32 && faults.Add(where + "|" + exception.GetType().Name)) Debug.LogException(exception);
            }
            catch { /* reporting must never throw */ }
        }

        static void Report(int key, int octant, float now, float hold, float cooldown, bool extend)
        {
            Lookups++;
            if (!sources.TryGetValue(key, out var state))
            {
                state = new DirectionHintRules.SourceState();
                sources.Add(key, state);
            }
            int decision = DirectionHintRules.Decide(state, octant, now, cooldown, extend);
            if (decision == DirectionHintRules.Ignore) return;
            DirectionHintRules.Light(ref Hint.Start[octant], ref Hint.HoldUntil[octant], now, hold);
            if (decision == DirectionHintRules.Show) Shown++;
            else Extended++;
            LastOctant = octant;
        }

        public static bool CanShowNow()
        {
            var local = NetworkPlayer.Local;
            var round = NetworkRound.Instance;
            if (local == null || round == null) return false;
            return DirectionHintRules.CanShow(local.Status == PlayerStatus.Alive, local.IsEcho,
                round.Mode != RoundMode.NotStarted && round.Result == RoundResult.InProgress);
        }

        // ---- where the server thought I was ----
        // The hidden point is 2 m from the server's copy of my position, which lags a remote client by about one round
        // trip plus the transform interpolation. The overlay samples my position every frame; the host has no lag.
        const int History = 64;
        static readonly float[] historyTime = new float[History];
        static readonly Vector3[] historyPosition = new Vector3[History];
        static int historyHead, historyCount;

        internal static void Sample(Vector3 position, float time)
        {
            historyTime[historyHead] = time;
            historyPosition[historyHead] = position;
            historyHead = (historyHead + 1) % History;
            if (historyCount < History) historyCount++;
        }

        static Vector3 ListenerPosition(float now)
        {
            var local = NetworkPlayer.Local;
            Vector3 current = local != null ? local.transform.position : Vector3.zero;
            float lag = ServerViewLag();
            if (lag <= 0f || historyCount == 0) return current;
            float at = now - lag;
            for (int i = 0; i < historyCount; i++)
            {
                int index = (historyHead - 1 - i + History) % History;
                if (historyTime[index] <= at) return historyPosition[index];
            }
            return historyPosition[(historyHead - historyCount + History) % History];
        }

        static float ServerViewLag()
        {
            var round = NetworkRound.Instance;
            if (round == null || round.IsServerStarted) return 0f;
            var time = round.TimeManager;
            if (time == null) return 0f;
            return Mathf.Min(.5f, time.RoundTripTime / 1000f + 2f * (float)time.TickDelta);
        }
    }
}
