using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // 기획서 8.3 최고의 낚시상 — knock lure judgment, a heuristic on distance and time only.
    //
    //   d(t) = distance from the Seeker to the knock position
    //   start watching at the knock t0 only if  ① d(t0) > 9 m  and  ② d(t0) >= d(t0 − 2 s)
    //   then every 0.5 s for 30 s:  approach += 0.5 s whenever d shrank since the last sample;
    //   reaching d <= 9 m with approach >= 3.0 s = lure succeeded.
    //   Every watch fails on: 30 s passing, the Seeker tagging a Runner, the round ending.
    //
    // Situation table rows (8.3) and where each lives here:
    //   1 passing by — approach under 3.0 s at entry: no success (the watch keeps going until 30 s)
    //   2 already heading there — ② fails, no watch
    //   3 away then back — approach only grows on closing samples, so the return can still reach 3.0 s
    //   4 several knocks entered at once — only the most recent of them is counted (and announced)
    //   5 knocks again at the same spot — the active watch there is replaced by the newest
    //   6 Seeker tags — SeekerTagged ends every watch
    //   7 nobody comes — expires after 30 s
    //   8 already within 9 m — ① fails, no watch
    //
    // The server feeds SampleSeeker every 0.5 s and ReportKnock when a knock actually sounds (after its
    // 1.5 s delay, 3.2). Succeeded fires with the knocking Echo's id.
    public sealed class LureJudge
    {
        public const float EntryRadius = 9f;        // = knock radius (3.2)
        public const float WatchSeconds = 30f;
        public const float PreWindowSeconds = 2f;   // ② look-back
        public const float SampleSeconds = .5f;
        public const float ApproachSeconds = 3f;
        // Row 5 "같은 위치": knocks within this distance of an active watch are the same spot. Not in the
        // spec's constant table — an interpretation constant (one footstep), kept apart on purpose.
        public const float SamePositionMeters = 1f;

        private const float Epsilon = 1e-4f;

        private sealed class Watch
        {
            public int EchoId;
            public AwardPoint Position;
            public float StartedAt;
            public float LastDistance;
            public float Approach;
            public bool Inside;
        }

        private readonly List<Watch> watches = new();
        private readonly List<(float time, AwardPoint position)> history = new();
        private readonly List<Watch> entered = new();

        public event Action<int> Succeeded;
        public bool IsStopped { get; private set; }
        public int ActiveWatchCount => watches.Count;

        // The Seeker's position at a 0.5 s sample. Advances every watch.
        public void SampleSeeker(float time, AwardPoint seeker)
        {
            if (IsStopped) return;
            history.Add((time, seeker));
            float keepFrom = time - PreWindowSeconds - 2 * SampleSeconds;
            while (history.Count > 0 && history[0].time < keepFrom) history.RemoveAt(0);

            entered.Clear();
            for (int i = watches.Count - 1; i >= 0; i--)
            {
                var watch = watches[i];
                if (time - watch.StartedAt > WatchSeconds + Epsilon) { watches.RemoveAt(i); continue; }   // row 7
                float distance = AwardPoint.Distance(seeker, watch.Position);
                if (distance < watch.LastDistance) watch.Approach += SampleSeconds;                        // ③
                watch.LastDistance = distance;
                bool inside = distance <= EntryRadius;
                if (inside && !watch.Inside && watch.Approach >= ApproachSeconds - Epsilon) entered.Add(watch); // ④
                watch.Inside = inside;
            }
            if (entered.Count == 0) return;

            // Row 4: of the knocks reached at this moment, only the most recent one counts.
            Watch latest = entered[0];
            foreach (var watch in entered)
                if (watch.StartedAt > latest.StartedAt) latest = watch;
            foreach (var watch in entered) watches.Remove(watch);
            Succeeded?.Invoke(latest.EchoId);
        }

        // A knock sounded at position, with the Seeker at seekerNow. Returns whether a watch started.
        public bool ReportKnock(int echoId, float time, AwardPoint position, AwardPoint seekerNow)
        {
            if (IsStopped) return false;
            float distance = AwardPoint.Distance(seekerNow, position);
            if (distance <= EntryRadius) return false;                                                  // ① · row 8
            if (TryDistanceAt(time - PreWindowSeconds, position, out float before) && distance < before)
                return false;                                                                           // ② · row 2
            for (int i = watches.Count - 1; i >= 0; i--)                                                // row 5
                if (AwardPoint.Distance(watches[i].Position, position) <= SamePositionMeters) watches.RemoveAt(i);
            watches.Add(new Watch { EchoId = echoId, Position = position, StartedAt = time, LastDistance = distance });
            return true;
        }

        // Row 6: the Seeker landed a tag (first hit or Echo) — every watch fails.
        public void SeekerTagged() => watches.Clear();

        // Round over: every watch fails and nothing more is judged.
        public void Stop()
        {
            IsStopped = true;
            watches.Clear();
            history.Clear();
        }

        // d(t0 − 2 s) from the newest sample at or before that time (half a sample of slack). No such sample
        // (the round just began) = unknown, and ② is taken as met.
        private bool TryDistanceAt(float time, AwardPoint position, out float distance)
        {
            distance = 0;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].time > time + SampleSeconds * .5f) continue;
                distance = AwardPoint.Distance(history[i].position, position);
                return true;
            }
            return false;
        }
    }
}
