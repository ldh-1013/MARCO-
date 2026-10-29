using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    // 라운드 시작 순서(부록 A [라운드 시작], 10-26): 0~5초 역할 확인 → 5초 InGame(도망자 출발) → 10초 술래 해제.
    public class RoundStartScheduleTests
    {
        private const float Tolerance = 0.0001f;

        private static RoundStartSchedule NewSchedule() => new RoundStartSchedule(new WinConditionConfig());

        [Test]
        public void Config_DefaultsMatchDesign()
        {
            var config = new WinConditionConfig();

            Assert.AreEqual(5f, config.RoleRevealSeconds, Tolerance);
            Assert.AreEqual(5f, config.SeekerHoldSeconds, Tolerance);
        }

        [Test]
        public void StartsInRoleReveal_AtZero()
        {
            var schedule = NewSchedule();

            Assert.AreEqual(0f, schedule.ElapsedSeconds, Tolerance);
            Assert.AreEqual(RoundStartPhase.RoleReveal, schedule.Phase);
            Assert.IsFalse(schedule.HasInGameStarted);
            Assert.AreEqual(5f, schedule.PhaseRemainingSeconds, Tolerance);
        }

        [TestCase(4.99f, RoundStartPhase.RoleReveal)]
        [TestCase(5.0f, RoundStartPhase.SeekerHold)]
        [TestCase(9.99f, RoundStartPhase.SeekerHold)]
        [TestCase(10.0f, RoundStartPhase.Released)]
        public void Phase_AtBoundary(float elapsed, RoundStartPhase expected)
        {
            var schedule = NewSchedule();

            schedule.Tick(elapsed);

            Assert.AreEqual(expected, schedule.Phase);
        }

        [Test]
        public void At4_99_NoInGameTimeIsGiven()
        {
            var schedule = NewSchedule();

            float inGame = schedule.Tick(4.99f);

            Assert.AreEqual(0f, inGame, Tolerance);
            Assert.IsFalse(schedule.HasInGameStarted);
            Assert.IsFalse(schedule.IsSeekerReleased);
        }

        [Test]
        public void At5_0_InGameStarts_SeekerStillHeld()
        {
            var schedule = NewSchedule();

            schedule.Tick(5f);

            Assert.IsTrue(schedule.HasInGameStarted);
            Assert.IsFalse(schedule.IsSeekerReleased);
            Assert.AreEqual(5f, schedule.PhaseRemainingSeconds, Tolerance);
        }

        [Test]
        public void At9_99_SeekerStillHeld()
        {
            var schedule = NewSchedule();

            schedule.Tick(9.99f);

            Assert.IsFalse(schedule.IsSeekerReleased);
            Assert.AreEqual(0.01f, schedule.PhaseRemainingSeconds, Tolerance);
        }

        [Test]
        public void At10_0_SeekerReleased()
        {
            var schedule = NewSchedule();

            schedule.Tick(10f);

            Assert.IsTrue(schedule.IsSeekerReleased);
            Assert.AreEqual(0f, schedule.PhaseRemainingSeconds, Tolerance);
        }

        [Test]
        public void Tick_CrossingInGameStart_GivesOnlyTheOverflow()
        {
            var schedule = NewSchedule();
            schedule.Tick(4.99f);

            float inGame = schedule.Tick(0.02f);

            Assert.AreEqual(0.01f, inGame, Tolerance);
        }

        [Test]
        public void Tick_AfterInGameStart_GivesWholeDt()
        {
            var schedule = NewSchedule();
            schedule.Tick(5f);

            Assert.AreEqual(0.02f, schedule.Tick(0.02f), Tolerance);
            schedule.Tick(10f);
            Assert.AreEqual(0.02f, schedule.Tick(0.02f), Tolerance);
        }

        [Test]
        public void Tick_NonPositiveDt_DoesNothing()
        {
            var schedule = NewSchedule();

            Assert.AreEqual(0f, schedule.Tick(0f), Tolerance);
            Assert.AreEqual(0f, schedule.Tick(-1f), Tolerance);
            Assert.AreEqual(0f, schedule.ElapsedSeconds, Tolerance);
        }

        [Test]
        public void Reset_RestartsFromZero()
        {
            // NetworkRound.RestartRound → ResetRound가 Reset을 부른다: 새 판은 역할 확인 0초부터.
            var schedule = NewSchedule();
            schedule.Tick(30f);
            Assert.AreEqual(RoundStartPhase.Released, schedule.Phase);

            schedule.Reset();

            Assert.AreEqual(0f, schedule.ElapsedSeconds, Tolerance);
            Assert.AreEqual(RoundStartPhase.RoleReveal, schedule.Phase);
            Assert.AreEqual(0f, schedule.Tick(4.99f), Tolerance);
            Assert.AreEqual(RoundStartPhase.RoleReveal, schedule.Phase);
        }

        [Test]
        public void UsesConfigValues()
        {
            var config = new WinConditionConfig { RoleRevealSeconds = 2f, SeekerHoldSeconds = 3f };
            var schedule = new RoundStartSchedule(config);

            schedule.Tick(2f);
            Assert.AreEqual(RoundStartPhase.SeekerHold, schedule.Phase);
            schedule.Tick(3f);
            Assert.AreEqual(RoundStartPhase.Released, schedule.Phase);
        }

        [TestCase(RoundStartPhase.RoleReveal, PlayerRole.Runner, true)]
        [TestCase(RoundStartPhase.RoleReveal, PlayerRole.Seeker, true)]
        [TestCase(RoundStartPhase.SeekerHold, PlayerRole.Runner, false)]
        [TestCase(RoundStartPhase.SeekerHold, PlayerRole.Seeker, true)]
        [TestCase(RoundStartPhase.Released, PlayerRole.Runner, false)]
        [TestCase(RoundStartPhase.Released, PlayerRole.Seeker, false)]
        public void IsMovementLocked_ByPhaseAndRole(RoundStartPhase phase, PlayerRole role, bool expected)
        {
            Assert.AreEqual(expected, RoundStartSchedule.IsMovementLocked(phase, role));
        }
    }
}
