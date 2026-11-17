using Marco.Network.Escape;
using Marco.Network.Player;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // VB B-3 연출.md 5 · 7.1 · 7.2 — escape fade (unchanged, pinned), the Echo's return from the tag blackout, the knock tremor.
    public class EchoFeelRulesTests
    {
        private const float T = 1e-4f;

        [Test]
        public void Escape_AsSpecified_SmoothAndWithin1_5s()
        {
            Assert.AreEqual(1f, EscapeFade.FadeOutSeconds, T, "1.0 s cyan (spec)");
            Assert.LessOrEqual(EscapeFade.TotalSeconds, 1.5f + T, "fade + reveal ≤ 1.5 s");
            Assert.AreEqual(1f, EscapeFade.Alpha(EscapeFade.FadeOutSeconds - 1e-4f), 1e-3f, "fully cyan at 1.0 s");
            Assert.AreEqual(1f, EscapeFade.Alpha(EscapeFade.FadeOutSeconds), 1e-3f, "no blackout, no step at the body switch");
            float last = 0f, maxStep = 0f;
            for (float t = 0f; t <= EscapeFade.TotalSeconds + .05f; t += 1f / 144f)
            {
                float a = EscapeFade.Alpha(t); maxStep = Mathf.Max(maxStep, Mathf.Abs(a - last)); last = a;
            }
            Assert.Less(maxStep, .03f, "ease-in-out, no jump at 144 fps");
            Assert.AreEqual(0f, last, T);
            var c = (Color32)EscapeFade.Cyan; Assert.AreEqual(new Color32(0x7F, 0xE7, 0xE0, 0xFF), c);
        }

        [Test]
        public void EchoReveal_EaseOut_SameStartAndEnd_NoStep()
        {
            Assert.AreEqual(1f, EchoFeelRules.RevealCover(0f), T);
            Assert.AreEqual(0f, EchoFeelRules.RevealCover(EchoFeelRules.RevealSeconds), T);
            Assert.AreEqual(EchoBodyRules.TagRevealSeconds, EchoFeelRules.RevealSeconds, T, "the same 0.5 s the blackout rules use");
            // ease-out: more than half open in the first third (the linear clearing was a third)
            Assert.Less(EchoFeelRules.RevealCover(EchoFeelRules.RevealSeconds / 3f), .5f);
            float last = 1f;
            for (float t = 0f; t <= .55f; t += 1f / 144f)
            {
                float c = EchoFeelRules.RevealCover(t);
                Assert.LessOrEqual(c, last + T, "only opens"); last = c;
            }
            // continuous with the full black before it
            Assert.AreEqual(1f, EchoBodyRules.TagBlackoutCover(EchoBodyRules.TagBlackoutSeconds - 1e-3f, out _), T);
        }

        [Test]
        public void EchoWiden_Fov_Minus2_BackTo0_InsideTheCap()
        {
            Assert.AreEqual(-2f, EchoFeelRules.RevealFov(-1f), T, "held while covered — no step at 3.0 s");
            Assert.AreEqual(-2f, EchoFeelRules.RevealFov(0f), T);
            Assert.AreEqual(0f, EchoFeelRules.RevealFov(EchoFeelRules.RevealSeconds), T);
            float last = -2f;
            for (float t = 0f; t <= .55f; t += 1f / 144f)
            {
                float f = EchoFeelRules.RevealFov(t);
                Assert.GreaterOrEqual(f, last - T, "only returns"); last = f;
                Assert.LessOrEqual(Mathf.Abs(FeelRules.CapFov(f + FeelRules.PunchFov * 0f)), FeelRules.MaxFovOffset);
            }
            Assert.GreaterOrEqual(EchoFeelRules.RevealFov(0f), -FeelRules.MaxFovOffset, "inside the FOV cap");
        }

        [Test]
        public void Knock_Small_Smooth_LowestPriority()
        {
            Assert.AreEqual(Vector3.zero, EchoFeelRules.KnockRotation(-.01f));
            Assert.AreEqual(Vector3.zero, EchoFeelRules.KnockRotation(EchoFeelRules.KnockSeconds));
            Assert.AreEqual(.1f, EchoFeelRules.KnockSeconds, T, "0.1 s (spec)");
            float maxRot = 0f, maxPos = 0f, maxStep = 0f; Vector3 last = Vector3.zero;
            for (float t = 0f; t <= .12f; t += 1f / 1000f)
            {
                var r = EchoFeelRules.KnockRotation(t); var p = EchoFeelRules.KnockPosition(t);
                maxRot = Mathf.Max(maxRot, r.magnitude); maxPos = Mathf.Max(maxPos, p.magnitude);
                maxStep = Mathf.Max(maxStep, (r - last).magnitude); last = r;
            }
            Assert.LessOrEqual(maxRot, .12f + T, "≤ 0.12°"); Assert.LessOrEqual(maxPos, .002f + T, "≤ 2 mm");
            Assert.Greater(maxRot, .03f, "still felt");
            Assert.Less(maxStep, .02f, "no jump at the start or the end (1 ms steps)");
            Assert.AreEqual(.4f, FeelRules.PriorityScale(true), T, "under any other one-shot: 40 %");
        }
    }
}
