using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    // 기획서 6.1-2 · 부록 A [밸브 역류] (10-26): 개방 유지 240초 → 역류 30초 → Closed. 재작업은 재개방 4.0초.
    public class ValveReflowTests
    {
        private const float Tolerance = 0.001f;

        private static Valve OpenValve(WinConditionConfig config)
        {
            var valve = new Valve(config);
            valve.BeginWork(1);
            valve.Tick(100f); // 1인 작업으로 8초면 충분하지만 여유있게 큰 dt로 한 번에 Open까지 진행
            valve.EndWork(1);
            return valve;
        }

        [Test]
        public void StillOpen_JustBefore240SecondGrace()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(239.9f);

            Assert.AreEqual(ValveStatus.Open, valve.Status);
            Assert.AreEqual(1f, valve.Progress, Tolerance);
        }

        [Test]
        public void StillOpen_AtExactly240Seconds_ReflowsRightAfter()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(240f); // 유예를 정확히 다 쓴 순간까지는 Open

            Assert.AreEqual(ValveStatus.Open, valve.Status);

            valve.Tick(0.1f);

            Assert.AreEqual(ValveStatus.Reflowing, valve.Status);
            Assert.AreEqual(1f - 0.1f / 30f, valve.Progress, Tolerance);
        }

        [Test]
        public void StillReflowing_JustBefore270Seconds()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(269.9f); // 유예 240 + 역류 29.9

            Assert.AreEqual(ValveStatus.Reflowing, valve.Status);
            Assert.AreEqual(0.1f / 30f, valve.Progress, Tolerance);
        }

        [Test]
        public void FullyClosed_At270SecondsAfterOpen()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(270f); // 유예 240초 + 역류 30초

            Assert.AreEqual(ValveStatus.Closed, valve.Status);
            Assert.AreEqual(0f, valve.Progress, Tolerance);
        }

        [Test]
        public void TwoValves_CloseIndependentlyBasedOnTheirOwnOpenTime()
        {
            var config = new WinConditionConfig();
            var valveA = OpenValve(config);

            valveA.Tick(100f); // A는 열린 지 100초 지남

            var valveB = OpenValve(config); // B는 지금 막 열림 (열린 지 0초)

            valveA.Tick(170f); // A: 100 + 170 = 270 → Closed
            valveB.Tick(170f); // B: 0 + 170 = 170 → 아직 Open

            Assert.AreEqual(ValveStatus.Closed, valveA.Status);
            Assert.AreEqual(ValveStatus.Open, valveB.Status);

            valveB.Tick(100f); // B: 170 + 100 = 270 → Closed

            Assert.AreEqual(ValveStatus.Closed, valveB.Status);
        }

        [Test]
        public void ReflowRippleTriggered_FiresExactlyThreeTimes()
        {
            var valve = OpenValve(new WinConditionConfig());
            int rippleCount = 0;
            valve.ReflowRippleTriggered += () => rippleCount++;

            valve.Tick(270f); // 유예 + 역류 전체를 한 번에 통과

            Assert.AreEqual(3, rippleCount);
            Assert.AreEqual(ValveStatus.Closed, valve.Status);
        }

        [Test]
        public void ValveOpening_ClearsWorkers()
        {
            var valve = new Valve(new WinConditionConfig());

            valve.BeginWork(1);
            valve.BeginWork(2);
            valve.Tick(5f); // 2인 5초 -> Open

            Assert.AreEqual(ValveStatus.Open, valve.Status);
            Assert.AreEqual(0, valve.WorkerCount);
        }

        // 기획서 6.1-2: 역류 중 재작업은 0부터 다시, 회전 시간은 재개방 4.0초(10-26)
        [Test]
        public void ReworkDuringReflow_OneWorker_Takes4Seconds()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(241f); // 유예 240 + 역류 1초 -> Reflowing
            Assert.AreEqual(ValveStatus.Reflowing, valve.Status);

            valve.BeginWork(1);
            valve.Tick(3.9f); // 0.25 * 3.9 = 0.975, 아직 미완료

            Assert.AreEqual(ValveStatus.Reflowing, valve.Status);
            Assert.AreEqual(0.975f, valve.ReworkProgress, Tolerance);

            valve.Tick(0.2f); // 누적 4.1초 -> 완료

            Assert.AreEqual(ValveStatus.Open, valve.Status);
            Assert.AreEqual(1f, valve.Progress, Tolerance);
            Assert.AreEqual(0f, valve.ReworkProgress, Tolerance);
        }

        [Test]
        public void ReworkDuringReflow_TwoWorkers_Take2_5Seconds()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(241f); // Reflowing
            valve.BeginWork(1);
            valve.BeginWork(2);
            valve.Tick(2.4f); // 0.25 * 1.6 * 2.4 = 0.96

            Assert.AreEqual(ValveStatus.Reflowing, valve.Status);

            valve.Tick(0.2f);

            Assert.AreEqual(ValveStatus.Open, valve.Status);
        }

        [Test]
        public void ReworkDuringReflow_ReflowKeepsClosing()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(245f); // 역류 5초 -> Progress = 1 - 5/30
            valve.BeginWork(1);
            valve.Tick(3f); // 재작업 중에도 역류는 계속: 1 - 8/30

            Assert.AreEqual(ValveStatus.Reflowing, valve.Status);
            Assert.AreEqual(1f - 8f / 30f, valve.Progress, Tolerance);
            Assert.AreEqual(0.75f, valve.ReworkProgress, Tolerance);
        }

        // 역류로 닫혀도 재개방 플래그는 남는다 — Closed에서 다시 돌려도 4.0초 기준(+0.25/sec).
        [Test]
        public void ReflowFinishesBeforeRework_BecomesClosed_AndKeepsReopenRate()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(268f); // 역류 28초, 닫힐 때까지 2초 남음
            valve.BeginWork(1);
            valve.Tick(2f); // 재작업은 0.5까지밖에 못 감 -> 역류가 먼저 끝남

            Assert.AreEqual(ValveStatus.Closed, valve.Status);
            Assert.AreEqual(0f, valve.Progress, Tolerance);
            Assert.AreEqual(0f, valve.ReworkProgress, Tolerance);
            Assert.IsTrue(valve.HasReachedOpen);

            valve.Tick(1f); // 계속 잡고 있으면 Closed에서 0부터 회전, 재개방 속도

            Assert.AreEqual(ValveStatus.Rotating, valve.Status);
            Assert.AreEqual(0.25f, valve.Progress, Tolerance);
        }

        [Test]
        public void ReworkDuringReflow_ReturnsToOpen_AndRestartsGraceTimerFromZero()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(245f); // Reflowing 진입
            valve.BeginWork(1);
            valve.Tick(4f); // 재작업 완료 -> Open, 유예 타이머 0부터 재시작

            Assert.AreEqual(ValveStatus.Open, valve.Status);

            valve.EndWork(1);
            valve.Tick(239.9f); // 새로 시작된 유예 240초가 아직 안 끝났으니 여전히 Open이어야 함

            Assert.AreEqual(ValveStatus.Open, valve.Status);
            Assert.AreEqual(1f, valve.Progress, Tolerance);
        }
    }
}
