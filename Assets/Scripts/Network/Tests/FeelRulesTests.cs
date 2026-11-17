using Marco.Network.Round;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // V29 A-1 · A-2 카메라 · 타격감 (연출.md 2.2 · 4.2 · 15.3 · 16.2).
    public class FeelRulesTests
    {
        private const float T = 1e-4f;

        [Test]
        public void Caps_AfterTheSum()
        {
            // position: hit kick 1.2 cm + a 1.4 cm sprint bob would be 2.6 cm → capped at 2 cm
            var position = FeelRules.CapPosition(new Vector3(0f, -.014f, -FeelRules.HitKickBackMeters));
            Assert.LessOrEqual(position.magnitude, FeelRules.MaxPositionMeters + T);
            // rotation: hit 0.5° + miss 0.3° (V30 pitch only) + hurt 0.6° / 0.4° all at their peaks
            var rotation = FeelRules.CapRotation(new Vector3(-.5f, 0f, 0f) + new Vector3(.3f, 0f, 0f) + new Vector3(-.4f, .6f, 0f) + new Vector3(0f, .6f, 0f));
            Assert.LessOrEqual(rotation.magnitude, FeelRules.MaxRotationDegrees + T);
            Assert.AreEqual(5f, FeelRules.CapFov(9f), T);
            Assert.AreEqual(-5f, FeelRules.CapFov(-8f), T);
        }

        [Test]
        public void Fov_AreTheDocumentValues()
        {
            Assert.AreEqual(5f, FeelRules.SprintFov, T, "질주 +5°");
            Assert.AreEqual(-3f, FeelRules.SneakFov, T, "잠행 −3°");
            Assert.AreEqual(-5f, FeelRules.PunchFov, T, "punch-in −5°");
            Assert.AreEqual(5f, FeelRules.Follow(0f, FeelRules.SprintFov, FeelRules.FovInSeconds, FeelRules.FovInSeconds), T, "+5° reached at 0.2 s");
            Assert.AreEqual(0f, FeelRules.Follow(5f, 0f, FeelRules.SprintFovOutSeconds, FeelRules.SprintFovOutSeconds), T, "back at 0.25 s");
            Assert.AreEqual(-5f, FeelRules.PunchFovOffset(FeelRules.PunchRiseSeconds), T, "punch-in peak at 0.15 s");
            Assert.AreEqual(0f, FeelRules.PunchFovOffset(FeelRules.PunchRiseSeconds + FeelRules.PunchReturnSeconds), T, "back at 1.15 s");
        }

        [Test]
        public void MomentaryEffects_EndWithinHalfASecond()
        {
            Assert.LessOrEqual(FeelRules.HitKickSeconds, .5f);
            Assert.LessOrEqual(FeelRules.MissSeconds, .5f);
            Assert.LessOrEqual(FeelRules.HurtSeconds, .5f + T);
            Assert.LessOrEqual(FeelRules.FlashSeconds, .5f);
            Assert.AreEqual(Vector3.zero, FeelRules.HitKickRotation(FeelRules.HitKickSeconds));
            Assert.AreEqual(Vector3.zero, FeelRules.MissRotation(FeelRules.MissSeconds));
            Assert.AreEqual(Vector3.zero, FeelRules.HurtRotation(FeelRules.HurtSeconds, 1f, 3f));
            Assert.AreEqual(0f, FeelRules.FlashAlpha(FeelRules.FlashSeconds), T);
            Assert.AreEqual(FeelRules.FlashMaxAlpha, FeelRules.FlashAlpha(FeelRules.FlashRiseSeconds), T, "4.2: max at 0.15 s");
        }

        [Test]
        public void Impulse_NoOvershoot_ContinuousAndBackToZero()
        {
            float previous = 0f, peak = 0f;
            for (float t = 0f; t <= .3f; t += .001f)
            {
                float v = FeelRules.Impulse(t, .04f, .25f);
                Assert.That(v, Is.InRange(0f, 1.15f), "overshoot ≤ 15 %");
                if (t > .041f) Assert.LessOrEqual(v, previous + 1e-4f, "after the peak it only falls (critically damped)");
                if (t > .04f) Assert.Less(Mathf.Abs(v - previous), .06f, "no jump at the join or in the decay, t=" + t);   // the 40 ms rise is fast by design
                peak = Mathf.Max(peak, v); previous = v;
            }
            Assert.AreEqual(1f, peak, .01f);
            Assert.AreEqual(0f, FeelRules.Impulse(.25f, .04f, .25f), T);
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void SameValueAtTheSameTime_AtAnyFrameRate(int fps)
        {
            // every curve is a function of the time since its event: stepping frames lands on the same values
            float t = 0f;
            int frames = Mathf.RoundToInt(.1f * fps);
            for (int i = 0; i < frames; i++) t += 1f / fps;
            Assert.AreEqual(FeelRules.Impulse(.1f, .04f, .25f), FeelRules.Impulse(t, .04f, .25f), .03f);
            Assert.AreEqual(FeelRules.FlashAlpha(.1f), FeelRules.FlashAlpha(t), .02f);
            Assert.AreEqual(FeelRules.Follow(0f, 5f, .1f, .2f), FeelRules.Follow(0f, 5f, t, .2f), .3f);
        }

        // V30: the miss lean is pitch only, 0.3° (V29's yaw 0.35° / roll 0.2° assumed a right-hand swing the client cannot know).
        [Test]
        public void Miss_PitchOnly()
        {
            var peak = FeelRules.MissRotation(FeelRules.MissRiseSeconds);
            Assert.AreEqual(.3f, peak.x, T);
            Assert.AreEqual(0f, peak.y, T, "no yaw");
            Assert.AreEqual(0f, peak.z, T, "no roll");
        }

        [Test]
        public void SeekerRim_FifteenHundredthsOfASecond_InTheSeekerRimColour()
        {
            Assert.AreEqual(0f, FeelRules.RimColor(FeelRules.RimSeconds).maxColorComponent, T, "gone at 0.15 s");
            var peak = FeelRules.RimColor(FeelRules.RimRiseSeconds);
            Assert.AreEqual(FeelRules.SeekerFlashColor.r * FeelRules.RimIntensity, peak.r, 1e-3f, "#EF6A4C, peak at 40 ms");
            Assert.AreEqual(FeelRules.SeekerFlashColor.g / FeelRules.SeekerFlashColor.r, peak.g / peak.r, 1e-3f, "same hue");
        }

        [Test]
        public void HeadBob_CountsTowardTheCap_ButIsNeverCut()
        {
            // a 1.6 cm bob leaves 0.4 cm for the hit kick's 1.2 cm
            var bob = new Vector3(0f, .016f, 0f);
            var kick = new Vector3(0f, 0f, -FeelRules.HitKickBackMeters);
            Assert.AreEqual(.004f, FeelRules.CapPositionWithBob(kick, bob).magnitude, 1e-5f);
            Assert.LessOrEqual((FeelRules.CapPositionWithBob(kick, bob) + bob).magnitude, FeelRules.MaxPositionMeters + 1e-5f);
            // a 3.3 cm sprint bob leaves nothing — the bob itself is not touched
            Assert.AreEqual(0f, FeelRules.CapPositionWithBob(kick, new Vector3(0f, .033f, 0f)).magnitude, 1e-6f);
            Assert.AreEqual(.012f, FeelRules.CapPositionWithBob(kick, Vector3.zero).magnitude, 1e-5f, "no bob: the full kick");
            // the baseline is a time-based low-pass: same at any frame rate
            foreach (int fps in new[] { 30, 60, 144 })
            {
                Vector3 b = Vector3.zero;
                for (int i = 0; i < fps; i++) b = FeelRules.Baseline(b, Vector3.up, 1f / fps);
                Assert.AreEqual(1f - Mathf.Exp(-1f / FeelRules.BobBaselineSeconds), b.y, 1e-3f, fps + " fps");
            }
        }

        // V31 벽 통과: one effect per entry, none while inside, again after leaving.
        [Test]
        public void WallPass_EnterStayLeaveReenter()
        {
            bool[] inside = { false, true, true, true, false, false, true, true, false };
            int effects = 0; bool was = false;
            foreach (bool now in inside) { if (WallPassRules.Entered(was, now)) effects++; was = now; }
            Assert.AreEqual(2, effects, "enter → 1, stay → 0, leave → 0, re-enter → 1");
            Assert.IsFalse(WallPassRules.Entered(true, true));
            Assert.IsFalse(WallPassRules.Entered(true, false));
            Assert.IsTrue(WallPassRules.Entered(false, true));
            // a thin wall crossed within one frame (outside before and after) still counts once; leaving does not
            Assert.IsTrue(WallPassRules.Entered(false, false, true), "crossed in one frame");
            Assert.IsFalse(WallPassRules.Entered(true, false, true), "the exit surface is not an entry");
        }

        [Test]
        public void WallPass_Overlay_20msIn_80msOut()
        {
            Assert.AreEqual(0f, WallPassRules.Alpha(-.001f), T);
            Assert.AreEqual(WallPassRules.MaxAlpha, WallPassRules.Alpha(WallPassRules.InSeconds), T, "max 0.25 at 20 ms");
            Assert.Less(WallPassRules.Alpha(.06f), WallPassRules.MaxAlpha * .5f, "falling");
            Assert.AreEqual(0f, WallPassRules.Alpha(.1f), T, "gone at 0.1 s");
            Assert.AreEqual(.1f, WallPassRules.TotalSeconds, T);
        }

        [Test]
        public void Priority_LowerToFortyPercent()
        {
            Assert.AreEqual(.4f, FeelRules.PriorityScale(true), T);
            Assert.AreEqual(1f, FeelRules.PriorityScale(false), T);
        }

        [Test]
        public void HurtShake_TurnsAwayFromTheSeeker_PitchOnlyWithoutDirection()
        {
            Vector3 right = FeelRules.HurtRotation(FeelRules.HurtRiseSeconds, 1f, 2f), none = FeelRules.HurtRotation(FeelRules.HurtRiseSeconds, 0f, 2f);
            Assert.AreEqual(FeelRules.HurtYawDegrees, right.y, T);
            Assert.AreEqual(0f, none.y, T);
            Assert.AreEqual(-FeelRules.HurtPitchDegrees, none.x, T);
            // residual: small, low-frequency, and gone at the end
            float tResidual = FeelRules.HurtRiseSeconds + FeelRules.HurtDecaySeconds + .02f;
            Assert.LessOrEqual(FeelRules.HurtRotation(tResidual, 1f, 2f).magnitude, FeelRules.HurtResidualDegrees * 1.5f);
        }

        [Test]
        public void SeekerFlash_AndRunnerSwell_NeverOnTheSameScreen()
        {
            foreach (bool seeker in new[] { true, false })
                Assert.IsFalse(FeelRules.ShowsSeekerFlash(seeker) && FeelRules.ShowsRunnerSwell(seeker));
            Assert.AreEqual(new Color32(0xEF, 0x6A, 0x4C, 0xFF), (Color32)FeelRules.SeekerFlashColor, "연출 4.2 #EF6A4C");
        }

        // V31 B-1 스태미나 소진 수축: 0 → 0.12 over 0.3 s (ease-out), held without a pulse, 0.12 → 0 over 0.3 s after the end;
        // a change mid-fade continues from the value shown.
        [Test]
        public void Exhaustion_Contraction_InHoldOut()
        {
            Assert.AreEqual(0f, ExhaustionRules.Alpha(0f, true, 0f), T, "starts from nothing");
            Assert.AreEqual(ExhaustionRules.MaxAlpha, ExhaustionRules.Alpha(0f, true, ExhaustionRules.FadeSeconds), T, "0.12 at 0.3 s");
            Assert.AreEqual(.12f, ExhaustionRules.MaxAlpha, T);
            Assert.AreEqual(.3f, ExhaustionRules.FadeSeconds, T);
            float last = -1f;
            for (float t = 0f; t <= ExhaustionRules.FadeSeconds + T; t += .01f)
            {
                float a = ExhaustionRules.Alpha(0f, true, t);
                Assert.GreaterOrEqual(a, last - T, $"rises monotonically (t={t:F2})"); last = a;
            }
            for (float t = ExhaustionRules.FadeSeconds; t < 6f; t += .05f)
                Assert.AreEqual(ExhaustionRules.MaxAlpha, ExhaustionRules.Alpha(0f, true, t), T, $"held, no pulse (t={t:F2})");
            Assert.AreEqual(ExhaustionRules.MaxAlpha, ExhaustionRules.Alpha(ExhaustionRules.MaxAlpha, false, 0f), T, "the end starts from the shown value");
            Assert.AreEqual(0f, ExhaustionRules.Alpha(ExhaustionRules.MaxAlpha, false, ExhaustionRules.FadeSeconds), T, "gone 0.3 s after the end");
            last = 1f;
            for (float t = 0f; t <= ExhaustionRules.FadeSeconds + T; t += .01f)
            {
                float a = ExhaustionRules.Alpha(ExhaustionRules.MaxAlpha, false, t);
                Assert.LessOrEqual(a, last + T, $"falls monotonically (t={t:F2})"); last = a;
            }
            // fast-in (ease-out): more than half of the way in the first third
            Assert.Greater(ExhaustionRules.Alpha(0f, true, ExhaustionRules.FadeSeconds / 3f), ExhaustionRules.MaxAlpha * .5f);
            // reversal mid-fade: no jump
            float shown = ExhaustionRules.Alpha(0f, true, .1f);
            Assert.AreEqual(shown, ExhaustionRules.Alpha(shown, false, 0f), T);
            // frame-rate independent: the value depends on the time since the change only
            foreach (float fps in new[] { 30f, 60f, 144f })
            {
                float dt = 1f / fps, t = 0f;
                while (t + dt <= .2f) t += dt;
                Assert.AreEqual(FeelRules.Follow(0f, ExhaustionRules.MaxAlpha, t, ExhaustionRules.FadeSeconds), ExhaustionRules.Alpha(0f, true, t), T);
            }
            Assert.AreEqual(Color.black, ExhaustionRules.Colour);
            Assert.AreEqual(.09f, ExhaustionRules.CornerDepth, T, "corner 9 %");
            Assert.AreEqual(.035f, ExhaustionRules.EdgeDepth, T, "edge middle 3.5 %");
        }

        // V31 B-1: the sprint FOV goes back when the exhaustion starts (0.25 s ease-out) and does not rise while it lasts,
        // nor in the 0.1 s after (a client's server correction can flip IsSprinting for a frame).
        [Test]
        public void Exhaustion_SprintFov_BackAndNotRaised()
        {
            Assert.IsTrue(ExhaustionRules.SprintFovAllowed(true, false, -1f), "sprinting, never exhausted");
            Assert.IsFalse(ExhaustionRules.SprintFovAllowed(false, false, -1f));
            Assert.IsFalse(ExhaustionRules.SprintFovAllowed(true, true, 0f), "exhausted: never raised");
            Assert.IsFalse(ExhaustionRules.SprintFovAllowed(true, false, .05f), "0.05 s after the end: still held");
            Assert.IsTrue(ExhaustionRules.SprintFovAllowed(true, false, ExhaustionRules.SprintFovHoldSeconds), "0.1 s after the end");
            Assert.IsTrue(ExhaustionRules.SprintFovAllowed(true, false, 10f));
            // the FOV curve the stack follows from the exhaustion start: +5 → 0 in 0.25 s, monotone, ease-out
            Assert.AreEqual(.25f, FeelRules.SprintFovOutSeconds, T);
            float last = FeelRules.SprintFov;
            for (float t = 0f; t <= FeelRules.SprintFovOutSeconds + T; t += .01f)
            {
                float fov = FeelRules.Follow(FeelRules.SprintFov, 0f, t, FeelRules.SprintFovOutSeconds);
                Assert.LessOrEqual(fov, last + T, $"never rises again (t={t:F2})"); last = fov;
            }
            Assert.AreEqual(0f, FeelRules.Follow(FeelRules.SprintFov, 0f, FeelRules.SprintFovOutSeconds, FeelRules.SprintFovOutSeconds), T, "back at 0.25 s");
        }

        [Test]
        public void OneEvent_OneEffect()
        {
            var go = new GameObject("feel test");
            try
            {
                var stack = go.AddComponent<CameraFeelStack>();
                int wound = CameraFeelStack.SeekerWoundHits, echo = CameraFeelStack.SeekerEchoHits, miss = CameraFeelStack.SeekerMisses;
                stack.OnLocalSwing(SwingResult.Wounded);
                stack.OnLocalSwing(SwingResult.Echoed);
                stack.OnLocalSwing(SwingResult.Missed);
                stack.OnLocalSwing(SwingResult.Rejected);
                Assert.AreEqual(wound + 1, CameraFeelStack.SeekerWoundHits, "first hit once");
                Assert.AreEqual(echo + 1, CameraFeelStack.SeekerEchoHits, "Echo transition once");
                Assert.AreEqual(miss + 1, CameraFeelStack.SeekerMisses, "miss once; Rejected adds nothing");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
