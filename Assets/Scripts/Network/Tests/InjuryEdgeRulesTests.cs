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
            Assert.AreEqual(.18f, InjuryEdgeRules.TargetAlpha(3), T, "1.5 회복 중 (11-17 후속 5; 0.22 → 0.30 → 0.13 → 0.18)");
            Assert.AreEqual(new Vector2(.09f, .035f), InjuryEdgeRules.Depth(3), "1.5 depth: corner 9 %, edge middle 3.5 %");
            Assert.AreEqual(new Vector2(.15f, .06f), InjuryEdgeRules.Depth(2), "1.0 depth unchanged");
            Assert.AreEqual(0f, InjuryEdgeRules.TargetAlpha(4), T, "2.0 정상");
            Assert.AreEqual(0f, InjuryEdgeRules.TargetAlpha(-1), T);
        }

        [Test]
        public void ColourCorrection_PerState_AndStrengthOrder()
        {
            Assert.AreEqual(new Vector2(.35f, .60f), InjuryEdgeRules.Correction(2), "1.0: desaturate 35 %, brightness 60 %");
            Assert.AreEqual(new Vector2(.10f, .85f), InjuryEdgeRules.Correction(3), "1.5: desaturate 10 %, brightness 85 % (11-17 후속 5, back from 70 %)");
            // Order regression guard (11-17 후속 5). Weighted strength = opacity × Rec.709 luminance of the corrected colour
            // (gamma values) × corner depth / the 1.0 corner depth (InjuryEdgeRules.Strength), normal and colour-blind palettes.
            // Replaces 후속 4 (no depth factor — 1.5 is now lighter by area) and 후속 3 ("opacity × brightness multiplier",
            // which ignored that the 술래 레드 is a far darker colour than the 앰버).
            foreach (var (redPalette, amberPalette, mode) in new[]
            {
                ((Color)new Color32(0xFF, 0x3B, 0x4D, 0xFF), (Color)new Color32(0xFF, 0xB8, 0x4D, 0xFF), "normal"),
                ((Color)new Color32(0x33, 0x57, 0xFF, 0xFF), (Color)new Color32(0xC9, 0x8A, 0x2E, 0xFF), "colour-blind"),
            })
            {
                float wounded = InjuryEdgeRules.Strength(2, redPalette), recovering = InjuryEdgeRules.Strength(3, amberPalette);
                TestContext.WriteLine($"{mode}: 1.0 {wounded:F4}, 1.5 {recovering:F4}, ratio {recovering / wounded:F2}");
                Assert.LessOrEqual(recovering, wounded * InjuryEdgeRules.RecoveringStrengthLimit, mode + ": 1.5 ≤ 1.0 × 0.70");
            }
            Assert.Less(InjuryEdgeRules.TargetAlpha(3), InjuryEdgeRules.TargetAlpha(2), "1.5 stays weaker than 1.0");
            // §16.2 앰버 #FFB84D keeps its hue after the correction (r > g > b, still orange-yellow)
            var amber = InjuryEdgeRules.StainColor(new Color32(0xFF, 0xB8, 0x4D, 0xFF), 3);
            Assert.Greater(amber.r, amber.g); Assert.Greater(amber.g, amber.b);
            Color.RGBToHSV(amber, out float hue, out float saturation, out float value);
            Color.RGBToHSV(new Color32(0xFF, 0xB8, 0x4D, 0xFF), out float paletteHue, out _, out _);
            Assert.AreEqual(paletteHue, hue, .01f, "same hue as the palette amber");
            // brightness 85 % = the multiplier after desaturating 10 % toward the palette colour's own luminance
            Color palette = new Color32(0xFF, 0xB8, 0x4D, 0xFF);
            float luminance = palette.r * .299f + palette.g * .587f + palette.b * .114f;
            Assert.AreEqual((palette.r * .9f + luminance * .1f) * .85f, amber.r, 1e-4f, "brightness 85 %");
            Assert.AreEqual(1f, amber.a);
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
        public void RecoveringMask_StaysWithinItsDepth()
        {
            var depth = InjuryEdgeRules.Depth(3);
            for (float u = 0f; u <= 1f; u += .0125f)
                for (float v = 0f; v <= 1f; v += .0125f)
                {
                    float m = InjuryEdgeRules.Mask(u, v, 16f / 9f, 5f, .2f, depth.x, depth.y);
                    if (Mathf.Min(Mathf.Min(u, 1f - u) * 16f / 9f, Mathf.Min(v, 1f - v)) > depth.x + T) Assert.AreEqual(0f, m, $"({u}, {v})");
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
