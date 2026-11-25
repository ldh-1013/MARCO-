using System;
using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network.Round
{
    // One listener's hearing result (SoundWorld.HearResolved).
    public struct HearSample
    {
        public bool Heard, Blocked;
        public Vector3 Point;
        public float Gain, PerceivedRadius, DurationScale;
    }

    // A pulse being re-judged. The position is fixed at emission (5.6 pulse.position); only listeners move.
    public sealed class RecheckPulse
    {
        public int Id;
        public Vector3 Origin;
        public float Radius, BaseSeconds, Started, Lifetime;
        public readonly Dictionary<int, HearState> States = new();
        // listener id, the new hearing, elapsed seconds — the path's own resend (same values as at emission otherwise).
        public Action<int, HearSample, float> Send;

        public void Note(int listener, HearState state)
        {
            if (state != HearState.Clear) States[listener] = state;
        }
    }

    // 부록 A [사운드 차폐] 차폐 재판정 주기 0.25초 · 재판정 상향 규칙 [11-16] — server side. Plain class (no network) so the
    // timing and the upward-only rule are testable with a fake clock and hearing.
    public sealed class OcclusionRecheck
    {
        public delegate bool Hear(int listener, Vector3 origin, float radius, out HearSample sample);

        private readonly List<RecheckPulse> pulses = new();
        private readonly List<int> keys = new();
        private int nextId = 1;
        private float nextTick;

        public int Count => pulses.Count;
        public int Rechecks, Upgrades, Sends;
        public Action<RecheckPulse, int, HearState, HearState, float> Upgraded;

        public RecheckPulse Begin(Vector3 origin, float radius, float baseSeconds, float now)
        {
            int id = nextId;
            nextId = nextId == int.MaxValue ? 1 : nextId + 1;
            return new RecheckPulse
            {
                Id = id, Origin = origin, Radius = radius, BaseSeconds = baseSeconds, Started = now,
                // The longest any listener can perceive it: Seeker ×1.5, unoccluded (an Echo body is never followed).
                Lifetime = PulseSpec.ListenerSeconds(baseSeconds, true, false, true, 1f)
            };
        }

        public void Commit(RecheckPulse pulse)
        {
            if (pulse != null && pulse.Send != null && pulse.States.Count > 0) pulses.Add(pulse);
        }

        public void Clear()
        {
            pulses.Clear();
            nextTick = 0f;
        }

        // seeker(listener): true / false = still followed (Seeker or not); null = gone, not a target any more, not alive or an
        // Echo body (then it is dropped). Returns false when it is not yet time for the next recheck.
        public bool Tick(float now, Func<int, bool?> seeker, Hear hear)
        {
            if (now < nextTick) return false;
            nextTick = now + PulseSpec.OcclusionRecheckSeconds;
            for (int i = pulses.Count - 1; i >= 0; i--)
            {
                var pulse = pulses[i];
                float elapsed = now - pulse.Started;
                if (elapsed >= pulse.Lifetime) { pulses.RemoveAt(i); continue; }
                keys.Clear();
                keys.AddRange(pulse.States.Keys);
                foreach (int listener in keys)
                {
                    bool? isSeeker = seeker(listener);
                    if (isSeeker == null) { pulse.States.Remove(listener); continue; }
                    var old = pulse.States[listener];
                    Rechecks++;
                    bool heard = hear(listener, pulse.Origin, pulse.Radius, out var sample);
                    var state = RecheckRules.State(heard, sample.Blocked);
                    if (!RecheckRules.Upgrade(old, state)) continue;
                    float total = PulseSpec.ListenerSeconds(pulse.BaseSeconds, isSeeker.Value, false, true, sample.DurationScale);
                    if (!RecheckRules.Send(old, state, total, elapsed)) continue;
                    Upgrades++;
                    if (state == HearState.Clear) pulse.States.Remove(listener);
                    else pulse.States[listener] = state;
                    Upgraded?.Invoke(pulse, listener, old, state, elapsed);
                    pulse.Send(listener, sample, elapsed);
                    Sends++;
                }
                if (pulse.States.Count == 0) pulses.RemoveAt(i);
            }
            return true;
        }
    }
}
