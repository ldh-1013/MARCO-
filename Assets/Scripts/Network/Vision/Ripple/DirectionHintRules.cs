using System;
using UnityEngine;

namespace Marco.Network.Vision
{
    // Pure rules of the §5.4 direction hint (no scene, no allocation), shared by the hint state and the checks.
    //   direction: 8 world octants, the same 45° snap the server uses for hidden sources (atan2(x, z), 0 = +Z, clockwise)
    //   time: fade in 0.08 s (§2.1 0~80ms), hold for the perceived duration, fade out 0.3 s
    //   repeats: one source re-lights after 0.5 s (§3.4); continuing sounds every 2.5 s; packets extend by 0.6 s
    public static class DirectionHintRules
    {
        public const float MaxAlpha = .35f;          // 연출 튜닝값 (승인 11-08): "은은한" vignette
        public const float BandFraction = .06f;      // 연출 튜닝값: band depth, share of the screen's short side
        public const float PacketHold = .6f;         // 연출 튜닝값: voice / sound can packet keeps the hint lit
        public const float FadeIn = .08f;            // §2.1 0~80ms
        public const float FadeOut = .3f;            // 연출 튜닝값
        public const float SourceCooldown = .5f;     // §3.4 same-source update cooldown
        public const float LoopPeriod = 2.5f;        // 연출 튜닝값: continuing sounds (valve turning), like the underwater valve ring
        public const float LoopRingWindow = 2.6f;    // underwater valve: a ring this recent means the 0-radius tick is its gap, not occlusion
        public const float ArcDegrees = 45f;         // one octant

        // Ring-display kinds (§5.5-1) that reach the client through SoundTarget; 0 = not ring display (no hint).
        public const int KindForced = 1, KindValve = 2, KindValveUnderwater = 3, KindReflow = 4, KindBandage = 5,
            KindTreat = 6, KindDrowning = 7, KindMoan = 8;

        public static int RingKind(string clip)
        {
            if (clip == null) return 0;
            if (clip.Length == 0) return KindForced;   // PulseSpec.NoAudio: tag · gate · last survivor · drain work · exit (Shout / Valve grade)
            if (clip == "Valve") return KindValve;
            if (clip == "ValveUnderwater") return KindValveUnderwater;
            if (clip == "Reflow") return KindReflow;
            if (clip == "Bandage") return KindBandage;
            if (clip == "Treat") return KindTreat;
            if (clip == "Hurt") return KindDrowning;   // EmitDrowningSound — its ring goes separately and only when unblocked
            if (clip.StartsWith("Moan_", StringComparison.Ordinal)) return KindMoan;
            return 0;
        }

        // World octant 0..7 from `from` towards `to` on the floor plane, -1 if they coincide.
        public static int Octant(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            if (dx * dx + dz * dz < 1e-6f) return -1;
            float yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            return ((int)Mathf.Round(yaw / ArcDegrees) % 8 + 8) % 8;
        }

        // Octant relative to the view: 0 = straight ahead (top edge), +90 = right, ±180 = behind (bottom edge).
        public static float RelativeDegrees(int octant, float viewYaw) => Mathf.DeltaAngle(viewYaw, octant * ArcDegrees);

        // State D: only a living, non-Echo player inside a running round sees hints (not lobby, result, spectating, escaped).
        public static bool CanShow(bool alive, bool echo, bool roundRunning) => alive && !echo && roundRunning;

        // Alpha at `now` of a hint lit at `start` and held until `holdUntil` (start < 0: never lit).
        public static float Alpha(float start, float holdUntil, float now)
        {
            if (start < 0 || now < start) return 0;
            float a = Mathf.Clamp01((now - start) / FadeIn);
            if (now > holdUntil) a *= 1f - Mathf.Clamp01((now - holdUntil) / FadeOut);
            return a * MaxAlpha;
        }

