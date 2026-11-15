using System.Collections.Generic;

namespace Marco.Network.Voice
{
    // One 3 s calibration prompt (부록 A [마이크 캘리브레이션] "측정 3초 = 20ms 프레임 150개") — pure, no scene.
    // The microphone controller feeds every processed frame; the prompt ends at 150 frames or 3 s, whichever first.
    public sealed class MicCalibrationSession
    {
        private readonly List<float> frames = new(MicCalibration.FrameCount);
        private float startedAt;

        public MicCalibrationSession(float now) => startedAt = now;

        public int FrameCount => frames.Count;
        public int ClippedFrames { get; private set; }
        public bool IsFull => frames.Count >= MicCalibration.FrameCount;

        // Frames past the 150th are ignored (the prompt is already full).
        public void Add(float dbfs, bool clipped)
        {
            if (IsFull) return;
            frames.Add(dbfs);
            if (clipped) ClippedFrames++;
        }

        public bool IsDue(float now) => IsFull || now - startedAt >= MicCalibration.DurationSeconds;

        public MicCalibration.Result Complete() => MicCalibration.Measure(frames, ClippedFrames);

        public void Reset(float now)
        {
            frames.Clear();
            ClippedFrames = 0;
            startedAt = now;
        }
    }
}
