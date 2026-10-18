using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    public class LastSurvivorPhaseTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        public void Enter_OnlyAppliesOnce()
        {
            var phase = new LastSurvivorPhase(new WinConditionConfig());

            phase.Enter(gateIsOpen: false, openValveCount: 0, roundRemainingSeconds: 90f, nextIndex: _ => 0);
            var firstChoice = phase.ActiveDrainHatchId;

            // 두 번째 진입 시도는 무시되어야 함 (다른 값을 넘겨도 바뀌지 않음)
            phase.Enter(gateIsOpen: true, openValveCount: 5, roundRemainingSeconds: 10f, nextIndex: _ => 1);

            Assert.AreEqual(firstChoice, phase.ActiveDrainHatchId);
            Assert.AreEqual(90f, phase.RemainingSeconds, Tolerance);
        }

        [Test]
        public void GateAlreadyOpen_DoesNotActivateAnyDrainHatch()
        {
            var phase = new LastSurvivorPhase(new WinConditionConfig());

            phase.Enter(gateIsOpen: true, openValveCount: 0, roundRemainingSeconds: 90f, nextIndex: _ => 0);

            Assert.IsNull(phase.ActiveDrainHatchId);
            Assert.AreEqual(DrainHatchStatus.Inactive, phase.GetDrainHatch(DrainHatchId.First).Status);
            Assert.AreEqual(DrainHatchStatus.Inactive, phase.GetDrainHatch(DrainHatchId.Second).Status);
        }

        [TestCase(0, DrainHatchId.First)]
        [TestCase(1, DrainHatchId.Second)]
        public void GateClosed_ActivatesOneRandomDrainHatch(int injectedIndex, DrainHatchId expectedId)
        {
            var phase = new LastSurvivorPhase(new WinConditionConfig());

            phase.Enter(gateIsOpen: false, openValveCount: 0, roundRemainingSeconds: 90f, nextIndex: _ => injectedIndex);

            Assert.AreEqual(expectedId, phase.ActiveDrainHatchId);
            Assert.AreEqual(DrainHatchStatus.InProgress, phase.GetDrainHatch(expectedId).Status);

            var otherId = expectedId == DrainHatchId.First ? DrainHatchId.Second : DrainHatchId.First;
            Assert.AreEqual(DrainHatchStatus.Inactive, phase.GetDrainHatch(otherId).Status);
        }

        [Test]
        public void RemainingSeconds_CapsAt90()
        {
            var phase = new LastSurvivorPhase(new WinConditionConfig());

            phase.Enter(gateIsOpen: true, openValveCount: 0, roundRemainingSeconds: 1000f, nextIndex: _ => 0);

            Assert.AreEqual(90f, phase.RemainingSeconds, Tolerance);
        }

        [Test]
        public void RemainingSeconds_CapsAtRoundRemaining_WhenShorterThan90()
        {
            var phase = new LastSurvivorPhase(new WinConditionConfig());

            phase.Enter(gateIsOpen: true, openValveCount: 0, roundRemainingSeconds: 60f, nextIndex: _ => 0);

            Assert.AreEqual(60f, phase.RemainingSeconds, Tolerance);
        }

        [Test]
        public void Tick_BecomesInactive_WhenTimeRunsOut()
        {
            var phase = new LastSurvivorPhase(new WinConditionConfig());
            phase.Enter(gateIsOpen: true, openValveCount: 0, roundRemainingSeconds: 10f, nextIndex: _ => 0);

            phase.Tick(10f);

            Assert.IsFalse(phase.IsActive);
        }
    }
}
