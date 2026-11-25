using System.Collections.Generic;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // V32 연출.md 2.1 — the walking bob on the own footstep phase (WalkBobRules / WalkBobProfile).
    public class WalkBobPhaseTests
    {
        // The camera height above the body per gait phase measured in the build (V31 runs, Runner walk, cm from the mean,
        // 32 bins, bin centres (i + 0.5) / 32): lows at 0.26 (deep) and 0.84 (shallow), SFX at 0 and 0.5.
        private static readonly float[] MeasuredCm =
        {
            .33f, .49f, .54f, .40f, .11f, -.26f, -.59f, -.80f, -.86f, -.83f, -.77f, -.67f, -.54f, -.38f, -.22f, -.06f,
            .10f, .33f, .66f, .93f, .99f, .85f, .59f, .29f, .03f, -.13f, -.19f, -.19f, -.15f, -.09f, -.01f, .07f,
        };

        // The clip's height at a phase (m), smooth (Catmull-Rom) between the measured bins.
        private static float Clip(float phase)
        {
            var t = new float[MeasuredCm.Length];
            for (int i = 0; i < t.Length; i++) t[i] = MeasuredCm[i] * .01f;
            return WalkBobRules.Sample(t, phase);
        }

        private static float[] LearnedTable()
        {
            var table = new float[WalkBobRules.Bins];
            for (int b = 0; b < table.Length; b++) table[b] = Clip((b + .5f) / table.Length);
            float mean = 0f; foreach (var v in table) mean += v; mean /= table.Length;
            for (int b = 0; b < table.Length; b++) table[b] -= mean;
            return table;
        }

        [Test]
        public void Lows_FoundAndPairedWithTheSfxPhases()
        {
            var table = LearnedTable();
            WalkBobRules.FindLows(table, out float first, out float second);
            Assert.AreEqual(.26f, first, .02f, "deep low");
            Assert.AreEqual(.84f, second, .02f, "shallow low");
            Assert.IsTrue(WalkBobRules.PairLows(first, second, out float a, out float b));
            Assert.AreEqual(-.16f, a, .02f, "the low 0.16 cycle before SFX 0 goes onto SFX 0");
            Assert.AreEqual(.26f, b, .02f, "the low 0.24 cycle before SFX 0.5 goes onto SFX 0.5");
        }

        [Test]
        public void Warp_IsContinuous_AndPutsTheLowsOnZeroAndHalf()
        {
            var table = LearnedTable();
            WalkBobRules.FindLows(table, out float first, out float second);
            WalkBobRules.PairLows(first, second, out float a, out float b);
            Assert.AreEqual(a, WalkBobRules.Warp(0f, a, b), 1e-5f);
            Assert.AreEqual(b, WalkBobRules.Warp(.5f, a, b), 1e-5f);
            Assert.AreEqual(a + 1f, WalkBobRules.Warp(.99999f, a, b), 1e-3f, "continuous across the cycle seam");
            // the re-phased height: lows exactly at 0 and 0.5, the same amplitude as the clip
            float min0 = float.MaxValue, at0 = 0f, min5 = float.MaxValue, at5 = 0f, lo = float.MaxValue, hi = float.MinValue, last = float.NaN, maxStep = 0f;
            for (int i = 0; i <= 4000; i++)
            {
                float p = i / 4000f;
                float y = WalkBobRules.Sample(table, p) + WalkBobRules.Correction(table, p, a, b);
                lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                if (!float.IsNaN(last)) maxStep = Mathf.Max(maxStep, Mathf.Abs(y - last)); last = y;
                float d0 = Mathf.Min(p, 1f - p);
                if (d0 < .25f && y < min0) { min0 = y; at0 = d0; }
                if (Mathf.Abs(p - .5f) < .25f && y < min5) { min5 = y; at5 = Mathf.Abs(p - .5f); }
            }
            Assert.LessOrEqual(at0, 1.5f / WalkBobRules.Bins, "low on SFX 0");
            Assert.LessOrEqual(at5, 1.5f / WalkBobRules.Bins, "low on SFX 0.5");
            float clipLo = float.MaxValue, clipHi = float.MinValue;
            foreach (var v in table) { clipLo = Mathf.Min(clipLo, v); clipHi = Mathf.Max(clipHi, v); }
            Assert.AreEqual(clipHi - clipLo, hi - lo, .0005f, "same bob amplitude (peak to peak)");
            Assert.Less(maxStep, .0005f, "no jump along the phase");
        }

        // End to end at 30 / 60 / 144 fps: walk at 5 m/s (cycle 1.2 s), learn from the live height, then the frame of every
        // own footstep (FootstepIndex = floor(cycles × 2) steps — the SFX) against the frames of the bob lows.
        [TestCase(30f)]
        [TestCase(60f)]
        [TestCase(144f)]
        public void SfxAndBobLow_WithinOneFrame(float fps)
        {
            var profile = new WalkBobProfile();
            float dt = 1f / fps, weight = 0f; double cycles = .37;
            var height = new List<float>(); var sfx = new List<int>(); int lastIndex = (int)System.Math.Floor(cycles * 2);
            int readyFrame = -1;
            for (int f = 0; f < fps * 9f; f++)
            {
                cycles += dt / 1.2;
                int index = (int)System.Math.Floor(cycles * 2); float phase = (float)(cycles - System.Math.Floor(cycles));
                float live = 1.55f + Clip(phase);
                profile.Observe(index / 2, phase, live, f * dt >= WalkBobRules.SettleSeconds);
                weight = WalkBobRules.Fade(weight, profile.Ready, dt);
                if (profile.Ready && readyFrame < 0) readyFrame = f;
                height.Add(live + profile.Correction(phase) * WalkBobRules.Shown(weight));
                if (index > lastIndex) sfx.Add(f); lastIndex = index;
            }
            Assert.IsTrue(profile.Ready, "learned");
            Assert.LessOrEqual(readyFrame * dt, 2.8f, "ready after the first whole cycle (settle 0.3 s + ≤ 2 cycles)");
            int from = readyFrame + Mathf.CeilToInt(WalkBobRules.BlendSeconds / dt) + 1;
            int w = Mathf.Max(1, Mathf.RoundToInt(.1f / dt));
            var lows = new List<int>();
            for (int f = Mathf.Max(from, w); f < height.Count - w; f++)
            {
                bool min = true;
                for (int k = f - w; k <= f + w && min; k++) if (height[k] < height[f]) min = false;
                if (min) lows.Add(f);
            }
            int checkedSfx = 0;
            foreach (int s in sfx)
            {
                if (s < from + w || s > height.Count - w - 1) continue;
                int best = int.MaxValue; foreach (int l in lows) if (Mathf.Abs(l - s) < Mathf.Abs(best)) best = l - s;
                // one frame, and never less than one 60 fps frame (16.7 ms) at high frame rates
                Assert.LessOrEqual(Mathf.Abs(best) * dt, Mathf.Max(dt, 1f / 60f) + 1e-5f, $"SFX frame {s}: nearest bob low {best:+0;-0;0} frames ({fps} fps)");
                checkedSfx++;
            }
            Assert.GreaterOrEqual(checkedSfx, 8);
        }

        // Start / stop / walk ↔ run: the correction fades over 0.15 s, linear in time whatever the frame rate, and the shown
        // weight (smoothstep) never steps.
        [Test]
        public void Fade_SameAt30_60_144fps_AndNoJump()
        {
            foreach (float fps in new[] { 30f, 60f, 144f })
            {
                float dt = 1f / fps, w = 0f, t = 0f, lastShown = 0f, maxStep = 0f;
                while (t < WalkBobRules.BlendSeconds - 1e-4f)
                {
                    w = WalkBobRules.Fade(w, true, dt); t += dt;
                    float s = WalkBobRules.Shown(w); maxStep = Mathf.Max(maxStep, s - lastShown); lastShown = s;
                }
                Assert.AreEqual(1f, w, 1e-4f, $"in after 0.15 s ({fps} fps)");
                Assert.LessOrEqual(maxStep, 1.5f * dt / WalkBobRules.BlendSeconds + 1e-4f, "smoothstep slope ≤ 1.5 × linear");
                t = 0f;
                while (t < WalkBobRules.BlendSeconds - 1e-4f) { w = WalkBobRules.Fade(w, false, dt); t += dt; }
                Assert.AreEqual(0f, w, 1e-4f, $"out after 0.15 s ({fps} fps)");
            }
            // half way at 75 ms, whatever the frame rate
            foreach (float fps in new[] { 40f, 80f, 160f })
            {
                float w = 0f; for (int i = 0; i < Mathf.RoundToInt(.075f * fps); i++) w = WalkBobRules.Fade(w, true, 1f / fps);
                Assert.AreEqual(.5f, w, 1e-3f);
            }
        }

        // Stopping mid-step: the phase freezes (no movement, no gait advance) and the shift fades out — never faster than the
        // 0.15 s smoothstep fade of the shift itself (no jump), and back on the live head within 0.15 s.
        [Test]
        public void Stop_FadesWithoutAJump()
        {
            var profile = new WalkBobProfile();
            double cycles = 0; float dt = 1f / 60f, weight = 0f;
            for (int f = 0; f < 60 * 5; f++)
            {
                cycles += dt / 1.2; float phase = (float)(cycles - System.Math.Floor(cycles));
                profile.Observe((int)System.Math.Floor(cycles), phase, Clip(phase), f * dt >= .3f);
                weight = WalkBobRules.Fade(weight, profile.Ready, dt);
            }
            Assert.IsTrue(profile.Ready); Assert.AreEqual(1f, weight, 1e-4f);
            float frozen = (float)(cycles - System.Math.Floor(cycles));
            float last = Clip(frozen) + profile.Correction(frozen) * WalkBobRules.Shown(weight), maxStep = 0f;
            for (int f = 0; f < 30; f++)
            {
                weight = WalkBobRules.Fade(weight, false, dt);
                float y = Clip(frozen) + profile.Correction(frozen) * WalkBobRules.Shown(weight);
                maxStep = Mathf.Max(maxStep, Mathf.Abs(y - last)); last = y;
            }
            float shift = Mathf.Abs(profile.Correction(frozen));
            Assert.LessOrEqual(maxStep, shift * 1.5f * dt / WalkBobRules.BlendSeconds + 1e-5f, $"no step beyond the fade (shift {shift * 100:F2} cm)");
            Assert.AreEqual(Clip(frozen), last, 1e-6f, "back on the live head");
        }

        [Test]
        public void Learner_DropsCyclesWithAnInvalidFrame_AndResets()
        {
            var profile = new WalkBobProfile();
            double cycles = 0; float dt = 1f / 60f;
            for (int f = 0; f < 60 * 4; f++)
            {
                cycles += dt / 1.2; float phase = (float)(cycles - System.Math.Floor(cycles));
                profile.Observe((int)System.Math.Floor(cycles), phase, Clip(phase), f % 50 != 0);   // one bad frame per cycle
            }
            Assert.IsFalse(profile.Ready, "no whole valid cycle");
            for (int f = 0; f < 60 * 3; f++)
            {
                cycles += dt / 1.2; float phase = (float)(cycles - System.Math.Floor(cycles));
                profile.Observe((int)System.Math.Floor(cycles), phase, Clip(phase), true);
            }
            Assert.IsTrue(profile.Ready);
            profile.Reset();
            Assert.IsFalse(profile.Ready); Assert.AreEqual(0f, profile.Correction(.3f));
        }
    }
}
