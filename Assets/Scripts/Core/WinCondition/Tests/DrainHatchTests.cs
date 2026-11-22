using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    public class DrainHatchTests
    {
        private const float Tolerance = 0.001f;

        [TestCase(0, 11f)]
        [TestCase(1, 8f)]
        [TestCase(2, 5f)]
        public void Activate_UsesFormulaBasedWorkSeconds(int openValveCount, float expectedWorkSeconds)
        {
            var hatch = new DrainHatch(new WinConditionConfig());

            hatch.Activate(openValveCount);
            hatch.BeginWork(1);
            hatch.Tick(expectedWorkSeconds);

            Assert.AreEqual(1f, hatch.Progress, Tolerance);
            Assert.AreEqual(DrainHatchStatus.Completed, hatch.Status);
        }

        [Test]
        public void MultipleWorkers_DoNotSpeedUpProgress()
        {
            // 배수구는 밸브와 달리 다인 배율이 없다: 1명이든 3명이든 속도가 같아야 함
            var hatchOne = new DrainHatch(new WinConditionConfig());
            var hatchThree = new DrainHatch(new WinConditionConfig());

            hatchOne.Activate(0);
            hatchThree.Activate(0);

            hatchOne.BeginWork(1);
            hatchThree.BeginWork(1);
            hatchThree.BeginWork(2);
            hatchThree.BeginWork(3);

            hatchOne.Tick(5f);
            hatchThree.Tick(5f);

            Assert.AreEqual(hatchOne.Progress, hatchThree.Progress, Tolerance);
        }

        [Test]
        public void StoppingWork_HoldsThenDecaysAfterGrace()
        {
            var hatch = new DrainHatch(new WinConditionConfig());
            hatch.Activate(0); // T = 11초

            hatch.BeginWork(1);
            hatch.Tick(5.5f); // progress 0.5

            hatch.EndWork(1);
            hatch.Tick(3f); // 유예 3초 소비, 그대로 0.5
            Assert.AreEqual(0.5f, hatch.Progress, Tolerance);

            hatch.Tick(1f); // 유예 끝난 뒤 1초 감쇠
            Assert.AreEqual(0.4f, hatch.Progress, Tolerance);
            Assert.AreEqual(DrainHatchStatus.InProgress, hatch.Status);
        }

        [Test]
        public void InactiveHatch_IgnoresWorkAndTicks()
        {
            var hatch = new DrainHatch(new WinConditionConfig());

            hatch.BeginWork(1);
            hatch.Tick(100f);

            Assert.AreEqual(0f, hatch.Progress);
            Assert.AreEqual(DrainHatchStatus.Inactive, hatch.Status);
        }
    }
}