        // Light (or keep lit) one octant slot. Overlapping sounds never add up: the brighter and longer one wins.
        public static void Light(ref float start, ref float holdUntil, float now, float hold)
        {
            float current = Alpha(start, holdUntil, now) / MaxAlpha;
            if (current <= 0f) start = now;
            else if (now > holdUntil) start = now - FadeIn * current;   // fading out: rise again from where it is
            holdUntil = Mathf.Max(now > holdUntil ? now : holdUntil, now + hold);
        }

        public sealed class SourceState { public float NextAllowed = float.NegativeInfinity; public int Octant = -1; }

        public const int Ignore = 0, Show = 1, Extend = 2;

        // One source: a new hint at most every `cooldown`; packets of the same source in the same octant extend it.
        public static int Decide(SourceState state, int octant, float now, float cooldown, bool extendOnRepeat)
        {
            if (now < state.NextAllowed) return extendOnRepeat && octant == state.Octant ? Extend : Ignore;
            state.NextAllowed = now + cooldown;
            state.Octant = octant;
            return Show;
        }

        // Underwater valve: SoundTarget is muffled for every listener, so a 0-radius tick is hidden only when no ring of
        // the same loop arrived within the ring gap.
        public static bool LoopHidden(float lastRingAt, float now) => now - lastRingAt > LoopRingWindow;

        // Screen band for a relative direction: centre (pixels from the screen centre), rotation (degrees, +Y points to
        // the screen edge) and size (length along the edge, depth inward). The band lies on the edge the direction
        // meets and covers that edge between the directions ±22.5°.
        public static void Band(float relativeDegrees, float width, float height, out Vector2 center, out float rotation, out Vector2 size)
        {
            float hw = width * .5f, hh = height * .5f, depth = BandFraction * Mathf.Min(width, height);
            Edge(relativeDegrees, hw, hh, out int side);
            Vector2 a = Edge(relativeDegrees - ArcDegrees * .5f, hw, hh, out _), b = Edge(relativeDegrees + ArcDegrees * .5f, hw, hh, out _);
            bool horizontal = side == 0 || side == 2;
            float alongA = horizontal ? Mathf.Clamp(a.x, -hw, hw) : Mathf.Clamp(a.y, -hh, hh);
            float alongB = horizontal ? Mathf.Clamp(b.x, -hw, hw) : Mathf.Clamp(b.y, -hh, hh);
            float lo = Mathf.Min(alongA, alongB), hi = Mathf.Max(alongA, alongB);
            float mid = (lo + hi) * .5f, length = Mathf.Max(hi - lo, depth);
            switch (side)
            {
                case 0: center = new Vector2(mid, hh - depth * .5f); rotation = 0f; break;      // top: ahead
                case 1: center = new Vector2(hw - depth * .5f, mid); rotation = -90f; break;    // right
                case 2: center = new Vector2(mid, -hh + depth * .5f); rotation = 180f; break;   // bottom: behind
                default: center = new Vector2(-hw + depth * .5f, mid); rotation = 90f; break;   // left
            }
            size = new Vector2(length, depth);
        }

        // Where the ray from the screen centre at `degrees` (0 = up, clockwise) meets the screen border; side 0 top, 1 right, 2 bottom, 3 left.
        public static Vector2 Edge(float degrees, float hw, float hh, out int side)
        {
            float r = degrees * Mathf.Deg2Rad, vx = Mathf.Sin(r), vy = Mathf.Cos(r);
            float tx = Mathf.Abs(vx) > 1e-5f ? hw / Mathf.Abs(vx) : float.PositiveInfinity;
            float ty = Mathf.Abs(vy) > 1e-5f ? hh / Mathf.Abs(vy) : float.PositiveInfinity;
            if (tx < ty) { side = vx > 0 ? 1 : 3; return new Vector2(vx * tx, vy * tx); }
            side = vy > 0 ? 0 : 2;
            return new Vector2(vx * ty, vy * ty);
        }
    }
}
