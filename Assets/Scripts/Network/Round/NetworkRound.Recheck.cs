using Marco.Core.WinCondition;
using Marco.Network.Audio;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Round
{
    // 부록 A [사운드 차폐] 차폐 재판정 주기 0.25초 · 재판정 상향 규칙 [11-16] — wiring of OcclusionRecheck to the round's
    // players and SoundWorld. Paths: EmitSound (non-looping), shout · scream (PlayerMovement.EmitAbilitySound), drowning ring.
    public sealed partial class NetworkRound
    {
        private readonly OcclusionRecheck recheck = new();
        private double recheckWindowMs, recheckWindowMaxMs;
        private int recheckWindowTicks, recheckWindowChecks, recheckWindowSends, recheckWindowMaxPulses;
        private float recheckReportAt;

        internal RecheckPulse BeginRecheck(Vector3 origin, float radius, float baseSeconds) =>
            recheck.Begin(origin, radius, baseSeconds, Time.time);

        internal void CommitRecheck(RecheckPulse pulse) => recheck.Commit(pulse);

        private void TickRecheck()
        {
            if (recheck.Count == 0) return;
            soundWorld ??= new SoundWorld();
            recheck.Upgraded ??= (pulse, listener, from, to, elapsed) =>
                Debug.Log($"[MARCO][Recheck] upgrade pulse {pulse.Id} listener {listener} {from}->{to} elapsed {elapsed:F2}s");
            int checks = recheck.Rechecks, sends = recheck.Sends, pulses = recheck.Count;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!recheck.Tick(Time.time, RecheckListener, RecheckHear)) return;
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            recheckWindowTicks++;
            recheckWindowMs += ms;
            recheckWindowMaxMs = System.Math.Max(recheckWindowMaxMs, ms);
            recheckWindowChecks += recheck.Rechecks - checks;
            recheckWindowSends += recheck.Sends - sends;
            recheckWindowMaxPulses = Mathf.Max(recheckWindowMaxPulses, pulses);
            if (Time.time < recheckReportAt) return;
            recheckReportAt = Time.time + 10f;
            Debug.Log($"[MARCO][Recheck] window: ticks={recheckWindowTicks} rechecks={recheckWindowChecks} sends={recheckWindowSends} " +
                $"maxPulses={recheckWindowMaxPulses} avgMs={recheckWindowMs / Mathf.Max(1, recheckWindowTicks):F3} maxMs={recheckWindowMaxMs:F3}");
            recheckWindowMs = recheckWindowMaxMs = 0;
            recheckWindowTicks = recheckWindowChecks = recheckWindowSends = recheckWindowMaxPulses = 0;
        }

        // Still followed: present, a target connection, alive and not an Echo body. The value is "is a Seeker".
        private bool? RecheckListener(int id) =>
            NetworkPlayer.TryGet(id, out var listener) && listener != null && CanTarget(listener) &&
            listener.Status == PlayerStatus.Alive && !listener.HasEchoBody
                ? listener.Role == PlayerRole.Seeker : (bool?)null;

        private bool RecheckHear(int id, Vector3 origin, float radius, out HearSample sample)
        {
            sample = default;
            if (!NetworkPlayer.TryGet(id, out var listener) || listener == null) return false;
            sample.Heard = soundWorld.HearResolved(origin, listener, radius, out sample.Point, out sample.Gain, out sample.Blocked,
                out sample.PerceivedRadius, out sample.DurationScale);
            return sample.Heard;
        }
    }
}
