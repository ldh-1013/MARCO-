using Marco.Network.Voice;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 부록 A [마이크 캘리브레이션] — 측정 세션(150프레임 / 3초)과 클리핑 프레임 기준(|샘플| ≥ 0.99가 3개 이상).
    public class MicCalibrationSessionTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void ClipConstants_MatchAppendixA()
        {
            Assert.AreEqual(.99f, MicCalibration.ClipSampleLevel, Tolerance);
            Assert.AreEqual(3, MicCalibration.ClipSamplesPerFrame);
        }

        [TestCase(.989f, 3, false)]
        [TestCase(.99f, 3, true)]
        [TestCase(-.99f, 3, true)]
        [TestCase(.99f, 2, false)]
        [TestCase(1f, 3, true)]
        public void ClippedFrame_LevelAndCount(float level, int loudSamples, bool expected)
        {
            var frame = new float[960];
            for (int i = 0; i < loudSamples; i++) frame[i * 100] = level;
            Assert.AreEqual(expected, MicCalibration.IsClippedFrame(frame, frame.Length));
        }

        [Test]
        public void ClippedFrame_CountsOnlyTheFrame()
        {
            var buffer = new float[10];
            buffer[7] = buffer[8] = buffer[9] = 1f;   // beyond the frame length 5
            Assert.IsFalse(MicCalibration.IsClippedFrame(buffer, 5));
            Assert.IsFalse(MicCalibration.IsClippedFrame(null, 5));
        }

        [Test]
        public void Session_CompletesAt150Frames()
        {
            var session = new MicCalibrationSession(0f);
            for (int i = 0; i < 149; i++) session.Add(i < 120 ? -80f : -40f, false);
            Assert.IsFalse(session.IsDue(.5f), "149 frames, 0.5 s");
            session.Add(-40f, false);
            Assert.IsTrue(session.IsFull);
            Assert.IsTrue(session.IsDue(.5f), "150 frames ends the prompt before 3 s");
            var result = session.Complete();
            Assert.IsTrue(result.Valid, result.Reason.ToString());
            Assert.AreEqual(-40f, result.Baseline, Tolerance);
        }

        [Test]
        public void Session_CompletesAt3Seconds()
        {
            var session = new MicCalibrationSession(10f);
            session.Add(-40f, false);
            Assert.IsFalse(session.IsDue(12.99f));
            Assert.IsTrue(session.IsDue(13f), "3 s after the start");
            Assert.IsFalse(session.Complete().Valid, "1 frame is too few");
        }

        [Test]
        public void Session_IgnoresFramesPast150()
        {
            var session = new MicCalibrationSession(0f);
            for (int i = 0; i < 200; i++) session.Add(-40f, true);
            Assert.AreEqual(150, session.FrameCount);
            Assert.AreEqual(150, session.ClippedFrames, "clipped frames past 150 are ignored too");
        }

        [Test]
        public void Session_CountsClippedFrames()
        {
            var session = new MicCalibrationSession(0f);
            for (int i = 0; i < 150; i++) session.Add(i < 100 ? -80f : -40f, i < 4);
            Assert.AreEqual(4, session.ClippedFrames);
            Assert.IsTrue(session.Complete().Valid, "4 clipped frames");
            session.Reset(0f);
            for (int i = 0; i < 150; i++) session.Add(i < 100 ? -80f : -40f, i < 5);
            Assert.AreEqual(MicCalibration.FailReason.Clipped, session.Complete().Reason, "5 clipped frames");
        }

        [Test]
        public void Session_Progress()
        {
            var session = new MicCalibrationSession(0f);
            Assert.AreEqual(0f, session.Progress(0f), 0.0001f);
            Assert.AreEqual(.5f, session.Progress(1.5f), 0.0001f, "time");
            for (int i = 0; i < 120; i++) session.Add(-40f, false);
            Assert.AreEqual(.8f, session.Progress(1.5f), 0.0001f, "frames ahead of time");
            Assert.AreEqual(1f, session.Progress(9f), 0.0001f, "capped");
        }

        [Test]
        public void Session_Reset()
        {
            var session = new MicCalibrationSession(0f);
            for (int i = 0; i < 150; i++) session.Add(-40f, true);
            session.Reset(5f);
            Assert.AreEqual(0, session.FrameCount);
            Assert.AreEqual(0, session.ClippedFrames);
            Assert.IsFalse(session.IsFull);
            Assert.IsFalse(session.IsDue(7.9f), "the 3 s restart from the reset time");
            Assert.IsTrue(session.IsDue(8f));
        }
    }
}
