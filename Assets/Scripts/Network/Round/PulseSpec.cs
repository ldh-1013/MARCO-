namespace Marco.Network.Round
{
    // Pulse judgement numbers used by the server ripple additions, copied from the design document
    // (기획서 부록 A) with their section. One place: server code reads these instead of literals.
    public static class PulseSpec
    {
        /// <summary>부록 A [소리 등급] Whisper 속삭임 4m · 0.6s (§5.1). The voice pulse and voice routing read these.</summary>
        public const float Whisper = 4f, WhisperSeconds = .6f;
        /// <summary>부록 A [소리 등급] Talk 대화 9m · 1.2s (§5.1).</summary>
        public const float Talk = 9f, TalkSeconds = 1.2f;
        // 부록 A [소리 등급] Shout 22m · 2.5s — reused by tag, gate, last survivor, exit pulses (§3.1-1 · §10.5 · §6.5-1 · §6.2-1).
        public const float Shout = 22f, ShoutSeconds = 2.5f;
        /// <summary>부록 A [소리 등급] Scream 비명 9m · 1.0s — the Seeker's shout failed to be suppressed (§3.5).</summary>
        public const float Scream = 9f, ScreamSeconds = 1f;
        /// <summary>부록 A [외침] 공포 반경 22m — ability judgement, no occlusion (§3.5).</summary>
        public const float FearRadius = 22f;
        /// <summary>부록 A [소리 등급] 술래 청취 반경 ×1.2 (§5.7).</summary>
        public const float SeekerHearing = 1.2f;
        /// <summary>부록 A [숨 게이지] 질식 페널티 "고함급(22m) 파문 1회" — 지속은 고함 등급 정의를 따른다 (11-15).</summary>
        public const float Drowning = Shout, DrowningSeconds = ShoutSeconds;
        // 부록 A [소리 등급] Valve 12m; valve A sits in machine ambience × 0.5 → 6m (§6.1-0).
        public const float Valve = 12f, ValveAMachine = 6f;
        // §6.5-3 drain hatch work: forced pulse like an underwater valve, 12m every 2.5s.
        public const float DrainWork = 12f, DrainWorkPeriod = 2.5f;
        // 부록 A [정지 감지]: breath 3m after 20s still, +2m every 15s, at most 9m, 0.6s. The radii are defined once in
        // Core (AwardRules — the awards count the same breaths) and read here.
        public const float BreathStart = Marco.Core.WinCondition.AwardRules.BreathBaseRadius,
            BreathStep = Marco.Core.WinCondition.AwardRules.BreathRadiusStep,
            BreathMax = Marco.Core.WinCondition.AwardRules.BreathMaxRadius, BreathSeconds = .6f;
        public static float BreathRadius(int breathsSoFar) =>
            breathsSoFar < 0 ? BreathStart : System.Math.Min(BreathMax, BreathStart + BreathStep * breathsSoFar);
        // §5.2 ⑦ debounce reused for ⑨ re-emission: a changed grade counts after 0.15s; pulses of one
        // speaker are at least 0.25s apart (at most 4 per second per listener — server delivery rule).
        public const float VoiceGradeHold = .15f, VoiceMinInterval = .25f;
        // D9: ring-only pulses send this instead of a clip name; RoundAudioPlayback plays nothing for it.
        public const string NoAudio = "";
    }
}
