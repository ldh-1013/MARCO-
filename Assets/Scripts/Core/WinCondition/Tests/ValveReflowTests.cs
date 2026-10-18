using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
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
        public void StillOpen_JustBefore180SecondGrace()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(179.9f);

            Assert.AreEqual(ValveStatus.Open, valve.Status);
            Assert.AreEqual(1f, valve.Progress, Tolerance);
        }

        [Test]
        public void FullyClosed_At210SecondsAfterOpen()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(210f); // 유예 180초 + 역류 30초

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

            valveA.Tick(110f); // A: 100 + 110 = 210 → Closed
            valveB.Tick(110f); // B: 0 + 110 = 110 → 아직 Open

            Assert.AreEqual(ValveStatus.Closed, valveA.Status);
            Assert.AreEqual(ValveStatus.Open, valveB.Status);

            valveB.Tick(100f); // B: 110 + 100 = 210 → Closed

            Assert.AreEqual(ValveStatus.Closed, valveB.Status);
        }

        [Test]
        public void ReflowRippleTriggered_FiresExactlyThreeTimes()
        {
            var valve = OpenValve(new WinConditionConfig());
            int rippleCount = 0;
            valve.ReflowRippleTriggered += () => rippleCount++;

            valve.Tick(210f); // 유예 + 역류 전체를 한 번에 통과

            Assert.AreEqual(3, rippleCount);
            Assert.AreEqual(ValveStatus.Closed, valve.Status);
        }

        // v0.6: 역류 중 재작업
        [Test]
        public void ReworkDuringReflow_CompletingRotation_ReturnsToOpen()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(185f); // 유예 180 + 역류 5초 진행 -> Reflowing, Progress = 1 - 5/30 = 0.8333
            Assert.AreEqual(ValveStatus.Reflowing, valve.Status);

            valve.BeginWork(1);
            valve.Tick(2f); // 1인 속도(0.125/초)로 0.1667만큼 더 필요 -> 1.0 넘겨서 완료

            Assert.AreEqual(ValveStatus.Open, valve.Status);
            Assert.AreEqual(1f, valve.Progress, Tolerance);
        }

        [Test]
        public void ReworkDuringReflow_NotYetComplete_StaysReflowingStatus()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(185f); // Reflowing, Progress = 0.8333
            valve.BeginWork(1);
            valve.Tick(0.5f); // 0.125 * 0.5 = 0.0625 만큼만 회복, 아직 1.0 미달

            Assert.AreEqual(ValveStatus.Reflowing, valve.Status);
            Assert.AreEqual(0.8958f, valve.Progress, Tolerance);
        }

        [Test]
        public void ReworkDuringReflow_ReturnsToOpen_AndRestartsGraceTimerFromZero()
        {
            var valve = OpenValve(new WinConditionConfig());

            valve.Tick(185f); // Reflowing 진입
            valve.BeginWork(1);
            valve.Tick(2f); // 재작업 완료 -> Open, 유예 타이머 0부터 재시작

            Assert.AreEqual(ValveStatus.Open, valve.Status);

            valve.EndWork(1);
            valve.Tick(179.9f); // 새로 시작된 유예 180초가 아직 안 끝났으니 여전히 Open이어야 함

            Assert.AreEqual(ValveStatus.Open, valve.Status);
            Assert.AreEqual(1f, valve.Progress, Tolerance);
        }
    }
}
