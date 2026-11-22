using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // [11-22 V43] 연출 4.5 첫 적중 대상 몸 플래시 (TargetRimRules / TargetRimFlash) and 연출 2.2 메아리 비행 관성 (EchoInertia).
    // Set MARCO_VD_CSV_DIR to also write the curves as CSV (the capture folder of the V43 report).
    public class PresentationVisibleTests
    {
        static readonly int FeelRim = Shader.PropertyToID("_FeelRim");

        static void Csv(string name, IEnumerable<string> rows)
        {
            string dir = Environment.GetEnvironmentVariable("MARCO_VD_CSV_DIR");
            if (string.IsNullOrEmpty(dir)) return;
            try { Directory.CreateDirectory(dir); File.WriteAllLines(Path.Combine(dir, name), rows); } catch (Exception) { }
        }
        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

        // ---- 연출 4.5 target rim ----

        [Test]
        public void TargetRim_Curve_RisesFast_FallsWithoutBounce_AndIsExactlyZeroAtTwoTenths()
        {
            Assert.AreEqual(.2f, TargetRimRules.Seconds, "4.5 spec: 0.2 s");
            Assert.AreEqual(0f, TargetRimRules.Weight(-.01f)); Assert.AreEqual(0f, TargetRimRules.Weight(0f), 1e-6f);
            Assert.AreEqual(1f, TargetRimRules.Weight(TargetRimRules.RiseSeconds), 1e-4f, "peak at the end of the rise");
            float previous = 1f; var rows = new List<string> { "t_s,weight" };
            for (float t = TargetRimRules.RiseSeconds; t < .2f; t += 1f / 240f)
            {
                float w = TargetRimRules.Weight(t); rows.Add($"{F(t)},{F(w)}");
                Assert.LessOrEqual(w, previous + 1e-6f, $"no bounce at {t}"); Assert.GreaterOrEqual(w, 0f);
                previous = w;
            }
            Assert.AreEqual(0f, TargetRimRules.Weight(.2f)); Assert.AreEqual(0f, TargetRimRules.Weight(1f));
            Csv("curve_targetrim.csv", rows);
        }

        [Test]
        public void TargetRim_Colour_IsTheRunnerRimNotANewColour()
        {
            var peak = TargetRimRules.RimColor(TargetRimRules.RiseSeconds);
            var baseRim = SourceSnapshots.RunnerRim;
            Assert.AreEqual(baseRim.r * TargetRimRules.Intensity, peak.r, 1e-4f);
            Assert.AreEqual(baseRim.g * TargetRimRules.Intensity, peak.g, 1e-4f);
            Assert.AreEqual(baseRim.b * TargetRimRules.Intensity, peak.b, 1e-4f);
            Assert.AreEqual(new Color32(0x7F, 0xE7, 0xE0, 0xFF), (Color32)baseRim, "§16.1 도망자 림라이트 #7FE7E0");
        }

        [Test]
        public void TargetRim_NoStandingRim_FeelRimIsZeroBeforeDuringAfter()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var renderer = body.GetComponent<Renderer>(); var block = new MaterialPropertyBlock();
                var flash = new TargetRimFlash();
                Assert.IsFalse(flash.Active);
                float start = 10f; var rows = new List<string> { "t_s,rim_r" }; float peak = 0f;
                flash.Begin(new[] { renderer }, start);
                Assert.IsTrue(flash.Active);
                for (float t = 0f; t < .6f; t += 1f / 60f)
                {
                    flash.Tick(start + t);
                    renderer.GetPropertyBlock(block); var rim = block.GetColor(FeelRim);
                    rows.Add($"{F(t)},{F(rim.r)}"); peak = Mathf.Max(peak, rim.g);
                    if (t > .2f) Assert.AreEqual(0f, rim.maxColorComponent, 0f, $"_FeelRim is 0 again at {t}");
                }
                Assert.Greater(peak, .5f, "the flash did show"); Assert.IsFalse(flash.Active, "it ended by itself");
                renderer.GetPropertyBlock(block); Assert.AreEqual(0f, block.GetColor(FeelRim).maxColorComponent);
                Csv("rim_feelrim_over_time.csv", rows);
            }
            finally { UnityEngine.Object.DestroyImmediate(body); }
        }

        [Test]
        public void TargetRim_Release_WritesZeroBackMidFlash()
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var renderer = body.GetComponent<Renderer>(); var block = new MaterialPropertyBlock();
                var flash = new TargetRimFlash();
                flash.Begin(new[] { renderer }, 0f); flash.Tick(.05f);
                renderer.GetPropertyBlock(block); Assert.Greater(block.GetColor(FeelRim).maxColorComponent, .1f);
                flash.Release();   // a round end, a rebind, the Seeker leaving the Seeker role
                renderer.GetPropertyBlock(block); Assert.AreEqual(0f, block.GetColor(FeelRim).maxColorComponent);
                Assert.IsFalse(flash.Active);
                flash.Begin(new Renderer[0], 1f); Assert.IsFalse(flash.Active, "no renderers = nothing to flash");
            }
            finally { UnityEngine.Object.DestroyImmediate(body); }
        }

        // ---- 연출 2.2 echo flight inertia ----

        const float Speed = 6f;   // 기획서 3.2 메아리 6.0 m/s

        // Flies along +x at `Speed` for `moveSeconds` (counted in whole frames), then stops; returns the drawn x per frame.
        static List<(float t, float real, float drawn, float speed)> Fly(float fps, float moveSeconds, float totalSeconds, float reverseAt = -1f)
        {
            var inertia = new EchoInertia(); var rows = new List<(float, float, float, float)>();
            float dt = 1f / fps, real = 0f, previousDrawn = 0f, dir = 1f;
            int moveFrames = Mathf.RoundToInt(moveSeconds * fps), totalFrames = Mathf.RoundToInt(totalSeconds * fps), reverseFrame = reverseAt >= 0f ? Mathf.RoundToInt(reverseAt * fps) : int.MaxValue;
            inertia.Step(new Vector3(real, 0, 0), dt, true);
            for (int n = 1; n <= totalFrames; n++)
            {
                if (n > reverseFrame) dir = -1f;
                if (n <= moveFrames) real += dir * Speed * dt;
                float drawn = inertia.Step(new Vector3(real, 0, 0), dt, true).x;
                rows.Add((n * dt, real, drawn, (drawn - previousDrawn) / dt)); previousDrawn = drawn;
            }
            return rows;
        }

        [Test]
        public void Inertia_Start_ReachesFlightSpeedInAboutPointThreeSeconds_OvershootAtMost15Percent()
        {
            var rows = Fly(60f, 3f, 3f); var csv = new List<string> { "t_s,real_x,drawn_x,lag_m,drawn_speed" };
            float maxSpeed = 0f, atThird = 0f;
            foreach (var r in rows)
            {
                csv.Add($"{F(r.t)},{F(r.real)},{F(r.drawn)},{F(r.real - r.drawn)},{F(r.speed)}");
                maxSpeed = Mathf.Max(maxSpeed, r.speed); if (Mathf.Abs(r.t - .3f) < .009f) atThird = r.speed;
                Assert.GreaterOrEqual(r.real - r.drawn, -1e-4f, "the camera never passes the real position");
            }
            Csv("curve_echo_start.csv", csv);
            Assert.Greater(atThird / Speed, .97f, "within 3 % of the flight speed by 0.3 s"); Assert.Less(atThird / Speed, 1.03f);
            Assert.LessOrEqual(maxSpeed / Speed, 1.15f, "overshoot of the follow speed ≤ 15 %");
            Assert.LessOrEqual(maxSpeed / Speed, 1.001f, "critically damped: the drawn speed rises monotonically, no overshoot");
            // the frame's drawn speed is the mean over the frame (its middle is half a frame earlier): v (1 − (1 + ωt) e^(−ωt)), ω = 20
            foreach (var r in rows) { float tm = r.t - 1f / 120f, expected = 1f - (1f + 20f * tm) * Mathf.Exp(-20f * tm); if (Mathf.Abs(r.t - .1f) < .009f || Mathf.Abs(r.t - .2f) < .009f) Assert.AreEqual(expected, r.speed / Speed, .01f, $"closed form at {r.t}"); }
            var last = rows[rows.Count - 1];
            Assert.AreEqual(.6f, last.real - last.drawn, .005f, "steady lag = 6.0 m/s × 0.1 s");
            Assert.LessOrEqual(last.real - last.drawn, EchoInertia.MaxLagMeters);
        }

        [Test]
        public void Inertia_Stop_GlidesOutWithoutOvershoot_AndSettlesWithinAboutPointThreeSeconds()
        {
            var rows = Fly(60f, 1.5f, 2.5f); var csv = new List<string> { "t_s,real_x,drawn_x,lag_m,drawn_speed" };
            float settled = -1f;
            foreach (var r in rows)
            {
                csv.Add($"{F(r.t)},{F(r.real)},{F(r.drawn)},{F(r.real - r.drawn)},{F(r.speed)}");
                Assert.GreaterOrEqual(r.real - r.drawn, -1e-4f, $"no overshoot past the stop at {r.t}");
                if (r.t > 1.5f && settled < 0f && r.real - r.drawn < .01f) settled = r.t - 1.5f;
            }
            Csv("curve_echo_stop.csv", csv);
            Assert.Greater(settled, 0f); Assert.LessOrEqual(settled, .31f, "within 1 cm of the stop after ≤ 0.3 s");
            var last = rows[rows.Count - 1]; Assert.AreEqual(last.real, last.drawn, 1e-3f);
        }

        [Test]
        public void Inertia_Reversal_StaysUnderTheCap_AndIsFrameRateIndependent()
        {
            float maxLag = 0f; var csv = new List<string> { "t_s,real_x,drawn_x,lag_m" };
            foreach (var r in Fly(60f, 3f, 3f, reverseAt: 1.5f)) { maxLag = Mathf.Max(maxLag, Mathf.Abs(r.real - r.drawn)); csv.Add($"{F(r.t)},{F(r.real)},{F(r.drawn)},{F(r.real - r.drawn)}"); }
            Csv("curve_echo_reversal.csv", csv);
            Assert.LessOrEqual(maxLag, EchoInertia.MaxLagMeters + 1e-4f, "even a full reversal stays under the hard cap");
            float Lag(float fps) { var rows = Fly(fps, 1f, 1f); var l = rows[rows.Count - 1]; return l.real - l.drawn; }
            float reference = Lag(60f);
            foreach (float fps in new[] { 30f, 144f, 240f }) Assert.AreEqual(reference, Lag(fps), .005f, $"{fps} fps");
        }

        [Test]
        public void Inertia_Inactive_PassesTheRealPositionExactly_AndResets()
        {
            var inertia = new EchoInertia();
            var p = new Vector3(3f, 2f, 1f);
            Assert.AreEqual(p, inertia.Step(p, .016f, false));
            inertia.Step(p, .016f, true);
            var moved = p + new Vector3(.1f, 0, 0);
            Assert.AreNotEqual(moved.x, inertia.Step(moved, .016f, true).x, "a step is followed, not jumped to");
            Assert.AreEqual(moved, inertia.Step(moved, .016f, false), "off = exactly the real position");
            Assert.AreEqual(0f, inertia.Lag.magnitude);
            Assert.AreEqual(p, inertia.Step(p, .016f, true), "the first active frame starts from the real position (no jump)");
            // a teleport (a round reset, > 2.5 m in one frame) is not followed
            Assert.AreEqual(p + Vector3.right * 20f, inertia.Step(p + Vector3.right * 20f, .016f, true));
            // a long hitch cannot stretch the lag over the cap; NaN is passed through untouched
            inertia.Step(p, .016f, true); inertia.Step(p + Vector3.right * 2f, 0.5f, true);
            Assert.LessOrEqual(inertia.Lag.magnitude, EchoInertia.MaxLagMeters + 1e-4f);
            var nan = new Vector3(float.NaN, 0, 0); Assert.IsTrue(float.IsNaN(inertia.Step(nan, .016f, true).x));
        }
    }
}
