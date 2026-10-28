using System.Collections.Generic;
using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    public class ValveTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void OneWorker_CompletesIn8Seconds()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.Tick(8f);

            Assert.AreEqual(1f, valve.Progress, Tolerance);
            Assert.AreEqual(ValveStatus.Open, valve.Status);
        }

        [Test]
        public void TwoWorkers_CompleteIn5Seconds()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.BeginWork(2);
            valve.Tick(5f);

            Assert.AreEqual(1f, valve.Progress, Tolerance);
            Assert.AreEqual(ValveStatus.Open, valve.Status);
        }

        [Test]
        public void SamePlayerCallingBeginWorkTwice_CountsAsOneWorker()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.BeginWork(1);

            Assert.AreEqual(1, valve.WorkerCount);
        }

        [Test]
        public void ThreeAndFourWorkers_HaveSameSpeed()
        {
            var config = new WinConditionConfig();
            var valveThree = new Valve(config);
            var valveFour = new Valve(config);

            valveThree.BeginWork(1);
            valveThree.BeginWork(2);
            valveThree.BeginWork(3);

            valveFour.BeginWork(1);
            valveFour.BeginWork(2);
            valveFour.BeginWork(3);
            valveFour.BeginWork(4);

            valveThree.Tick(2f);
            valveFour.Tick(2f);

            Assert.AreEqual(valveThree.Progress, valveFour.Progress, Tolerance);
        }

        [Test]
        public void ThreeOrMoreWorkers_CompleteInAbout4_2Seconds()
        {
            // 정확한 완료 시점은 1 / (0.125 * 1.9) = 4.21052...초.
            // 4.2초: 아직 완료 전(Rotating). 4.22초: 완료 시점을 넘겨서 Open.
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.BeginWork(2);
            valve.BeginWork(3);
            valve.Tick(4.2f);

            Assert.AreEqual(ValveStatus.Rotating, valve.Status);
            Assert.Less(valve.Progress, 1f);

            valve.Tick(0.02f); // 누적 4.22초

            Assert.AreEqual(1f, valve.Progress, Tolerance);
            Assert.AreEqual(ValveStatus.Open, valve.Status);
        }

        [Test]
        public void StoppingWork_HoldsProgressDuringGraceWindow()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.Tick(4f); // progress 0.5

            valve.EndWork(1);
            valve.Tick(3f); // 유예 3초 전부 소비, 감쇠 없음

            Assert.AreEqual(0.5f, valve.Progress, Tolerance);
            Assert.AreEqual(ValveStatus.Rotating, valve.Status);
        }

        [Test]
        public void AfterGraceWindow_ProgressDecays()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.Tick(4f); // progress 0.5

            valve.EndWork(1);
            valve.Tick(3f); // 유예 소비, 여전히 0.5
            valve.Tick(1f); // 유예 끝난 뒤 1초 감쇠 → 0.5 - 0.10

            Assert.AreEqual(0.4f, valve.Progress, Tolerance);
            Assert.AreEqual(ValveStatus.Rotating, valve.Status);
        }

        [Test]
        public void DecayAllTheWayToZero_BecomesClosed()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.Tick(1f); // progress 0.125

            valve.EndWork(1);
            valve.Tick(3f); // 유예 소비
            valve.Tick(2f); // 0.125 - 0.10*2 = -0.075 → 0으로 clamp

            Assert.AreEqual(0f, valve.Progress, Tolerance);
            Assert.AreEqual(ValveStatus.Closed, valve.Status);
        }

        [Test]
        public void ReworkingDuringDecay_ContinuesFromCurrentProgress()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.Tick(4f); // progress 0.5

            valve.EndWork(1);
            valve.Tick(3f); // 유예 소비, 0.5 유지
            valve.Tick(1f); // 감쇠 1초 → 0.4

            valve.BeginWork(1); // 다시 잡음
            valve.Tick(1f); // 0.4 + 0.125 = 0.525

            Assert.AreEqual(0.525f, valve.Progress, Tolerance);
            Assert.AreEqual(ValveStatus.Rotating, valve.Status);
        }

        [Test]
        public void LargeDtCrossingGraceAndDecayBoundary_IsStillCorrect()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.Tick(4f); // progress 0.5

            valve.EndWork(1);
            // 한 번의 큰 dt(4초) 안에 유예 3초 + 감쇠 1초가 모두 들어있는 경우
            valve.Tick(4f);

            Assert.AreEqual(0.4f, valve.Progress, Tolerance);
            Assert.AreEqual(ValveStatus.Rotating, valve.Status);
        }

        [Test]
        public void LargeDt_CompletesExactlyAtOne()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.Tick(100f); // 8초면 끝나는데 100초를 통째로 넘겨도 1.0에서 멈춰야 함

            Assert.AreEqual(1f, valve.Progress, Tolerance);
            Assert.AreEqual(ValveStatus.Open, valve.Status);
        }

        [Test]
        public void StatusChanged_FiresOnClosedToRotatingToOpen()
        {
            var valve = new Valve(new WinConditionConfig());
            var observed = new List<ValveStatus>();
            valve.StatusChanged += status => observed.Add(status);

            valve.BeginWork(1);
            valve.Tick(1f); // Closed -> Rotating
            valve.Tick(7f); // Rotating -> Open (총 8초)

            CollectionAssert.AreEqual(new[] { ValveStatus.Rotating, ValveStatus.Open }, observed);
        }

        // ── 재개방 4.0초 (기획서 6.1 · 부록 A [밸브], 10-26) ───────────────────────────────

        // 한 번 열었다가 역류로 완전히 닫힌 밸브 (유예 240 + 역류 30)
        private static Valve ReopenableClosedValve(WinConditionConfig config)
        {
            var valve = new Valve(config);
            valve.BeginWork(1);
            valve.Tick(8f);
            valve.EndWork(1);
            valve.Tick(270f);
            Assert.AreEqual(ValveStatus.Closed, valve.Status);
            return valve;
        }

        [Test]
        public void NewValve_HasNotReachedOpen()
        {
            var valve = new Valve(new WinConditionConfig());

            Assert.IsFalse(valve.HasReachedOpen);
        }

        [Test]
        public void ReachingOpen_SetsReopenFlag()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.Tick(7f);
            Assert.IsFalse(valve.HasReachedOpen);

            valve.Tick(1f);
            Assert.IsTrue(valve.HasReachedOpen);
        }

        // 역류로 Closed가 되어도 플래그는 유지된다
        [Test]
        public void ReopenFlag_SurvivesFullReflowClose()
        {
            var valve = ReopenableClosedValve(new WinConditionConfig());

            Assert.IsTrue(valve.HasReachedOpen);
        }

        [TestCase(1, 3.9f, 4.05f)]  // 1인 1 / 0.25 = 4.0초
        [TestCase(2, 2.4f, 2.55f)]  // 2인 1 / (0.25 × 1.6) = 2.5초
        [TestCase(3, 2.1f, 2.15f)]  // 3인 1 / (0.25 × 1.9) = 2.105...초
        public void Reopen_AfterReflowClose_UsesReopenTime(int workers, float justBefore, float justAfter)
        {
            var valve = ReopenableClosedValve(new WinConditionConfig());

            for (int id = 1; id <= workers; id++) valve.BeginWork(id);
            valve.Tick(justBefore);

            Assert.AreEqual(ValveStatus.Rotating, valve.Status);

            valve.Tick(justAfter - justBefore);

            Assert.AreEqual(ValveStatus.Open, valve.Status);
            Assert.AreEqual(1f, valve.Progress, Tolerance);
        }

        // 감쇠율은 재개방에서도 그대로다: 유예 3초 후 -0.10/sec
        [Test]
        public void Reopen_DecayIsUnchanged()
        {
            var valve = ReopenableClosedValve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.Tick(2f); // 0.25 * 2 = 0.5
            valve.EndWork(1);
            valve.Tick(3f); // 유예
            valve.Tick(1f); // 0.5 - 0.10

            Assert.AreEqual(0.4f, valve.Progress, Tolerance);
        }

        // 새 라운드는 ValveBoard(Valve)를 새로 만든다 → 플래그가 리셋되어 다시 8.0초
        [Test]
        public void NewRoundBoard_ResetsReopenFlag_BackTo8Seconds()
        {
            var config = new WinConditionConfig();
            var previousRound = new ValveBoard(new[] { ValveId.A }, config);
            previousRound.GetValve(ValveId.A).BeginWork(1);
            previousRound.Tick(8f);
            Assert.IsTrue(previousRound.GetValve(ValveId.A).HasReachedOpen);

            var nextRound = new ValveBoard(new[] { ValveId.A }, config);
            var valve = nextRound.GetValve(ValveId.A);

            Assert.IsFalse(valve.HasReachedOpen);
            valve.BeginWork(1);
            nextRound.Tick(7f);
            Assert.AreEqual(ValveStatus.Rotating, valve.Status);
            nextRound.Tick(1f);
            Assert.AreEqual(ValveStatus.Open, valve.Status);
        }
    }
}
