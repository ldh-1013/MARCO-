namespace Marco.Network.Round
{
    // Pulse judgement numbers used by the server ripple additions, copied from the design document
    // (기획서 부록 A) with their section. One place: server code reads these instead of literals.
    public static class PulseSpec
    {
        // 부록 A [소리 등급] Shout 22m · 2.5s — reused by tag, gate, last survivor, exit pulses (§3.1-1 · §10.5 · §6.5-1 · §6.2-1).
        public const float Shout = 22f, ShoutSeconds = 2.5f;
        // 부록 A [소리 등급] Valve 12m; valve A sits in machine ambience × 0.5 → 6m (§6.1-0).
        public const float Valve = 12f, ValveAMachine = 6f;
        // §6.5-3 drain hatch work: forced pulse like an underwater valve, 12m every 2.5s.
        public const float DrainWork = 12f, DrainWorkPeriod = 2.5f;
        // 부록 A [정지 감지]: breath 3m after 20s still, +2m every 15s, at most 9m, 0.6s.
        public const float BreathStart = 3f, BreathStep = 2f, BreathMax = 9f, BreathSeconds = .6f;
        public static float BreathRadius(int breathsSoFar) =>
            breathsSoFar < 0 ? BreathStart : System.Math.Min(BreathMax, BreathStart + BreathStep * breathsSoFar);
        // §5.2 ⑦ debounce reused for ⑨ re-emission: a changed grade counts after 0.15s; pulses of one
        // speaker are at least 0.15s apart.
        public const float VoiceGradeHold = .15f, VoiceMinInterval = .15f;
        // D9: ring-only pulses send this instead of a clip name; RoundAudioPlayback plays nothing for it.
        public const string NoAudio = "";
    }
}
