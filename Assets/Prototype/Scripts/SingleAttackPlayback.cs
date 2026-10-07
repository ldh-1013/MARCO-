using System;

namespace Marco.Prototype
{
    // Independent of Unity: wall-clock attack lifetime is not the full imported clip length.
    public sealed class SingleAttackPlayback
    {
        private double startedAt;
        private double duration;
        private double sourceDuration;
        public bool IsPlaying { get; private set; }

        public void Begin(double now, double motionDuration, double firstPunchDuration)
        {
            startedAt = now;
            duration = Math.Max(0.01, motionDuration);
            sourceDuration = Math.Max(0.01, firstPunchDuration);
            IsPlaying = true;
        }

        public double Sample(double now)
        {
            if (!IsPlaying) return 0;
            double elapsed = Math.Max(0, now - startedAt);
            if (elapsed >= duration)
            {
                IsPlaying = false;
                return 0;
            }
            return sourceDuration * elapsed / duration;
        }
    }
}
