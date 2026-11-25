namespace Marco.Network.Voice
{
    // 부록 A [마이크 캘리브레이션] 프롬프트 — pure state machine, no scene.
    // Failed measurements can be retried until the player succeeds or skips.
    public sealed class MicCalibrationPrompt
    {
        public enum State { Idle, Measuring, Success, Retry, Skipped, Cancelled }
        public enum Failure { None, TooQuiet, Clipped, Muted }

        public State Current { get; private set; } = State.Idle;
        public Failure LastFailure { get; private set; }
        public float Offset { get; private set; }

        public bool CanStart => Current == State.Idle || Current == State.Retry || Current == State.Cancelled;
        public bool IsFinished => Current == State.Success || Current == State.Skipped;
        public bool ShouldSave => Current == State.Success;

        public bool Start()
        {
            if (!CanStart) return false;
            Current = State.Measuring;
            return true;
        }

        // One finished 3 s measurement. muted: Left Alt was held during it (no frames reach the measurement then).
        public State Report(MicCalibration.Result result, bool muted)
        {
            if (Current != State.Measuring) return Current;
            if (result.Valid)
            {
                Offset = result.Offset;
                LastFailure = Failure.None;
                return Current = State.Success;
            }
            Offset = 0f;
            LastFailure = muted ? Failure.Muted : result.Reason == MicCalibration.FailReason.Clipped ? Failure.Clipped : Failure.TooQuiet;
            return Current = State.Retry;
        }

        // Cut short: the attempt is not counted.
        public void Cancel()
        {
            if (Current == State.Measuring) Current = State.Cancelled;
        }

        public void Skip()
        {
            if (IsFinished) return;
            Offset = 0f;
            Current = State.Skipped;
        }

        // 로비 "마이크 다시 측정": start a fresh measurement.
        public void Restart()
        {
            Current = State.Idle;
            LastFailure = Failure.None;
            Offset = 0f;
        }
    }
}
