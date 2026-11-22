using System.Collections.Generic;
using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    // 기획서 6.2-1 · 부록 A [종반 압박]: 잔여 2분 드론 / 1분 술래 +5%(1회 · 유지) / 30초 출구 파문 5초 주기 6회
    public class SuddenDeathTests
    {
        private const float Tolerance = 0.0001f;
        private const int SeekerId = 9001;

        // ── SuddenDeath 단독 ───────────────────────────────

        [Test]
        public void Drone_StartsAtExactly120Seconds_NotBefore()
        {
            var suddenDeath = new SuddenDeath(new WinConditionConfig());
            int started = 0;
            suddenDeath.DroneStarted += () => started++;

            suddenDeath.Update(120.1f);
            Assert.IsFalse(suddenDeath.IsDroneStarted);

            suddenDeath.Update(120f);
            Assert.IsTrue(suddenDeath.IsDroneStarted);

            suddenDeath.Update(100f);
            Assert.AreEqual(1, started); // 한 번만 알린다
        }

        [Test]
        public void SeekerBoost_StartsAtExactly60Seconds_NotBefore()
        {
            var suddenDeath = new SuddenDeath(new WinConditionConfig());

            suddenDeath.Update(60.1f);
            Assert.IsFalse(suddenDeath.IsSeekerSpeedBoosted);
            Assert.AreEqual(1f, suddenDeath.SeekerSpeedMultiplier, Tolerance);

            suddenDeath.Update(60f);
            Assert.IsTrue(suddenDeath.IsSeekerSpeedBoosted);
            Assert.AreEqual(1.05f, suddenDeath.SeekerSpeedMultiplier, Tolerance);
        }

        // 1회 적용 후 유지: 잔여 시간이 다시 60초 위로 보여도(재동기화 등) 끄지 않는다
        [Test]
        public void SeekerBoost_AppliedOnce_NeverTurnsOff()
        {
            var suddenDeath = new SuddenDeath(new WinConditionConfig());
            int boosted = 0;
            suddenDeath.SeekerSpeedBoosted += () => boosted++;

            suddenDeath.Update(59f);
            suddenDeath.Update(61f);
            suddenDeath.Update(30f);
            suddenDeath.Update(0f);

            Assert.IsTrue(suddenDeath.IsSeekerSpeedBoosted);
            Assert.AreEqual(1.05f, suddenDeath.SeekerSpeedMultiplier, Tolerance);
            Assert.AreEqual(1, boosted);
        }

        // 곱연산: 5인 술래 5.30 m/s → 5.565 (기획서 5.57)
        [Test]
        public void SeekerBoost_IsMultiplicative()
        {
            var suddenDeath = new SuddenDeath(new WinConditionConfig());

            suddenDeath.Update(60f);

            Assert.AreEqual(5.565f, 5.30f * suddenDeath.SeekerSpeedMultiplier, 0.001f);
        }

        [Test]
        public void ExitPulse_FiresAt30_25_20_15_10_5_ExactlySixTimes()
        {
            var suddenDeath = new SuddenDeath(new WinConditionConfig());
            var firedAt = new List<float>();
            float remaining = 0f;
            suddenDeath.ExitPulseTriggered += () => firedAt.Add(remaining);

            for (int tenths = 1300; tenths >= 0; tenths--) // 130.0초 → 0.0초, 0.1초 단위
            {
                remaining = tenths / 10f;
                suddenDeath.Update(remaining);
            }

            Assert.AreEqual(6, firedAt.Count);
            CollectionAssert.AreEqual(new[] { 30f, 25f, 20f, 15f, 10f, 5f }, firedAt);
            Assert.AreEqual(6, suddenDeath.ExitPulsesFired);
        }

        [Test]
        public void ExitPulse_NotBefore30Seconds()
        {
            var suddenDeath = new SuddenDeath(new WinConditionConfig());

            suddenDeath.Update(30.1f);
            Assert.AreEqual(0, suddenDeath.ExitPulsesFired);

            suddenDeath.Update(30f);
            Assert.AreEqual(1, suddenDeath.ExitPulsesFired);

            suddenDeath.Update(25.1f);
            Assert.AreEqual(1, suddenDeath.ExitPulsesFired);

            suddenDeath.Update(25f);
            Assert.AreEqual(2, suddenDeath.ExitPulsesFired);
        }

        // 큰 dt 한 번에 끝까지 넘어가도 지난 단계는 전부, 출구 파문도 6회만
        [Test]
        public void LargeJumpToZero_FiresEverythingOnce()
        {
            var suddenDeath = new SuddenDeath(new WinConditionConfig());
            int pulses = 0;
            suddenDeath.ExitPulseTriggered += () => pulses++;

            suddenDeath.Update(0f);
            suddenDeath.Update(0f);

            Assert.IsTrue(suddenDeath.IsDroneStarted);
            Assert.IsTrue(suddenDeath.IsSeekerSpeedBoosted);
            Assert.AreEqual(6, pulses);
        }

        // ── RoundState와 함께 ───────────────────────────────

        private static (RoundState Round, float TimeLimit) CreateRound(int runnerCount)
        {
            var config = new WinConditionConfig();
            var roster = new FakePlayerRoster { TotalRunnerCount = runnerCount };
            for (int i = 1; i <= runnerCount; i++) roster.SetRole(i, PlayerRole.Runner);
            roster.SetRole(SeekerId, PlayerRole.Seeker);

            var board = new ValveBoard(new[] { ValveId.A }, config);
            var gate = new Gate(requiredOpenValveCount: 1);
            var events = new FakeRoundEventSink();
            var service = new ValveInteractionService(board, gate, roster, new FakeValveSoundSink(), events);
            var round = new RoundState(config, roster, gate, service, events);
            return (round, config.GetRoundSettings(runnerCount).TimeLimitSeconds);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Round_StagesFollowRoundRemaining_ForEveryTimeLimit(int runnerCount)
        {
            var (round, timeLimit) = CreateRound(runnerCount);

            round.Tick(timeLimit - 121f); // 잔여 121초
            Assert.IsFalse(round.SuddenDeath.IsDroneStarted);

            round.Tick(1f); // 잔여 120초
            Assert.IsTrue(round.SuddenDeath.IsDroneStarted);
            Assert.IsFalse(round.SuddenDeath.IsSeekerSpeedBoosted);

            round.Tick(60f); // 잔여 60초
            Assert.IsTrue(round.SuddenDeath.IsSeekerSpeedBoosted);
            Assert.AreEqual(0, round.SuddenDeath.ExitPulsesFired);

            round.Tick(30f); // 잔여 30초
            Assert.AreEqual(1, round.SuddenDeath.ExitPulsesFired);
            Assert.AreEqual(RoundResult.InProgress, round.Result);
        }

        [Test]
        public void Round_ExitPulsesSixTimes_ThenRoundEnds_BoostStays()
        {
            var (round, timeLimit) = CreateRound(3);
            int pulses = 0;
            round.SuddenDeath.ExitPulseTriggered += () => pulses++;

            for (int second = 0; second < (int)timeLimit; second++) round.Tick(1f);

            Assert.AreEqual(RoundResult.SeekersWin, round.Result);
            Assert.AreEqual(6, pulses);
            Assert.AreEqual(1.05f, round.SuddenDeath.SeekerSpeedMultiplier, Tolerance);
        }

        // 최후 생존자 페이즈가 잔여 90초 안에서 시작되면 페이즈 = 라운드 잔여라 종반 압박은 끝까지 그대로 진행된다
        [Test]
        public void LastSurvivorPhase_EnteredInsideFinal90_SuddenDeathStillRunsFully()
        {
            var (round, timeLimit) = CreateRound(3);
            int pulses = 0;
            round.SuddenDeath.ExitPulseTriggered += () => pulses++;

            round.Tick(timeLimit - 80f); // 잔여 80초
            Assert.IsTrue(round.SuddenDeath.IsDroneStarted);
            round.NotifyRunnerTagged(1);
            round.NotifyRunnerTagged(2);

            for (int second = 0; second < 100 && round.Result == RoundResult.InProgress; second++) round.Tick(1f);

            Assert.IsTrue(round.LastSurvivorPhase.HasEntered);
            Assert.IsTrue(round.SuddenDeath.IsSeekerSpeedBoosted);
            Assert.AreEqual(6, pulses);
        }

        // 페이즈가 일찍 시작돼 먼저 끝나면 라운드도 끝나므로 남은 단계는 발동하지 않는다(라운드 잔여 기준 — 기획서 6.2-1 그대로)
        [Test]
        public void LastSurvivorPhase_EnteredEarly_RoundEndsBeforeSuddenDeath()
        {
            var (round, timeLimit) = CreateRound(3);
            int pulses = 0;
            round.SuddenDeath.ExitPulseTriggered += () => pulses++;

            round.Tick(timeLimit - 300f); // 잔여 5분
            round.NotifyRunnerTagged(1);
            round.NotifyRunnerTagged(2);

            for (int second = 0; second < 200 && round.Result == RoundResult.InProgress; second++) round.Tick(1f);

            Assert.AreEqual(RoundResult.SeekersWin, round.Result); // 90초 페이즈 종료, 잔여 약 210초
            Assert.IsFalse(round.SuddenDeath.IsDroneStarted);
            Assert.IsFalse(round.SuddenDeath.IsSeekerSpeedBoosted);
            Assert.AreEqual(0, pulses);
        }
    }
}
