using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    public enum RematchVoteOutcome
    {
        Pending,
        // Yes votes went above half of the voters: restart at once (role redraw, 15.4 RoundEnd → RoleAssign).
        Passed,
        // The time ran out without a majority: back to the lobby (15.4 RoundEnd → Lobby).
        Failed,
    }

    // Rematch vote on the result screen (기획서 8 · 12.5 · 14.2): 15 s, yes only, one vote each, no
    // cancelling. The voters are everyone connected; the server adds joiners and removes leavers.
    // Majority = yes > half of the voters. Once decided, the outcome never changes.
    public sealed class RematchVote
    {
        public const float DefaultDurationSeconds = 15f;

        private readonly HashSet<int> voters = new();
        private readonly HashSet<int> yes = new();

        public RematchVote(IEnumerable<int> voterIds, float durationSeconds = DefaultDurationSeconds)
        {
            if (voterIds == null) throw new ArgumentNullException(nameof(voterIds));
            if (durationSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            foreach (int id in voterIds) voters.Add(id);
            RemainingSeconds = durationSeconds;
        }

        public RematchVoteOutcome Outcome { get; private set; } = RematchVoteOutcome.Pending;
        public bool IsDecided => Outcome != RematchVoteOutcome.Pending;
        public float RemainingSeconds { get; private set; }
        public int YesCount => yes.Count;
        public int VoterCount => voters.Count;
        public IReadOnlyCollection<int> VoterIds => voters;

        public bool HasVoted(int playerId) => yes.Contains(playerId);

        public static bool IsMajority(int yesCount, int voterCount) => voterCount > 0 && yesCount * 2 > voterCount;

        // False when the vote is over, the player is not a voter, or they already voted.
        public bool CastYes(int playerId)
        {
            if (IsDecided || !voters.Contains(playerId) || !yes.Add(playerId)) return false;
            Evaluate();
            return true;
        }

        // A late joiner counts toward the total too ("전체 접속자 수"), which can only delay a pass.
        public void AddVoter(int playerId)
        {
            if (IsDecided) return;
            voters.Add(playerId);
        }

        // A leaver's vote goes with them; the smaller total can make the remaining yes votes a majority.
        public void RemoveVoter(int playerId)
        {
            if (IsDecided || !voters.Remove(playerId)) return;
            yes.Remove(playerId);
            Evaluate();
        }

        // A pass decided earlier in the same step wins over the time running out.
        public void Tick(float dt)
        {
            if (IsDecided || dt <= 0f) return;
            RemainingSeconds = Math.Max(0f, RemainingSeconds - dt);
            if (RemainingSeconds <= 0f) Outcome = RematchVoteOutcome.Failed;
        }

        private void Evaluate()
        {
            if (IsMajority(yes.Count, voters.Count)) Outcome = RematchVoteOutcome.Passed;
        }
    }
}
