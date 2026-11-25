namespace Marco.Network.Voice
{
    // 부록 A [마이크 캘리브레이션] 프롬프트 — pure state machine, no scene.
    //   Idle → Measuring → Success | Retry(failure) | Fallback ; Skip → Skipped ; Cancel (while measuring) → Cancelled
    // At most MaxAttempts measured attempts. A prompt cut short (controller disabled, scene change, round start) is
    // Cancelled and does not use an attempt. Three failed attempts: Fallback — offset 0, not saved. Skipped: offset 0
    // for this session, not saved. Success: the offset is saved.
    public sealed class MicCalibrationPrompt
    {
        public enum State { Idle, Measuring, Success, Retry, Fallback, Skipped, Cancelled }
        public enum Failure { None, TooQuiet, Clipped, Muted }

        public State Current { get; private set; } = State.Idle;
        public int Attempts { get; private set; }
        public Failure LastFailure { get; private set; }
        public float Offset { get; private set; }

        public bool CanStart => Current == State.Idle || Current == State.Retry || Current == State.Cancelled;
        public bool IsFinished => Current == State.Success || Current == State.Fallback || Current == State.Skipped;
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
            Attempts++;
            if (result.Valid)
            {
                Offset = result.Offset;
                LastFailure = Failure.None;
                return Current = State.Success;
            }
            Offset = 0f;
            LastFailure = muted ? Failure.Muted : result.Reason == MicCalibration.FailReason.Clipped ? Failure.Clipped : Failure.TooQuiet;
            return Current = Attempts >= MicCalibration.MaxAttempts ? State.Fallback : State.Retry;
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

        // 로비 "마이크 다시 측정": a fresh prompt with all attempts back.
        public void Restart()
        {
            Current = State.Idle;
            Attempts = 0;
            LastFailure = Failure.None;
            Offset = 0f;
        }
    }
}
