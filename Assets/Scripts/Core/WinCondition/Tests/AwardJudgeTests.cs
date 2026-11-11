using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    // 기획서 8.1 · 8.2 · 8.3 수상 판정 + 3.3-3 탈출 시점 집계 고정 · 메아리 몸 행동 무시.
    public class AwardJudgeTests
    {
        private static AwardTally Runners(params int[] ids)
        {
            var tally = new AwardTally();
            foreach (int id in ids) tally.AddRunner(id);
            return tally;
        }

        // ---------------------------------------------------------------- 8.1 최다 비명상

        [Test]
        public void MostScreams_CountsScreamEventsOnly()
        {
            var tally = Runners(1, 2);
            tally.RecordNoise(1, AwardNoise.Scream, 9, 1);
            tally.RecordNoise(1, AwardNoise.Scream, 9, 1);
            tally.RecordNoise(2, AwardNoise.Scream, 9, 1);
            tally.RecordNoise(2, AwardNoise.Voice, 22, 2.5f);   // Shout: never counted (8.1)
            tally.RecordNoise(2, AwardNoise.Voice, 22, 2.5f);
            tally.RecordNoise(2, AwardNoise.Moan, 9, 1.2f);     // Moan: never counted
            var award = AwardJudge.MostScreams(tally);
            CollectionAssert.AreEqual(new[] { 1 }, award.Winners);
            Assert.AreEqual(2f, award.Value);
        }

        [Test]
        public void MostScreams_TieIsShared()
        {
            var tally = Runners(3, 1, 2);
            foreach (int id in new[] { 1, 3 }) { tally.RecordNoise(id, AwardNoise.Scream, 9, 1); tally.RecordNoise(id, AwardNoise.Scream, 9, 1); }
            tally.RecordNoise(2, AwardNoise.Scream, 9, 1);
            CollectionAssert.AreEqual(new[] { 1, 3 }, AwardJudge.MostScreams(tally).Winners, "both, in id order");
        }

        [Test]
        public void MostScreams_NoScreams_NoWinner()
        {
            var award = AwardJudge.MostScreams(Runners(1, 2));
            Assert.IsFalse(award.HasWinner);
            Assert.AreEqual(0f, award.Value);
        }

        [Test]
        public void MostScreams_UnregisteredPlayersAreIgnored()
        {
            var tally = Runners(1);
            tally.RecordNoise(99, AwardNoise.Scream, 9, 1);   // Seeker or mid-round joiner
            Assert.IsFalse(AwardJudge.MostScreams(tally).HasWinner);
        }

        [Test]
        public void MostScreams_TaggedRunnerKeepsScreamsFromBeforeTheTag()
        {
            var tally = Runners(1, 2);
            tally.RecordNoise(1, AwardNoise.Scream, 9, 1);
            tally.MarkTagged(1);
            CollectionAssert.AreEqual(new[] { 1 }, AwardJudge.MostScreams(tally).Winners);
        }

        // ---------------------------------------------------------------- 8.2 무성 생존상

        [Test]
        public void Silent_ScoreIsNoiseOverDistance_LowestWins()
        {
            var tally = Runners(1, 2);
            tally.RecordNoise(1, AwardNoise.Footstep, 2.6f, .4f);   // tile walk: 2 × 1.3
            tally.RecordDistance(1, 2f);                              // 1.04 / 2 = 0.52
            tally.RecordNoise(2, AwardNoise.Footstep, 6f, .8f);
            tally.RecordDistance(2, 6f);                              // 4.8 / 6 = 0.8
            var award = AwardJudge.SilentSurvivor(tally);
            CollectionAssert.AreEqual(new[] { 1 }, award.Winners);
            Assert.AreEqual(.52f, award.Value, 1e-5f);
        }

        [Test]
        public void Silent_ValveMoanAndItemsAreLeftOut_BreathBandageTreatDrowningCount()
        {
            var tally = Runners(1);
            tally.RecordDistance(1, 10);
            tally.RecordNoise(1, AwardNoise.Valve, 12, 8);
            tally.RecordNoise(1, AwardNoise.Moan, 9, 1.2f);
            tally.RecordNoise(1, AwardNoise.Item, 9, 1.2f);
            Assert.AreEqual(0, tally.NoiseOf(1));
            tally.RecordNoise(1, AwardNoise.Breath, 3, .6f);     // 1.8
            tally.RecordNoise(1, AwardNoise.Bandage, 6, 1.2f);   // 7.2
            tally.RecordNoise(1, AwardNoise.Treat, 9, 1.2f);     // 10.8
            tally.RecordNoise(1, AwardNoise.Drowning, 22, 1.5f); // 33
            tally.RecordNoise(1, AwardNoise.Scream, 9, 1f);      // 9
            tally.RecordNoise(1, AwardNoise.Voice, 4, .6f);      // 2.4
            Assert.AreEqual(64.2, tally.NoiseOf(1), 1e-4);
        }

        [Test]
        public void Silent_TaggedRunnersAreNotEligible()
        {
            var tally = Runners(1, 2);
            tally.RecordDistance(1, 50);                 // silent but tagged
            tally.MarkTagged(1);
            tally.RecordDistance(2, 10);
            tally.RecordNoise(2, AwardNoise.Voice, 22, 2.5f);
            CollectionAssert.AreEqual(new[] { 2 }, AwardJudge.SilentSurvivor(tally).Winners);
        }

        [Test]
        public void Silent_EscapedAndUnescapedSurvivorsAreBothEligible()
        {
            var tally = Runners(1, 2);
            tally.RecordDistance(1, 10); tally.MarkEscaped(1);
            tally.RecordDistance(2, 10);
            CollectionAssert.AreEqual(new[] { 1, 2 }, AwardJudge.SilentSurvivor(tally).Winners, "both 0.0 — shared");
        }

        [Test]
        public void Silent_UnderOneMeter_IsNotRanked_ExactlyOneMeterIs()
        {
            var tally = Runners(1, 2);
            tally.RecordDistance(1, .999f);              // would score 0
            tally.RecordDistance(2, 1f);
            tally.RecordNoise(2, AwardNoise.Footstep, 2, .4f);
            var award = AwardJudge.SilentSurvivor(tally);
            CollectionAssert.AreEqual(new[] { 2 }, award.Winners);
            Assert.AreEqual(.8f, award.Value, 1e-5f);
        }

        [Test]
        public void Silent_NobodyMoved_NoWinner_NoDivisionByZero()
        {
            var tally = Runners(1, 2);
            tally.RecordNoise(1, AwardNoise.Voice, 9, 1.2f);
            var award = AwardJudge.SilentSurvivor(tally);
            Assert.IsFalse(award.HasWinner);
            Assert.IsFalse(float.IsNaN(award.Value) || float.IsInfinity(award.Value));
        }

        [Test]
        public void Silent_TieIsShared()
        {
            var tally = Runners(1, 2, 3);
            tally.RecordDistance(1, 4); tally.RecordNoise(1, AwardNoise.Footstep, 2, .4f);   // 0.2
            tally.RecordDistance(2, 8); tally.RecordNoise(2, AwardNoise.Footstep, 2, .8f);   // 0.2
            tally.RecordDistance(3, 8); tally.RecordNoise(3, AwardNoise.Footstep, 6, .8f);   // 0.6
            CollectionAssert.AreEqual(new[] { 1, 2 }, AwardJudge.SilentSurvivor(tally).Winners);
        }

        [Test]
        public void Silent_EverybodyTagged_NoWinner()
        {
            var tally = Runners(1);
            tally.RecordDistance(1, 30);
            tally.MarkTagged(1);
            Assert.IsFalse(AwardJudge.SilentSurvivor(tally).HasWinner);
        }

        // ---------------------------------------------------------------- 3.3-3 · 8 집계 고정

        [Test]
        public void Escape_FreezesSoundsAndDistance()
        {
            var tally = Runners(1);
            tally.RecordDistance(1, 10);
            tally.RecordNoise(1, AwardNoise.Footstep, 2, .4f);
            tally.MarkEscaped(1);
            // Echo body afterwards: flying, talking in the Echo channel — none of it counts.
            tally.RecordDistance(1, 500);
            tally.RecordNoise(1, AwardNoise.Voice, 22, 2.5f);
            tally.RecordNoise(1, AwardNoise.Scream, 9, 1);
            Assert.AreEqual(10, tally.DistanceOf(1), 1e-6);
            Assert.AreEqual(.8, tally.NoiseOf(1), 1e-6);
            Assert.AreEqual(0, tally.ScreamsOf(1));
        }

        [Test]
        public void Tag_FreezesSoundsAndDistance()
        {
            var tally = Runners(1);
            tally.RecordDistance(1, 3);
            tally.MarkTagged(1);
            tally.RecordDistance(1, 100);
            tally.RecordNoise(1, AwardNoise.Scream, 9, 1);
            Assert.AreEqual(3, tally.DistanceOf(1), 1e-6);
            Assert.AreEqual(0, tally.ScreamsOf(1));
        }

        [Test]
        public void FirstDecisionSticks()
        {
            var tally = Runners(1);
            tally.MarkEscaped(1);
            tally.MarkTagged(1);
            Assert.AreEqual(AwardPlayerState.Escaped, tally.StateOf(1));
        }

        [Test]
        public void LeftPlayer_IsOutOfEveryAward()
        {
            var tally = Runners(1, 2);
            tally.RecordNoise(1, AwardNoise.Scream, 9, 1);
            tally.RecordDistance(1, 10);
            tally.Remove(1);
            Assert.IsFalse(AwardJudge.MostScreams(tally).HasWinner);
            Assert.IsFalse(AwardJudge.SilentSurvivor(tally).HasWinner);
        }

        [Test]
        public void InvalidValuesAreIgnored()
        {
            var tally = Runners(1);
            tally.RecordDistance(1, float.NaN);
            tally.RecordDistance(1, -3);
            tally.RecordDistance(1, float.PositiveInfinity);
            tally.RecordNoise(1, AwardNoise.Voice, float.NaN, 1);
            tally.RecordNoise(1, AwardNoise.Voice, 9, -1);
            Assert.AreEqual(0, tally.DistanceOf(1));
            Assert.AreEqual(0, tally.NoiseOf(1));
        }

        // ---------------------------------------------------------------- 8.3 최고의 낚시상

        [Test]
        public void BestLure_EliminatedEchoesOnly()
        {
            var tally = Runners(1, 2, 3);
            tally.MarkTagged(1);
            tally.RecordLure(1);
            tally.RecordLure(2);          // alive Runner: cannot knock — ignored
            tally.MarkEscaped(3);
            tally.RecordLure(3);          // escapee: no knock (3.3-3) — ignored
            var award = AwardJudge.BestLure(tally);
            CollectionAssert.AreEqual(new[] { 1 }, award.Winners);
            Assert.AreEqual(1f, award.Value);
        }

        [Test]
        public void BestLure_CountsKeepGrowingAfterTheTag_TieShared_ZeroNoWinner()
        {
            var tally = Runners(1, 2);
            Assert.IsFalse(AwardJudge.BestLure(tally).HasWinner);
            tally.MarkTagged(1); tally.MarkTagged(2);
            Assert.IsFalse(AwardJudge.BestLure(tally).HasWinner, "0 lures = 수상자 없음");
            tally.RecordLure(1); tally.RecordLure(1); tally.RecordLure(2); tally.RecordLure(2);
            var award = AwardJudge.BestLure(tally);
            CollectionAssert.AreEqual(new[] { 1, 2 }, award.Winners);
            Assert.AreEqual(2f, award.Value);
        }

        // ---------------------------------------------------------------- 표시 · 상수

        [Test]
        public void JudgeAll_KeepsTheCardOrder_EvenWithoutWinners()
        {
            var awards = AwardJudge.JudgeAll(Runners(1));
            Assert.AreEqual(3, awards.Length);
            Assert.AreEqual(AwardKind.MostScreams, awards[0].Kind);
            Assert.AreEqual(AwardKind.SilentSurvivor, awards[1].Kind);
            Assert.AreEqual(AwardKind.BestLure, awards[2].Kind);
        }

        [Test]
        public void EvidenceLines()
        {
            Assert.AreEqual("비명 3번", AwardRules.Evidence(AwardKind.MostScreams, 3));
            Assert.AreEqual("가장 조용히 움직임", AwardRules.Evidence(AwardKind.SilentSurvivor, .5199f));
            Assert.AreEqual("술래를 2번 낚음", AwardRules.Evidence(AwardKind.BestLure, 2));
            Assert.AreEqual("무성 점수 0.52", AwardRules.EvidenceDetail(AwardKind.SilentSurvivor, .5199f));
            Assert.IsNull(AwardRules.EvidenceDetail(AwardKind.MostScreams, 3));
            Assert.IsNull(AwardRules.EvidenceDetail(AwardKind.BestLure, 2));
        }

        [TestCase(0f, 3f)]
        [TestCase(20f, 3f)]
        [TestCase(34.9f, 3f)]
        [TestCase(35f, 5f)]
        [TestCase(50f, 7f)]
        [TestCase(65f, 9f)]
        [TestCase(200f, 9f)]
        public void BreathRadius_ThreeToNineMeters(float stillSeconds, float radius)
        {
            Assert.AreEqual(radius, AwardRules.BreathRadiusAt(stillSeconds), 1e-5f);
        }
    }
}
