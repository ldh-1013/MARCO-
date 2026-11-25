using System.Collections.Generic;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // VB B-4 연출.md 8.1 · 8.2 — suppression pulse, suffocation edge (merged with the health edge), breathing sway.
    public class BreathFeelRulesTests
    {
        private const float T = 1e-4f;

        [Test]
        public void Suppression_DetectedFromTheSpentBreath_OnlyOutOfTheWater()
        {
            Assert.IsTrue(BreathFeelRules.Suppressed(14f, 9.5f, false), "4.5 s spent at once");
            Assert.IsTrue(BreathFeelRules.Suppressed(10f, 5.9f, false), "with 0.4 of recovery inside the same sync");
            Assert.IsFalse(BreathFeelRules.Suppressed(14f, 9.5f, true), "diving: suppressed for free, nothing spent");
            Assert.IsFalse(BreathFeelRules.Suppressed(8f, 7.9f, false), "ordinary changes");
            Assert.IsFalse(BreathFeelRules.Suppressed(4f, 14f, false), "a refill (new round) is not a suppression");
        }

        [Test]
        public void Suppression_Pulse_InHoldOut_WithinHalfASecond()
        {
            Assert.AreEqual(0f, BreathFeelRules.SuppressAlpha(0f), T);
            Assert.AreEqual(.10f, BreathFeelRules.SuppressAlpha(BreathFeelRules.SuppressInSeconds), T, "peak 0.10");
            Assert.AreEqual(0f, BreathFeelRules.SuppressAlpha(BreathFeelRules.SuppressSeconds), T);
            Assert.LessOrEqual(BreathFeelRules.SuppressSeconds, .5f + T, "a 0.5 s pulse");
            float last = 0f; int peaks = 0; bool rising = true;
            for (float t = 0f; t <= .6f; t += .005f)
            {
                float a = BreathFeelRules.SuppressAlpha(t);
                Assert.LessOrEqual(a, .10f + T);
                if (rising && a < last - T) { peaks++; rising = false; }
                if (!rising) Assert.LessOrEqual(a, last + T, "one rise, one fall");
                last = a;
            }
            Assert.AreEqual(1, peaks);
            // fast in, slow out: half of the peak sooner on the way in than on the way out
            Assert.Greater(BreathFeelRules.SuppressAlpha(BreathFeelRules.SuppressInSeconds * .3f), .05f);
        }

        [Test]
        public void Suffocation_Detection_And_Curve()
        {
            Assert.IsTrue(BreathFeelRules.Suffocated(0f, 2.95f), "the forced-surfacing slow starts (~3 s)");
            Assert.IsFalse(BreathFeelRules.Suffocated(2.95f, 2.85f), "the same slow, counting down");
            Assert.IsFalse(BreathFeelRules.Suffocated(0f, 1f), "not a fresh start");
            Assert.AreEqual(0f, BreathFeelRules.SuffocationWeight(0f), T);
            Assert.AreEqual(1f, BreathFeelRules.SuffocationWeight(BreathFeelRules.SuffocationCloseSeconds), T, "closed at 0.25 s");
            Assert.AreEqual(0f, BreathFeelRules.SuffocationWeight(BreathFeelRules.SuffocationSeconds), T);
            Assert.LessOrEqual(BreathFeelRules.SuffocationSeconds, .5f + T, "0.5 s in total");
            // healthy: closes from 9 % / 3.5 % to 22 % / 9 % at 0.55, then fades away
            Assert.AreEqual(.55f, BreathFeelRules.MixAlpha(0f, .25f), T);
            var d0 = BreathFeelRules.MixDepth(Vector2.zero, false, 0f); var dp = BreathFeelRules.MixDepth(Vector2.zero, false, .25f);
            Assert.AreEqual(.09f, d0.x, T); Assert.AreEqual(.035f, d0.y, T);
            Assert.AreEqual(.22f, dp.x, T); Assert.AreEqual(.09f, dp.y, T);
            Assert.AreEqual(0f, BreathFeelRules.MixAlpha(0f, .5f - 1e-5f), 1e-3f);
        }

        // Hand-over to the health edge (1.0: 0.40 · 15 % / 6 %) without a step, and — a hit at the same moment — one peak only:
        // the overlay folds the health swell into the suffocation while it runs.
        [Test]
        public void Suffocation_HandsOverToTheHealthEdge_OnePeak_NoStep()
        {
            Vector2 wounded = InjuryEdgeRules.Depth(2); float woundedAlpha = InjuryEdgeRules.TargetAlpha(2);
            float last = -1f; Vector2 lastDepth = new(-1, -1); int peaks = 0; bool rising = true; float maxStep = 0f, maxDepthStep = 0f;
            for (float t = 0f; t <= .9f; t += 1f / 144f)
            {
                bool suffocating = t < BreathFeelRules.SuffocationSeconds;
                // the hit lands with the suffocation: health jumps to the 1.0 value (hitChange), its swell suppressed meanwhile
                float health = InjuryEdgeRules.Displayed(0f, woundedAlpha, t, true, suffocating ? -1f : t);
                float a = suffocating ? BreathFeelRules.MixAlpha(health, t) : health;
                Vector2 d = suffocating ? BreathFeelRules.MixDepth(wounded, true, t) : wounded;
                if (last >= 0f) { maxStep = Mathf.Max(maxStep, Mathf.Abs(a - last)); maxDepthStep = Mathf.Max(maxDepthStep, (d - lastDepth).magnitude); }
                if (rising && last >= 0f && a < last - T) { peaks++; rising = false; }
                if (!rising && a > last + T) { peaks++; rising = true; }
                last = a; lastDepth = d;
            }
            Assert.AreEqual(1, peaks, "one peak (no second flash from the health swell)");
            Assert.AreEqual(woundedAlpha, last, 1e-3f, "ends on the health edge's own value");
            Assert.AreEqual(wounded.x, lastDepth.x, T); Assert.AreEqual(wounded.y, lastDepth.y, T);
            Assert.Less(maxStep, .06f, "no step at 144 fps");
            Assert.Less(maxDepthStep, .02f, "the depth moves without a jump");
        }

        [Test]
        public void Sway_Bounds_Band_And_Fade()
        {
            int crossings = 0; float last = 0f; float maxRot = 0f, maxPos = 0f;
            for (float t = 0f; t < 60f; t += 1f / 60f)
            {
                var r = BreathFeelRules.SwayRotation(t, 1f); var p = BreathFeelRules.SwayPosition(t, 1f);
                maxRot = Mathf.Max(maxRot, r.magnitude); maxPos = Mathf.Max(maxPos, p.magnitude);
                if (t > 0f && Mathf.Sign(r.x) != Mathf.Sign(last) && Mathf.Abs(r.x) > 1e-5f) crossings++;
                if (Mathf.Abs(r.x) > 1e-5f) last = r.x;
            }
            Assert.LessOrEqual(maxRot, .15f * 1.2f + T, "≤ 0.15° per axis (pitch + 0.6 × yaw)");
            Assert.LessOrEqual(maxPos, .003f + T, "≤ 3 mm");
            Assert.LessOrEqual(maxRot, FeelRules.MaxRotationDegrees, "inside the stack cap on its own");
            // 0.25 – 0.4 Hz noise: over 60 s, between 10 and 60 sign changes (a 0.33 Hz lattice, no fast jitter)
            Assert.That(crossings, Is.InRange(10, 60), $"sign changes {crossings}");
            foreach (float fps in new[] { 30f, 60f, 144f })
            {
                float w = 0f, t = 0f;
                while (t < BreathFeelRules.SwayFadeSeconds - 1e-4f) { w = BreathFeelRules.SwayFade(w, true, 1f / fps); t += 1f / fps; }
                Assert.AreEqual(1f, w, 1e-3f, $"in over 0.3 s at {fps} fps");
                t = 0f; while (t < BreathFeelRules.SwayFadeSeconds - 1e-4f) { w = BreathFeelRules.SwayFade(w, false, 1f / fps); t += 1f / fps; }
                Assert.AreEqual(0f, w, 1e-3f, $"out over 0.3 s at {fps} fps");
            }
            Assert.AreEqual(Vector3.zero, BreathFeelRules.SwayRotation(12.3f, 0f));
        }
    }
}
