namespace Marco.Network.Voice
{
    // Voice re-emission (기획서 §5.2 ⑨), shared by the speaker's client (own ring) and the server (rings for the
    // other players) so both follow one rule:
    //   - a new pulse when the previous pulse's duration has ended, or
    //   - when the grade changed and the new grade has held for PulseSpec.VoiceGradeHold (§5.2 ⑦ 0.15s);
    //   - never two pulses of one speaker closer than PulseSpec.VoiceMinInterval.
    // A loudness flickering across a grade boundary therefore cannot fire a pulse per frame.
    // Pure logic: the caller passes the time.
    public sealed class VoicePulseGate
    {
        private VoiceGrade lastGrade, candidate;
        private bool hasCandidate, hasPulsed;
        private float candidateSince, nextPulseTime, lastPulseTime = float.NegativeInfinity;
        public int Pulses { get; private set; }

        public bool ShouldPulse(VoiceGrade grade, float now, float duration)
        {
            if (!hasCandidate || grade != candidate)
            {
                candidate = grade;
                candidateSince = now;
                hasCandidate = true;
            }
            bool expired = !hasPulsed || now >= nextPulseTime;
            bool changed = hasPulsed && grade != lastGrade && now - candidateSince >= Round.PulseSpec.VoiceGradeHold;
            if (!expired && !changed) return false;
            if (now - lastPulseTime < Round.PulseSpec.VoiceMinInterval) return false;
            lastGrade = grade;
            lastPulseTime = now;
            nextPulseTime = now + duration;
            hasPulsed = true;
            Pulses++;
            return true;
        }
    }
}
