using System;
using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    // CLAUDE.md의 "규칙 요약" 숫자들이 WinConditionConfig에 그대로 들어있는지 확인하는 테스트.
    public class WinConditionConfigTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void RunnerCount2_MatchesTable()
        {
            var config = new WinConditionConfig();
            var settings = config.GetRoundSettings(2);

            Assert.AreEqual(1, settings.EscapeRequiredCount);
            Assert.AreEqual(11f * 60f, settings.TimeLimitSeconds, Tolerance);
        }

        [Test]
        public void RunnerCount3_MatchesTable()
        {
            var config = new WinConditionConfig();
            var settings = config.GetRoundSettings(3);

            Assert.AreEqual(2, settings.EscapeRequiredCount);
            Assert.AreEqual(13f * 60f, settings.TimeLimitSeconds, Tolerance);
        }

        [Test]
        public void RunnerCount4_MatchesTable()
        {
            var config = new WinConditionConfig();
            var settings = config.GetRoundSettings(4);

            Assert.AreEqual(2, settings.EscapeRequiredCount);
            Assert.AreEqual(13f * 60f, settings.TimeLimitSeconds, Tolerance);
        }

        [Test]
        public void RunnerCount5_MatchesTable()
        {
            var config = new WinConditionConfig();
            var settings = config.GetRoundSettings(5);

            Assert.AreEqual(3, settings.EscapeRequiredCount);
            Assert.AreEqual(15f * 60f, settings.TimeLimitSeconds, Tolerance);
        }

        [Test]
        public void ActiveAndRequiredOpenValveCount_AreAlwaysThree()
        {
            // v0.6: 인원과 무관하게 활성 밸브/요구 개방 수는 항상 3
            var config = new WinConditionConfig();

            Assert.AreEqual(3, config.ActiveValveCount);
            Assert.AreEqual(3, config.RequiredOpenValveCount);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(6)]
        public void UndefinedRunnerCount_Throws(int runnerCount)
        {
            var config = new WinConditionConfig();

            Assert.Throws<ArgumentOutOfRangeException>(() => config.GetRoundSettings(runnerCount));
        }

        [Test]
        public void ValveProgressRate_MatchesDesign()
        {
            var config = new WinConditionConfig();

            // 1인: 초당 1/8 → 8.0초에 완료
            Assert.AreEqual(1f / 8f, config.ValveBaseProgressPerSecond, Tolerance);
            float oneWorkerSeconds = 1f / config.ValveBaseProgressPerSecond;
            Assert.AreEqual(8.0f, oneWorkerSeconds, 0.01f);

            // 2인: ×1.6 → 약 5.0초
            float twoWorkerSeconds = 1f / (config.ValveBaseProgressPerSecond * config.ValveTwoWorkerMultiplier);
            Assert.AreEqual(5.0f, twoWorkerSeconds, 0.01f);

            // 3인 이상: ×1.9 → 약 4.2초 (상한)
            float threePlusWorkerSeconds = 1f / (config.ValveBaseProgressPerSecond * config.ValveThreeOrMoreWorkerMultiplier);
            Assert.AreEqual(4.2f, threePlusWorkerSeconds, 0.05f);
        }

        [Test]
        public void ValveNoWorkerDecay_MatchesDesign()
        {
            var config = new WinConditionConfig();

            Assert.AreEqual(3f, config.ValveNoWorkerGraceSeconds, Tolerance);
            Assert.AreEqual(0.10f, config.ValveNoWorkerDecayPerSecond, Tolerance);
        }

        [Test]
        public void Reflow_MatchesDesign()
        {
            var config = new WinConditionConfig();

            Assert.AreEqual(240f, config.ReflowOpenGraceSeconds, Tolerance);
            Assert.AreEqual(30f, config.ReflowCloseDurationSeconds, Tolerance);
            Assert.AreEqual(3, config.ReflowRippleEventCount);
        }

        // 부록 A [밸브]: 재개방 1인 +1/4 per sec (4.0초, 10-26)
        [Test]
        public void ValveReopenRate_MatchesDesign()
        {
            var config = new WinConditionConfig();

            Assert.AreEqual(1f / 4f, config.ValveReopenProgressPerSecond, Tolerance);
        }
        [Test]
        public void LastSurvivorPhase_MatchesDesign()
        {
            var config = new WinConditionConfig();

            Assert.AreEqual(90f, config.LastSurvivorPhaseDurationSeconds, Tolerance);
            Assert.AreEqual(2, config.DrainHatchCandidateCount);
        }

        // v0.6: T = 11 − (동시 개방 밸브 수 × 3)초, 최소 1초
        [TestCase(0, 11f)]
        [TestCase(1, 8f)]
        [TestCase(2, 5f)]
        [TestCase(3, 2f)]
        [TestCase(4, 1f)] // 11 - 12 = -1 → 최소 1초로 clamp
        public void DrainHatchWorkSeconds_MatchesFormula(int openValveCount, float expectedSeconds)
        {
            var config = new WinConditionConfig();

            Assert.AreEqual(expectedSeconds, config.GetDrainHatchWorkSeconds(openValveCount), Tolerance);
        }

        [Test]
        public void ValveFullyReflowsBackToClosed_DefaultsToTrue()
        {
            var config = new WinConditionConfig();

            Assert.IsTrue(config.ValveFullyReflowsBackToClosed);
        }
    }
}
