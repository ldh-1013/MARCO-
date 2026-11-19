using System;
using System.Linq;
using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    // 리매치 투표(기획서 8 · 12.5 · 14.2 · 15.4): 15초, 찬성만, 1인 1표, 취소 없음, 전체 접속자 수의 절반 초과면 즉시 가결.
    public class RematchVoteTests
    {
        private const float Tolerance = 0.0001f;

        private static RematchVote NewVote(int voters) => new RematchVote(Enumerable.Range(1, voters));

        private static void CastYes(RematchVote vote, int count)
        {
            for (int id = 1; id <= count; id++) vote.CastYes(id);
        }

        [Test]
        public void Defaults_FifteenSecondsPendingNoVotes()
        {
            var vote = NewVote(4);

            Assert.AreEqual(15f, RematchVote.DefaultDurationSeconds, Tolerance);
            Assert.AreEqual(15f, vote.RemainingSeconds, Tolerance);
            Assert.AreEqual(RematchVoteOutcome.Pending, vote.Outcome);
            Assert.AreEqual(0, vote.YesCount);
            Assert.AreEqual(4, vote.VoterCount);
        }

        // 과반 경계: 2명 2, 3명 2, 4명 3, 5명 3, 6명 4.
        [TestCase(2, 2)]
        [TestCase(3, 2)]
        [TestCase(4, 3)]
        [TestCase(5, 3)]
        [TestCase(6, 4)]
        public void Majority_PassesExactlyAtBoundary(int voters, int needed)
        {
            var vote = NewVote(voters);

            CastYes(vote, needed - 1);
            Assert.AreEqual(RematchVoteOutcome.Pending, vote.Outcome, "one short of a majority");

            Assert.IsTrue(vote.CastYes(needed));
            Assert.AreEqual(RematchVoteOutcome.Passed, vote.Outcome);
            Assert.AreEqual(needed, vote.YesCount);
        }

        [TestCase(2, 1, false)]
        [TestCase(4, 2, false)]
        [TestCase(6, 3, false)]
        [TestCase(3, 2, true)]
        [TestCase(5, 3, true)]
        [TestCase(0, 0, false)]
        public void IsMajority_StrictlyMoreThanHalf(int voters, int yes, bool expected)
        {
            Assert.AreEqual(expected, RematchVote.IsMajority(yes, voters));
        }

        [Test]
        public void DuplicateVote_IsIgnored()
        {
            var vote = NewVote(4);

            Assert.IsTrue(vote.CastYes(1));
            Assert.IsFalse(vote.CastYes(1));
            Assert.IsFalse(vote.CastYes(1));

            Assert.AreEqual(1, vote.YesCount);
            Assert.IsTrue(vote.HasVoted(1));
            Assert.AreEqual(RematchVoteOutcome.Pending, vote.Outcome);
        }

        [Test]
        public void NonVoter_IsIgnored()
        {
            var vote = NewVote(2);

            Assert.IsFalse(vote.CastYes(99));
            Assert.AreEqual(0, vote.YesCount);
        }

        [Test]
        public void VoteAfterPass_IsIgnored()
        {
            var vote = NewVote(3);
            CastYes(vote, 2);

            Assert.IsFalse(vote.CastYes(3));
            Assert.AreEqual(2, vote.YesCount);
            Assert.AreEqual(RematchVoteOutcome.Passed, vote.Outcome);
        }

        [Test]
        public void TimeRunsOut_WithoutMajority_Fails()
        {
            var vote = NewVote(4);
            CastYes(vote, 2);

            vote.Tick(15f);

            Assert.AreEqual(RematchVoteOutcome.Failed, vote.Outcome);
            Assert.AreEqual(0f, vote.RemainingSeconds, Tolerance);
        }

        [Test]
        public void JustBeforeFifteenSeconds_StillOpen_AndVoteCanPass()
        {
            var vote = NewVote(2);
            vote.CastYes(1);

            vote.Tick(14.99f);
            Assert.AreEqual(RematchVoteOutcome.Pending, vote.Outcome);
            Assert.AreEqual(0.01f, vote.RemainingSeconds, 0.001f);

            Assert.IsTrue(vote.CastYes(2));
            Assert.AreEqual(RematchVoteOutcome.Passed, vote.Outcome);
        }

        [Test]
        public void AtFifteenSeconds_Fails_AndLaterVoteIsIgnored()
        {
            var vote = NewVote(2);
            vote.CastYes(1);

            vote.Tick(10f);
            vote.Tick(5f);
            Assert.AreEqual(RematchVoteOutcome.Failed, vote.Outcome);

            Assert.IsFalse(vote.CastYes(2));
            Assert.AreEqual(RematchVoteOutcome.Failed, vote.Outcome);
        }

        [Test]
        public void Pass_IsNotUndoneByTimeRunningOut()
        {
            var vote = NewVote(2);
            CastYes(vote, 2);

            vote.Tick(20f);

            Assert.AreEqual(RematchVoteOutcome.Passed, vote.Outcome);
        }

        [Test]
        public void Disconnect_OfNonVoter_LowersTotal_AndCanPass()
        {
            // 4명 중 2명 찬성(과반 아님) → 반대 측 1명 이탈 → 3명 중 2명 = 과반.
            var vote = NewVote(4);
            CastYes(vote, 2);
            Assert.AreEqual(RematchVoteOutcome.Pending, vote.Outcome);

            vote.RemoveVoter(4);

            Assert.AreEqual(3, vote.VoterCount);
            Assert.AreEqual(RematchVoteOutcome.Passed, vote.Outcome);
        }

        [Test]
        public void Disconnect_OfYesVoter_RemovesTheirVote()
        {
            // 5명 중 2명 찬성 → 찬성자 1명 이탈 → 4명 중 1명.
            var vote = NewVote(5);
            CastYes(vote, 2);

            vote.RemoveVoter(1);

            Assert.AreEqual(4, vote.VoterCount);
            Assert.AreEqual(1, vote.YesCount);
            Assert.IsFalse(vote.HasVoted(1));
            Assert.AreEqual(RematchVoteOutcome.Pending, vote.Outcome);
        }

        [Test]
        public void Disconnect_OfUnknownPlayer_ChangesNothing()
        {
            var vote = NewVote(3);
            vote.CastYes(1);

            vote.RemoveVoter(42);

            Assert.AreEqual(3, vote.VoterCount);
            Assert.AreEqual(1, vote.YesCount);
            Assert.AreEqual(RematchVoteOutcome.Pending, vote.Outcome);
        }

        [Test]
        public void Disconnect_AfterDecision_ChangesNothing()
        {
            var vote = NewVote(3);
            CastYes(vote, 2);

            vote.RemoveVoter(1);

            Assert.AreEqual(RematchVoteOutcome.Passed, vote.Outcome);
            Assert.AreEqual(3, vote.VoterCount);
        }

        [Test]
        public void EveryoneLeaves_FailsWhenTimeRunsOut()
        {
            var vote = NewVote(2);
            vote.RemoveVoter(1);
            vote.RemoveVoter(2);
            Assert.AreEqual(RematchVoteOutcome.Pending, vote.Outcome);

            vote.Tick(15f);

            Assert.AreEqual(RematchVoteOutcome.Failed, vote.Outcome);
        }

        [Test]
        public void LateJoiner_RaisesTotal()
        {
            // 3명 중 1명 찬성 → 1명 합류(4명) → 2명 찬성은 과반 아님.
            var vote = NewVote(3);
            vote.CastYes(1);

            vote.AddVoter(4);
            vote.CastYes(2);

            Assert.AreEqual(4, vote.VoterCount);
            Assert.AreEqual(RematchVoteOutcome.Pending, vote.Outcome);
            Assert.IsTrue(vote.CastYes(4));
            Assert.AreEqual(RematchVoteOutcome.Passed, vote.Outcome);
        }

        [Test]
        public void InvalidArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => new RematchVote(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RematchVote(new[] { 1 }, 0f));
        }
    }
}
