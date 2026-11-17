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
            // rotation: hit 0.5° + miss 0.35° / 0.2° + hurt 0.6° / 0.4° all at their peaks
            var rotation = FeelRules.CapRotation(new Vector3(-.5f, 0f, 0f) + new Vector3(0f, .35f, -.2f) + new Vector3(-.4f, .6f, 0f));
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
            Assert.AreEqual(Vector3.zero, FeelRules.MissRotation(FeelRules.MissSeconds, 1f));
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
