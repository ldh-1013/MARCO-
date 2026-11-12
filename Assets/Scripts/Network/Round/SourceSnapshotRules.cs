using System;
using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Round
{
    // §5.5-2 발생원 스냅샷 · §14.2 [10-05] showSourceSnapshot — which character a listener may see in the frozen
    // silhouette of a ring-display sound. Server only; the result rides at the end of the existing per-listener
    // TargetRpc (SoundTarget · VoicePulseTarget · AbilitySoundTarget · DrowningPulseTarget) as a PlayerId, or None.
    //   R1 only character sources of ring-display sounds (whitelist below; voice and the ability vocals by path)
    //   R2 only when that listener got the ring unoccluded (blocked == false) — no source for an occluded sound (§5.4)
    //   R3 only living listeners without an Echo body; never the listener's own body; never a source that is not Alive
    // Fail-closed: anything unexpected — a missing player, an exception — gives None, and never stops the listener
    // loop or the sound, ring and hint of that RPC.
    public static class SourceSnapshotRules
    {
        public const int None = -1;
        public const int FakeRunner = -2;   // §5.5-2 메아리 노크: Runner idle pose at the knock point (not sent yet)

        // R1 · §5.5-1 ring-display sounds whose source is a character, reaching listeners through EmitSound:
        // Moan (the wounded Runner), Bandage (the user), Treat (healer and patient), Valve (the Runners turning a
        // ground valve — interpretation, see the report). Not listed → no snapshot: Walk · Sprint · swim (floor
        // rings), Breath (sound only), world sources (tag pulse, gate, reflow, underwater valve, drain, exit,
        // last-survivor notice) and items. Drowning (the drowning Runner) is decided at DrowningPulseTarget, its own ring path.
        public static bool IsCharacterSound(string clip) =>
            clip != null && (clip == "Bandage" || clip == "Treat" || clip == "Valve" || clip.StartsWith("Moan_", StringComparison.Ordinal));

        // Pure part of R2 · R3.
        public static int Decide(int sourceId, bool sourceAlive, int listenerId, bool listenerAlive, bool listenerEchoBody, bool blocked) =>
            sourceId >= 0 && sourceAlive && sourceId != listenerId && listenerAlive && !listenerEchoBody && !blocked ? sourceId : None;

        // Server: the snapshot value for one source and one listener. Never throws.
        public static int ForListener(int sourceId, NetworkPlayer listener, bool blocked)
        {
            try
            {
                if (sourceId < 0 || listener == null || blocked) return None;
                if (!NetworkPlayer.TryGet(sourceId, out var source) || source == null) return None;
                return Decide(sourceId, source.Status == PlayerStatus.Alive, listener.PlayerId,
                    listener.Status == PlayerStatus.Alive, listener.HasEchoBody, blocked);
            }
            catch (Exception exception)
            {
                Report("ForListener", exception);
                return None;
            }
        }

        static readonly HashSet<string> reported = new();

        // One log per kind (S1).
        public static void Report(string where, Exception exception)
        {
            try
            {
                if (reported.Add(where + ":" + exception.GetType().Name)) Debug.LogException(exception);
            }
            catch { }
        }
    }
}
