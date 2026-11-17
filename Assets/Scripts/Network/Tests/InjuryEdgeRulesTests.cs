using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // V28 A-0 체력 가장자리 표시 (연출.md 16).
    public class InjuryEdgeRulesTests
    {
        private const float T = 1e-4f;

        [Test]
        public void StateOpacity()
        {
            Assert.AreEqual(.40f, InjuryEdgeRules.TargetAlpha(2), T, "1.0 상처");
            Assert.AreEqual(.22f, InjuryEdgeRules.TargetAlpha(3), T, "1.5 회복 중");
            Assert.AreEqual(0f, InjuryEdgeRules.TargetAlpha(4), T, "2.0 정상");
            Assert.AreEqual(0f, InjuryEdgeRules.TargetAlpha(-1), T);
        }

        [TestCase(16f / 9f)]
        [TestCase(21f / 9f)]
        [TestCase(4f / 3f)]
        public void CentreSeventyPercent_Untouched_AndDepthWithinTheLimit(float aspect)
        {
            foreach (float seed in new[] { 0f, 7.3f, 41.9f })
                for (float u = 0f; u <= 1f; u += .0125f)
                    for (float v = 0f; v <= 1f; v += .0125f)
                    {
                        float m = InjuryEdgeRules.Mask(u, v, aspect, seed, .3f);
                        Assert.That(m, Is.InRange(0f, 1f));
                        if (Mathf.Abs(u - .5f) < .35f && Mathf.Abs(v - .5f) < .35f) Assert.AreEqual(0f, m, $"centre ({u}, {v})");
                        // never deeper than 15 % of the short side from the nearest edge
                        float dx = Mathf.Min(u, 1f - u) * aspect, dy = Mathf.Min(v, 1f - v);
                        if (Mathf.Min(dx, dy) > InjuryEdgeRules.CornerDepth + T) Assert.AreEqual(0f, m, $"too deep ({u}, {v})");
                    }
        }

        [Test]
        public void Corners_AreDeeperThanEdgeMiddles()
        {
            // 4 % of the short side in from the edge: present near a corner, mostly gone at the middle of a side
            float corner = InjuryEdgeRules.Mask(.04f * 9f / 16f, .04f, 16f / 9f, 3f, 0f);
            float middle = InjuryEdgeRules.Mask(.5f, .055f, 16f / 9f, 3f, 0f);
            Assert.Greater(corner, middle);
            Assert.AreEqual(0f, InjuryEdgeRules.Mask(.5f, .0601f, 16f / 9f, 3f, 0f), T, "6 % at the middle of a side");
        }

        [Test]
        public void Transition_EaseInOut_SameAtAnyFrameRate()
        {
            Assert.AreEqual(0f, InjuryEdgeRules.Transition(0f, .4f, 0f), T);
            Assert.AreEqual(.2f, InjuryEdgeRules.Transition(0f, .4f, .15f), T, "half way at 0.15 s");
            Assert.AreEqual(.4f, InjuryEdgeRules.Transition(0f, .4f, .3f), T);
            Assert.AreEqual(.4f, InjuryEdgeRules.Transition(0f, .4f, 5f), T);
            // time based: stepping at 30 / 60 / 144 fps lands on the same value at the same time
            foreach (int fps in new[] { 30, 60, 144 })
            {
                float t = 0f;
                int frames = Mathf.RoundToInt(.2f * fps);
                for (int i = 0; i < frames; i++) t += 1f / fps;   // ≈ 0.2 s (whole frames)
                Assert.AreEqual(.2f, t, .5f / fps);
                Assert.AreEqual(InjuryEdgeRules.Transition(.4f, .22f, .2f), InjuryEdgeRules.Transition(.4f, .22f, t), .01f, fps + " fps");
            }
        }

        // V28a capture fix: the first-hit swell must ride on the new state value, not on a value still easing up from 0.
        [Test]
        public void Displayed_HitGoesStraightToTheStateAndSwells_RecoveryEases()
        {
            float target = InjuryEdgeRules.WoundedAlpha;
            Assert.AreEqual(target * (1f + InjuryEdgeRules.SwellGain), InjuryEdgeRules.Displayed(0f, target, .04f, true, .04f), T, "peak at 40 ms");
            Assert.Greater(InjuryEdgeRules.Displayed(0f, target, .05f, true, .05f), target, "brighter than the state at 0.05 s");
            Assert.AreEqual(target, InjuryEdgeRules.Displayed(0f, target, .2f, true, .2f), T, "settled at 0.2 s");
            Assert.AreEqual(target, InjuryEdgeRules.Displayed(0f, target, .35f, true, .35f), T);
            // recovery 1.0 → 1.5: eases over 0.3 s, no swell
            Assert.AreEqual((target + InjuryEdgeRules.RecoveringAlpha) / 2f, InjuryEdgeRules.Displayed(target, InjuryEdgeRules.RecoveringAlpha, .15f, false, -1f), T);
            Assert.AreEqual(InjuryEdgeRules.RecoveringAlpha, InjuryEdgeRules.Displayed(target, InjuryEdgeRules.RecoveringAlpha, .3f, false, -1f), T);
        }

        [Test]
        public void Swell_FastIn_SlowOut_OverByPointTwoSeconds()
        {
            Assert.AreEqual(0f, InjuryEdgeRules.Swell(-.01f), T);
            Assert.AreEqual(InjuryEdgeRules.SwellGain, InjuryEdgeRules.Swell(InjuryEdgeRules.SwellInSeconds), T, "peak at 40 ms");
            Assert.Less(InjuryEdgeRules.Swell(.1f), InjuryEdgeRules.SwellGain * .5f, "ease-out");
            Assert.AreEqual(0f, InjuryEdgeRules.Swell(.2f), T);
            Assert.LessOrEqual(InjuryEdgeRules.SwellSeconds, .5f);
            Assert.LessOrEqual(InjuryEdgeRules.WoundedAlpha * (1f + InjuryEdgeRules.SwellGain), 1f);
            Assert.LessOrEqual(InjuryEdgeRules.DriftPerSecond, .02f);
        }
    }
}
