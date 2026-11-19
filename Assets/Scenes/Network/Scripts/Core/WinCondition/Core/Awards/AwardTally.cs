using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // Per-Runner award counters for one round (기획서 8). The server registers the round's Runners at
    // InGame start and reports sounds, movement, escapes, tags and lures; AwardJudge reads it at round end.
    //
    // Freezing (3.3-3 · 8): an escape or a tag ends a Runner's sounds and movement — whatever an Echo body
    // does afterwards is ignored. Lures are the one exception: only an eliminated Echo can lure (8.3).
    // Anyone not registered (the Seeker, mid-round joiners) is ignored.
    public sealed class AwardTally
    {
        private sealed class Record
        {
            public AwardPlayerState State;
            public int Screams;
            public double Noise;
            public double Distance;
            public int Lures;
        }

        private readonly Dictionary<int, Record> runners = new();

        public IEnumerable<int> RunnerIds => runners.Keys;

        public void AddRunner(int playerId)
        {
            if (!runners.ContainsKey(playerId)) runners[playerId] = new Record();
        }

        // Left mid-round: out of every award (they are not on the result screen either).
        public void Remove(int playerId) => runners.Remove(playerId);

        public bool IsRunner(int playerId) => runners.ContainsKey(playerId);

        // radius × duration once per pulse, however many listeners heard it (8.2 중복 집계 금지).
        public void RecordNoise(int playerId, AwardNoise kind, float radius, float duration)
        {
            if (!TryLive(playerId, out var record)) return;
            if (kind == AwardNoise.Scream) record.Screams++;
            if (!AwardRules.CountsForSilence(kind)) return;
            if (!IsFinite(radius) || !IsFinite(duration) || radius <= 0f || duration <= 0f) return;
            record.Noise += (double)radius * duration;
        }

        public void RecordDistance(int playerId, float meters)
        {
            if (!TryLive(playerId, out var record) || !IsFinite(meters) || meters <= 0f) return;
            record.Distance += meters;
        }

        public void MarkEscaped(int playerId) => Decide(playerId, AwardPlayerState.Escaped);
        public void MarkTagged(int playerId) => Decide(playerId, AwardPlayerState.Tagged);

        // A lure judged successful (LureJudge). Counted only for an eliminated Echo.
        public void RecordLure(int playerId)
        {
            if (runners.TryGetValue(playerId, out var record) && record.State == AwardPlayerState.Tagged)
                record.Lures++;
        }

        public AwardPlayerState StateOf(int playerId) => runners.TryGetValue(playerId, out var r) ? r.State : AwardPlayerState.Undecided;
        public int ScreamsOf(int playerId) => runners.TryGetValue(playerId, out var r) ? r.Screams : 0;
        public double NoiseOf(int playerId) => runners.TryGetValue(playerId, out var r) ? r.Noise : 0;
        public double DistanceOf(int playerId) => runners.TryGetValue(playerId, out var r) ? r.Distance : 0;
        public int LuresOf(int playerId) => runners.TryGetValue(playerId, out var r) ? r.Lures : 0;

        private void Decide(int playerId, AwardPlayerState state)
        {
            // The first decision sticks (escape and tag never both happen — 6.3 탈출 우선).
            if (runners.TryGetValue(playerId, out var record) && record.State == AwardPlayerState.Undecided)
                record.State = state;
        }

        private bool TryLive(int playerId, out Record record) =>
            runners.TryGetValue(playerId, out record) && record.State == AwardPlayerState.Undecided;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
