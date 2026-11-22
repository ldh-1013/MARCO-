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

        // 기획서 6.1 · 6.1-1: A · C · D 회전 8.0 / 재개방 4.0, 수중 B · E는 회전 구간만(진입 · 부상은 헤엄)
        [TestCase(ValveId.A, 8.0f, 4.0f)]
        [TestCase(ValveId.B, 5.0f, 2.5f)]
        [TestCase(ValveId.C, 8.0f, 4.0f)]
        [TestCase(ValveId.D, 8.0f, 4.0f)]
        [TestCase(ValveId.E, 7.0f, 3.5f)]
        public void ValveRotationSeconds_MatchDesign(ValveId id, float firstSeconds, float reopenSeconds)
        {
            var config = new WinConditionConfig();

            Assert.AreEqual(firstSeconds, config.GetValveRotationSeconds(id, reopen: false), Tolerance);
            Assert.AreEqual(reopenSeconds, config.GetValveRotationSeconds(id, reopen: true), Tolerance);
        }

        [Test]
        public void ValveWorkerMultipliers_MatchDesign()
        {
            var config = new WinConditionConfig();
            float oneWorkerSeconds = config.GetValveRotationSeconds(ValveId.A, reopen: false);

            // 2인: ×1.6 → A 기준 약 5.0초
            Assert.AreEqual(1.6f, config.ValveTwoWorkerMultiplier, Tolerance);
            Assert.AreEqual(5.0f, oneWorkerSeconds / config.ValveTwoWorkerMultiplier, 0.01f);

            // 3인 이상: ×1.9 → A 기준 약 4.2초 (상한)
            Assert.AreEqual(1.9f, config.ValveThreeOrMoreWorkerMultiplier, Tolerance);
            Assert.AreEqual(4.2f, oneWorkerSeconds / config.ValveThreeOrMoreWorkerMultiplier, 0.05f);
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
