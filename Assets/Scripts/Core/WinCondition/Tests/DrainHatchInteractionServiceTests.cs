using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    public class DrainHatchInteractionServiceTests
    {
        private static (DrainHatchInteractionService Service, LastSurvivorPhase Phase, FakePlayerRoster Roster) Create()
        {
            var config = new WinConditionConfig();
            var phase = new LastSurvivorPhase(config);
            phase.Enter(gateIsOpen: false, openValveCount: 0, roundRemainingSeconds: 90f, nextIndex: _ => 0); // First 활성화

            var roster = new FakePlayerRoster();
            var service = new DrainHatchInteractionService(phase, roster);

            return (service, phase, roster);
        }

        [Test]
        public void AliveRunner_CanWorkTheActiveDrainHatch()
        {
            var (service, phase, roster) = Create();
            roster.SetRole(1, PlayerRole.Runner);

            Assert.IsTrue(service.TryBeginWork(1, DrainHatchId.First));
            Assert.AreEqual(1, phase.GetDrainHatch(DrainHatchId.First).WorkerCount);
        }

        [Test]
        public void WrongDrainHatchId_IsRejected()
        {
            var (service, phase, roster) = Create();
            roster.SetRole(1, PlayerRole.Runner);

            Assert.IsFalse(service.TryBeginWork(1, DrainHatchId.Second)); // 활성화된 건 First

            Assert.AreEqual(0, phase.GetDrainHatch(DrainHatchId.Second).WorkerCount);
        }

        [Test]
        public void SeekerAndEcho_AreRejected()
        {
            var (service, _, roster) = Create();
            roster.SetRole(10, PlayerRole.Seeker);
            roster.SetRole(20, PlayerRole.Echo);

            Assert.IsFalse(service.TryBeginWork(10, DrainHatchId.First));
            Assert.IsFalse(service.TryBeginWork(20, DrainHatchId.First));
        }

        [Test]
        public void DeadRunner_IsRejected()
        {
            var (service, _, roster) = Create();
            roster.SetRole(1, PlayerRole.Runner);
            roster.SetAlive(1, false);

            Assert.IsFalse(service.TryBeginWork(1, DrainHatchId.First));
        }

        [Test]
        public void EndWork_RemovesPlayerFromDrainHatch()
        {
            var (service, phase, roster) = Create();
            roster.SetRole(1, PlayerRole.Runner);

            service.TryBeginWork(1, DrainHatchId.First);
            service.EndWork(1, DrainHatchId.First);

            Assert.AreEqual(0, phase.GetDrainHatch(DrainHatchId.First).WorkerCount);
        }
    }
}
