using System.Collections.Generic;
using Marco.Network.Round;
using Marco.Network.Vision;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 기획서 3.2-1 정보량 표(379-380행) · 5.7(1218행) · 부록 A [메아리] [11-16]: Echo body rings last their own duration (×1.0),
    // the sonar ring fades to the residual start alpha, then a 3 s residual; a ring and its residual are one slot of 8.
    public class EchoSonarRulesTests
    {
        private const float T = 1e-4f;

        [TestCase(.6f)]
        [TestCase(1.2f)]
        [TestCase(2.5f)]
        [TestCase(.4f)]
        public void EchoBody_RingKeepsItsOwnDuration(float seconds)
        {
            Assert.AreEqual(seconds, PulseSpec.ListenerSeconds(seconds, false, true, true, 1f), T, "ring");
            Assert.AreEqual(seconds, PulseSpec.ListenerSeconds(seconds, true, true, true, .5f), T, "no Seeker ×1.5, no occlusion");
        }

        [Test]
        public void SeekerAndRunner_Unchanged()
        {
            Assert.AreEqual(1.8f, PulseSpec.ListenerSeconds(1.2f, true, false, true, 1f), T, "Seeker ×1.5");
            Assert.AreEqual(.9f, PulseSpec.ListenerSeconds(1.2f, true, false, false, .5f), T, "Seeker behind a wall");
            Assert.AreEqual(2.5f, PulseSpec.ListenerSeconds(2.5f, false, false, true, 1f), T, "Runner");
        }

        [Test]
        public void SonarRing_FadesToTheResidualStart()
        {
            Assert.AreEqual(EchoSonarRules.MaxAlpha, EchoSonarRules.RingAlpha(0f, true), T, "t = 0");
            Assert.AreEqual(EchoSonarRules.ResidualStartAlpha, EchoSonarRules.RingAlpha(1f, true), T, "t = 1 → R");
            Assert.AreEqual(.15f, EchoSonarRules.ResidualStartAlpha, T, "R = 잔상 15% (AfterglowAlpha)");
        }

        [Test]
        public void SurvivorRing_StillFadesToZero()
        {
            Assert.AreEqual(EchoSonarRules.MaxAlpha, EchoSonarRules.RingAlpha(0f, false), T);
            Assert.AreEqual(0f, EchoSonarRules.RingAlpha(1f, false), T, "t = 1 → 0, unchanged");
        }

        [Test]
        public void RingEnd_ContinuesIntoTheResidual() =>
            Assert.AreEqual(EchoSonarRules.RingAlpha(1f, true), EchoSonarRules.ResidualAlpha(0f), T);

        [Test]
        public void Residual_FadesOverThreeSeconds()
        {
            float r = EchoSonarRules.ResidualStartAlpha;
            Assert.AreEqual(3f, EchoSonarRules.ResidualSeconds, T);
            Assert.AreEqual(r / 2f, EchoSonarRules.ResidualAlpha(1.5f), T, "s = 1.5");
            Assert.AreEqual(0f, EchoSonarRules.ResidualAlpha(3f), T, "s = 3");
            Assert.IsFalse(EchoSonarRules.ResidualDone(2.99f));
            Assert.IsTrue(EchoSonarRules.ResidualDone(3f));
            Assert.IsTrue(EchoSonarRules.ResidualDone(3.5f), "s > 3 → removed");
        }

        [Test]
        public void Slots_AllResiduals_EvictTheOldest()
        {
            var slots = new List<bool>();
            for (int i = 0; i < 8; i++) slots.Add(true);   // 8 sonar rings, all in their residual
            Assert.AreEqual(0, EchoSonarRules.SlotToEvict(slots, 8), "a new ring evicts the oldest slot");
        }

        [Test]
        public void Slots_FinishedResidualMakesRoom()
        {
            var slots = new List<bool>();
            for (int i = 0; i < 7; i++) slots.Add(true);   // one residual ended and was removed
            Assert.AreEqual(-1, EchoSonarRules.SlotToEvict(slots, 8));
            slots.Insert(0, false);                          // floor rings do not take a sound slot
            Assert.AreEqual(-1, EchoSonarRules.SlotToEvict(slots, 8));
            slots.Add(true);
            Assert.AreEqual(1, EchoSonarRules.SlotToEvict(slots, 8), "the oldest sound slot, skipping the floor ring");
        }
    }
}
