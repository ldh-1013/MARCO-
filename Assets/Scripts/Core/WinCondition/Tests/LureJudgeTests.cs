using System.Collections.Generic;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    // 기획서 8.3 판정식 · 상황 표 8행 · 경계값. Knock at the origin; the Seeker moves along x, so d = x.
    public class LureJudgeTests
    {
        private static readonly AwardPoint Origin = new(0, 0, 0);
        private static AwardPoint At(float x) => new(x, 0, 0);

        private LureJudge judge;
        private List<int> lured;

        [SetUp]
        public void SetUp()
        {
            judge = new LureJudge();
            lured = new List<int>();
            judge.Succeeded += lured.Add;
        }

        // Samples the Seeker every 0.5 s from `from` (inclusive) with d(t) given by `distance`.
        private void Walk(float from, float to, System.Func<float, float> distance)
        {
            for (float t = from; t <= to + 1e-4f; t += LureJudge.SampleSeconds) judge.SampleSeeker(t, At(distance(t)));
        }

        // Standing still at d before the knock at t = 0 (fills the ② look-back).
        private void StandBefore(float d) => Walk(-3f, -.5f, _ => d);

        [Test]
        public void Approach_ExactlyThreeSeconds_AtNineMeters_Succeeds()
        {
            StandBefore(15);
            Assert.IsTrue(judge.ReportKnock(1, 0, Origin, At(15)));
            // 1 m closer every sample: 14 at .5 s … 9 at 3.0 s → six closing samples = 3.0 s.
            Walk(.5f, 3f, t => 15 - t * 2);
            CollectionAssert.AreEqual(new[] { 1 }, lured);
            Assert.AreEqual(0, judge.ActiveWatchCount);
        }

        [Test]
        public void Row1_PassingBy_ApproachUnderThreeSeconds_Fails()
        {
            StandBefore(14);
            Assert.IsTrue(judge.ReportKnock(1, 0, Origin, At(14)));
            // 2.5 s of closing reaches 9 m: not enough, and staying inside never re-triggers.
            Walk(.5f, 2.5f, t => 14 - t * 2);
            Walk(3f, 10f, _ => 5);
            CollectionAssert.IsEmpty(lured);
        }

        [Test]
        public void Row2_AlreadyHeadingThere_DoesNotStartWatching()
        {
            Walk(-3f, -.5f, t => 12 - t);          // 15 at −3 s … closing
            Assert.IsFalse(judge.ReportKnock(1, 0, Origin, At(11)));   // d(t0)=11 < d(−2 s)=14
            Assert.AreEqual(0, judge.ActiveWatchCount);
        }

        [Test]
        public void Row2_Boundary_SameDistanceAsTwoSecondsAgo_Watches()
        {
            StandBefore(12);
            Assert.IsTrue(judge.ReportKnock(1, 0, Origin, At(12)));    // d(t0) >= d(t0 − 2) holds at equality
        }

        [Test]
        public void Row2_NoHistory_IsTakenAsMet()
        {
            Assert.IsTrue(judge.ReportKnock(1, 0, Origin, At(12)));
        }

        [Test]
        public void Row3_AwayThenBack_SucceedsOnTheReturn()
        {
            StandBefore(15);
            Assert.IsTrue(judge.ReportKnock(1, 0, Origin, At(15)));
            Walk(.5f, 2f, t => 15 + t * 2);        // away to 19
            Walk(2.5f, 7.5f, t => 19 - (t - 2f) * 2); // back: 18 … 8
            CollectionAssert.AreEqual(new[] { 1 }, lured);
        }

        [Test]
        public void Row4_SeveralKnocksEnteredTogether_OnlyTheLatestCounts()
        {
            StandBefore(30);
            Assert.IsTrue(judge.ReportKnock(1, 0, Origin, At(30)));
            Assert.IsTrue(judge.ReportKnock(2, .2f, new AwardPoint(0, 0, 2), At(30)));  // 2 m apart, a moment later
            // 2 m per sample: at x = 8 both are within 9 m for the first time (8 and √68 ≈ 8.25).
            Walk(.5f, 10f, t => 30 - t * 4);
            CollectionAssert.AreEqual(new[] { 2 }, lured, "most recent knock of the ones entered at once");
            Assert.AreEqual(0, judge.ActiveWatchCount);
        }

        [Test]
        public void Row4_KnocksReachedAtDifferentMoments_CountSeparately()
        {
            StandBefore(30);
            Assert.IsTrue(judge.ReportKnock(1, 0, Origin, At(30)));
            Assert.IsTrue(judge.ReportKnock(2, 0, At(-20), At(30)));    // 50 m away on the far side
            Walk(.5f, 30f, t => 30 - t * 2);                             // passes the origin, keeps going
            CollectionAssert.AreEqual(new[] { 1, 2 }, lured);
        }

        [Test]
        public void Row5_SameSpotAgain_ReplacesTheActiveWatch()
        {
            StandBefore(20);
            Assert.IsTrue(judge.ReportKnock(1, 0, Origin, At(20)));
            Walk(.5f, 2f, _ => 20);
            Assert.IsTrue(judge.ReportKnock(3, 2.2f, new AwardPoint(.5f, 0, 0), At(20)));   // .5 m away: same spot
            Assert.AreEqual(1, judge.ActiveWatchCount);
            Walk(2.5f, 8f, t => 20 - (t - 2f) * 2);
            CollectionAssert.AreEqual(new[] { 3 }, lured, "the newest knock at that spot is the one judged");
        }

        [Test]
        public void Row5_JustOutsideTheSameSpot_IsASecondWatch()
        {
            StandBefore(20);
            judge.ReportKnock(1, 0, Origin, At(20));
            judge.ReportKnock(1, 1, new AwardPoint(0, 0, LureJudge.SamePositionMeters + .01f), At(20));
            Assert.AreEqual(2, judge.ActiveWatchCount);
        }

        [Test]
        public void Row6_SeekerTags_EndsEveryWatch()
        {
            StandBefore(15);
            judge.ReportKnock(1, 0, Origin, At(15));
            Walk(.5f, 2f, t => 15 - t * 2);
            judge.SeekerTagged();
            Walk(2.5f, 6f, t => 15 - t * 2);
            CollectionAssert.IsEmpty(lured);
            Assert.AreEqual(0, judge.ActiveWatchCount);
        }

        [Test]
        public void Row7_NobodyComes_ExpiresAfterThirtySeconds()
        {
            StandBefore(20);
            judge.ReportKnock(1, 0, Origin, At(20));
            Walk(.5f, 30f, _ => 20);
            Assert.AreEqual(1, judge.ActiveWatchCount, "still watching at exactly 30 s");
            judge.SampleSeeker(30.5f, At(20));
            Assert.AreEqual(0, judge.ActiveWatchCount);
            CollectionAssert.IsEmpty(lured);
        }

        [Test]
        public void Row7_Boundary_EnteringAtThirtySeconds_StillCounts()
        {
            StandBefore(20);
            judge.ReportKnock(1, 0, Origin, At(20));
            Walk(.5f, 24f, _ => 20);
            Walk(24.5f, 30f, t => 20 - (t - 24f) * (11f / 6f));   // 9 m exactly at 30 s, approach 6 s
            CollectionAssert.AreEqual(new[] { 1 }, lured);
        }

        [Test]
        public void Row8_AlreadyWithinNineMeters_DoesNotStartWatching()
        {
            StandBefore(8);
            Assert.IsFalse(judge.ReportKnock(1, 0, Origin, At(8)));
        }

        [Test]
        public void Row8_Boundary_ExactlyNineMeters_DoesNotStart_JustOutsideDoes()
        {
            Assert.IsFalse(judge.ReportKnock(1, 0, Origin, At(9f)));
            Assert.IsTrue(judge.ReportKnock(1, 0, Origin, At(9.01f)));
        }

        [Test]
        public void StandingStill_DoesNotAccumulateApproach()
        {
            StandBefore(12);
            judge.ReportKnock(1, 0, Origin, At(12));
            Walk(.5f, 10f, _ => 12);
            Walk(10.5f, 11f, t => 12 - (t - 10f) * 6);   // inside at 10.5 s with only 0.5 s of approach
            CollectionAssert.IsEmpty(lured);
        }

        [Test]
        public void RoundEnd_StopsJudging()
        {
            StandBefore(15);
            judge.ReportKnock(1, 0, Origin, At(15));
            Walk(.5f, 2f, t => 15 - t * 2);
            judge.Stop();
            Walk(2.5f, 6f, t => 15 - t * 2);
            CollectionAssert.IsEmpty(lured);
            Assert.IsFalse(judge.ReportKnock(1, 7, Origin, At(30)));
        }

        [Test]
        public void ASuccessIsAnnouncedOnce()
        {
            StandBefore(15);
            judge.ReportKnock(1, 0, Origin, At(15));
            Walk(.5f, 3f, t => 15 - t * 2);
            Walk(3.5f, 6f, t => t < 4.5f ? 12 : 5);    // out and back in again
            CollectionAssert.AreEqual(new[] { 1 }, lured);
        }
    }
}
