using System.Collections.Generic;
using Marco.Network.Round;
using Marco.Network.Voice;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 부록 A [마이크 캘리브레이션] · [소리 등급] RMS 구간 (11-15). MicCalibration is pure, so every rule is checked here.
    public class MicCalibrationTests
    {
        private const float Silence = -50f;   // MicrophoneVisionController.silenceDb on the NetworkPlayer prefab
        private const float Tolerance = 0.0001f;

        // The rule MicrophoneVisionController.cs:284 used before the move (fixed thresholds).
        private static int Legacy(float dbfs, float silenceDb) => dbfs >= -15f ? 3 : dbfs >= -30f ? 2 : dbfs >= silenceDb ? 1 : 0;

        // noise frames at noiseDb, then voiced frames at voiceDb; 150 in total unless voiced exceeds it.
        private static List<float> Prompt(float noiseDb, float voiceDb, int voiced, int total = MicCalibration.FrameCount)
        {
            var frames = new List<float>(total);
            for (int i = 0; i < total - voiced; i++) frames.Add(noiseDb);
            for (int i = 0; i < voiced; i++) frames.Add(voiceDb);
            return frames;
        }

        [Test]
        public void Boundaries_MatchAppendixA()
        {
            Assert.AreEqual(-30f, PulseSpec.TalkDbfs, Tolerance, "Talk −30 dBFS");
            Assert.AreEqual(-15f, PulseSpec.ShoutDbfs, Tolerance, "Shout −15 dBFS");
            Assert.AreEqual(150, MicCalibration.FrameCount, "3 s / 20 ms");
            Assert.AreEqual(MicCalibration.FrameCount, (int)(MicCalibration.DurationSeconds * 1000 / MicCalibration.FrameMilliseconds));
        }

        [TestCase(-30.01f, 1)]
        [TestCase(-30f, 2)]
        [TestCase(-29.99f, 2)]
        [TestCase(-15.01f, 2)]
        [TestCase(-15f, 3)]
        [TestCase(-14.99f, 3)]
        public void Grade_Offset0_Boundaries(float dbfs, int expected) =>
            Assert.AreEqual(expected, MicCalibration.Grade(dbfs, 0f, Silence));

        [TestCase(6f, -36.01f, 1)]
        [TestCase(6f, -35.99f, 2)]
        [TestCase(6f, -21.01f, 2)]
        [TestCase(6f, -20.99f, 3)]
        [TestCase(-6f, -24.01f, 1)]
        [TestCase(-6f, -23.99f, 2)]
        [TestCase(-6f, -9.01f, 2)]
        [TestCase(-6f, -8.99f, 3)]
        public void Grade_OffsetMovesBoundaries(float offset, float dbfs, int expected) =>
            Assert.AreEqual(expected, MicCalibration.Grade(dbfs, offset, Silence));

        [TestCase(6f)]
        [TestCase(0f)]
        [TestCase(-6f)]
        public void Grade_SilenceFloorIgnoresOffset(float offset)
        {
            Assert.AreEqual(0, MicCalibration.Grade(-50.01f, offset, Silence), "below −50 is silence for any offset");
            Assert.AreEqual(1, MicCalibration.Grade(-49.99f, offset, Silence), "just above −50 is a whisper for any offset");
        }

        [TestCase(-34f, -6f)]
        [TestCase(-46f, 6f)]
        [TestCase(-40f, 0f)]
        [TestCase(-20f, -6f)]   // clamp: raw −20
        [TestCase(-70f, 6f)]    // clamp: raw +30
        public void Measure_OffsetSignAndClamp(float baseline, float expectedOffset)
        {
            var result = MicCalibration.Measure(Prompt(baseline - 30f, baseline, 30), 0);
            Assert.IsTrue(result.Valid, result.Reason.ToString());
            Assert.AreEqual(baseline, result.Baseline, Tolerance, "baseline = median of voiced frames");
            Assert.AreEqual(expectedOffset, result.Offset, Tolerance);
        }

        [Test]
        public void Measure_Invalid_TooFewVoicedFrames()
        {
            var r24 = MicCalibration.Measure(Prompt(-80f, -40f, 24), 0);
            Assert.IsFalse(r24.Valid);
            Assert.AreEqual(MicCalibration.FailReason.TooFewActive, r24.Reason);
            Assert.AreEqual(0f, r24.Offset, Tolerance, "an invalid prompt keeps offset 0");
            Assert.IsTrue(MicCalibration.Measure(Prompt(-80f, -40f, 25), 0).Valid, "25 voiced frames (0.5 s) is enough");
        }

        [Test]
        public void Measure_Invalid_Clipped()
        {
            var clipped = MicCalibration.Measure(Prompt(-80f, -40f, 60), 5);
            Assert.IsFalse(clipped.Valid);
            Assert.AreEqual(MicCalibration.FailReason.Clipped, clipped.Reason);
            Assert.IsTrue(MicCalibration.Measure(Prompt(-80f, -40f, 60), 4).Valid, "4 clipped frames is still valid");
        }

        [Test]
        public void Measure_Invalid_AllSilent()
        {
            var silent = MicCalibration.Measure(Prompt(-90f, -90f, 0), 0);
            Assert.IsFalse(silent.Valid);
            Assert.AreEqual(MicCalibration.FailReason.TooFewActive, silent.Reason);
            Assert.IsFalse(MicCalibration.Measure(new List<float>(), 0).Valid, "no frames");
            Assert.IsFalse(MicCalibration.Measure(null, 0).Valid, "null frames");
        }

        [TestCase(1)]
        [TestCase(2)]
        public void Measure_MedianIgnoresSpikes(int spikes)
        {
            var frames = Prompt(-80f, -40f, 30);
            for (int i = 0; i < spikes; i++) frames[frames.Count - 1 - i] = -5f;   // a cough, a bump
            var result = MicCalibration.Measure(frames, 0);
            Assert.IsTrue(result.Valid);
            Assert.AreEqual(-40f, result.Baseline, Tolerance);
            Assert.AreEqual(0f, result.Offset, Tolerance);
        }

        [Test]
        public void Measure_NoiseFloorIsTenthPercentile()
        {
            // 150 frames: −80 … −80 + 149 × 0.1. Nearest rank 10% = the 15th smallest (index 14).
            var frames = new List<float>();
            for (int i = 0; i < 150; i++) frames.Add(-80f + i * .1f);
            var result = MicCalibration.Measure(frames, 0);
            Assert.AreEqual(-80f + 14 * .1f, result.NoiseFloor, Tolerance);
        }

        [Test]
        public void Grade_Offset0_SameAsFixedThresholds_Sweep()
        {
            int points = 0;
            for (int i = 0; i <= 160; i++)
            {
                float dbfs = -80f + i * .5f;
                foreach (float d in new[] { dbfs, dbfs - .01f, dbfs + .01f })
                {
                    Assert.AreEqual(Legacy(d, Silence), MicCalibration.Grade(d, 0f, Silence), "dBFS " + d);
                    points++;
                }
            }
            Assert.AreEqual(483, points);
        }
    }
}
