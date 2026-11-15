using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // MicrophoneVisionController's calibration offset (부록 A [마이크 캘리브레이션]). AddComponent in EditMode does not
    // run Start, so no microphone is opened; only the offset and the grade it gives are checked here.
    public class MicVisionCalibrationTests
    {
        private GameObject go;
        private MicrophoneVisionController mic;

        [SetUp]
        public void SetUp()
        {
            go = new GameObject("mic calibration probe");
            mic = go.AddComponent<MicrophoneVisionController>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(go);

        private static int Legacy(float dbfs) => dbfs >= -15f ? 3 : dbfs >= -30f ? 2 : dbfs >= -50f ? 1 : 0;

        [TestCase(0f, 0f)]
        [TestCase(10f, 6f)]
        [TestCase(-10f, -6f)]
        [TestCase(6f, 6f)]
        [TestCase(-3.5f, -3.5f)]
        [TestCase(float.NaN, 0f)]
        public void SetCalibrationOffset_ClampsToSix(float requested, float expected)
        {
            mic.SetCalibrationOffset(requested);
            Assert.AreEqual(expected, mic.CalibrationOffsetDb, 0.0001f);
        }

        [Test]
        public void DefaultOffsetIsZero_AndGradeMatchesFixedThresholds()
        {
            Assert.AreEqual(0f, mic.CalibrationOffsetDb);
            for (int i = 0; i <= 160; i++)
            {
                float dbfs = -80f + i * .5f;
                Assert.AreEqual(Legacy(dbfs), mic.GradeLevel(dbfs), "dBFS " + dbfs);
            }
        }

        [Test]
        public void Offset_MovesOnlyTheGradeBoundaries()
        {
            mic.SetCalibrationOffset(6f);   // a quiet microphone: −36 already counts as Talk
            Assert.AreEqual(2, mic.GradeLevel(-35.99f));
            Assert.AreEqual(3, mic.GradeLevel(-20.99f));
            Assert.AreEqual(0, mic.GradeLevel(-50.01f), "the −50 voice floor is not moved");
            mic.SetCalibrationOffset(-6f);  // a hot microphone: −25 is still a whisper
            Assert.AreEqual(1, mic.GradeLevel(-24.01f));
            Assert.AreEqual(1, mic.GradeLevel(-49.99f), "the −50 voice floor is not moved");
        }

        [Test]
        public void BeginAndCancelCalibration()
        {
            Assert.IsFalse(mic.IsCalibrating);
            bool called = false;
            mic.BeginCalibration(_ => called = true);
            Assert.IsTrue(mic.IsCalibrating);
            mic.CancelCalibration();
            Assert.IsFalse(mic.IsCalibrating);
            Assert.IsFalse(called, "cancel does not report");
        }
    }
}
