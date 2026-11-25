using Marco.Core.WinCondition;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // [11-22 V42b] 부록 A [발소리 등급]: per-role boundary on the 0.3 s average speed.
    public class PulseGradeTests
    {
        [Test]
        public void Boundaries_PerRole_SurfaceAlwaysFast()
        {
            Assert.AreEqual(5.5f, MovementPulseClock.RunnerFastSpeed); Assert.AreEqual(4.3f, MovementPulseClock.SeekerFastSpeed);
            foreach (float walk in new[] { 1.25f, 3.4f, 4.0f, 4.25f, 5.0f, 5.14f })
                Assert.IsFalse(MovementPulseClock.IsFastMovement(PlayerRole.Runner, walk, false), $"runner {walk} walks");
            foreach (float sprint in new[] { 6.0f, 6.375f, 7.5f })
                Assert.IsTrue(MovementPulseClock.IsFastMovement(PlayerRole.Runner, sprint, false), $"runner {sprint} sprints");
            foreach (float sneak in new[] { 3.5f, 4.15f })
                Assert.IsFalse(MovementPulseClock.IsFastMovement(PlayerRole.Seeker, sneak, false), $"seeker {sneak} sneaks");
            foreach (float chase in new[] { 5.05f, 5.2f, 5.25f, 5.4f, 5.67f })
                Assert.IsTrue(MovementPulseClock.IsFastMovement(PlayerRole.Seeker, chase, false), $"seeker {chase} chases");
            Assert.IsTrue(MovementPulseClock.IsFastMovement(PlayerRole.Runner, 2.4f, true), "surface swimming stays sprint");
            Assert.IsTrue(MovementPulseClock.IsFastMovement(PlayerRole.Seeker, 3f, true));
        }

        [Test]
        public void Window_AveragesThreeTicks_AndForgetsOlderOnes()
        {
            var w = new GradeSpeedWindow();
            Assert.AreEqual(5f, w.Push(.5f, .1f), 1e-4f);
            Assert.AreEqual(5.25f, w.Push(.55f, .1f), 1e-4f);
            Assert.AreEqual(5f, w.Push(.45f, .1f), 1e-4f);
            Assert.AreEqual(5f, w.Push(.5f, .1f), 1e-4f, "the first tick has left the 0.3 s window");
            Assert.AreEqual(7.5f, Push(w, .75f, .1f, 3), 1e-4f, "three sprint ticks fill it");
            Assert.AreEqual(0f, w.Push(20f, .1f), "a teleport empties the window");
            Assert.AreEqual(5f, w.Push(.5f, .1f), 1e-4f);
        }

        [Test]
        public void Window_Frames_SmoothsAlternatingNoise()
        {
            // the owner's frames: 5.00 m/s with ±0.06 m/s frame noise (the measured spread) never reaches 5.5
            var w = new GradeSpeedWindow(); float max = 0f;
            for (int i = 0; i < 600; i++) { float dt = i % 3 == 0 ? .0172f : .0161f; max = Mathf.Max(max, w.Push((i % 2 == 0 ? 5.06f : 4.94f) * dt, dt)); }
            Assert.Less(max, 5.1f);
            Assert.IsFalse(MovementPulseClock.IsFastMovement(PlayerRole.Runner, max, false));
        }

        [Test]
        public void Clock_WalkAtFiveMetresPerSecond_StaysWalk_TwoMetreSteps()
        {
            // 64 s at the measured server tick noise (4.90 .. 5.09): every step is a 2 m step and the grade never changes
            var clock = new MovementPulseClock(); var w = new GradeSpeedWindow(); var pos = Vector3.zero; int steps = 0, fast = 0;
            float[] noise = { 4.92f, 5.09f, 4.90f, 5.01f, 5.00f, 5.03f, 4.94f };
            clock.Advance(pos, .1f, true, false, out _);
            for (int i = 0; i < 640; i++)
            {
                float d = noise[i % noise.Length] * .1f; pos += Vector3.forward * d;
                bool f = MovementPulseClock.IsFastMovement(PlayerRole.Runner, w.Push(d, .1f), false);
                if (clock.Advance(pos, .1f, true, f, out _)) steps++;
                if (clock.Fast) fast++;
            }
            Assert.AreEqual(0, fast, "never the sprint grade");
            Assert.That(steps, Is.InRange(158, 161), "distance / 2 m");
        }

        private static float Push(GradeSpeedWindow w, float d, float dt, int n) { float a = 0f; for (int i = 0; i < n; i++) a = w.Push(d, dt); return a; }
    }
}
