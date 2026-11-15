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
        /// <summary>부록 A [소리 등급] Walk 걷기 2m · 0.4s / Sprint 질주 6m · 0.8s — before the floor multiplier (§5.1-1 · §5.9).</summary>
        public const float Walk = 2f, WalkSeconds = .4f, Sprint = 6f, SprintSeconds = .8f;
        /// <summary>부록 A [소리 등급] · [신음 Moan] 신음 9m · 1.2s (§5.1-2).</summary>
        public const float Moan = 9f, MoanSeconds = 1.2f;
        /// <summary>부록 A [신음 Moan] "주기 6초 / 첫 신음 피격 6초 후" (§5.1-2).</summary>
        public const float MoanPeriod = 6f;
        /// <summary>부록 A [소리 등급] · [붕대] Bandage 6m · 1.2s, every 2s (§7).</summary>
        public const float Bandage = 6f, BandageSeconds = 1.2f, BandagePeriod = 2f;
        /// <summary>부록 A [소리 등급] · [동료 치료] Treat 9m · 1.2s, every 2.25s (§3.3-2).</summary>
        public const float Treat = 9f, TreatSeconds = 1.2f, TreatPeriod = 2.25f;
        /// <summary>
        /// Playback volume of the 신음 · 노크 · 호흡 · 붕대 · 치료 그룹 (moan · knock · breath · bandage · treat). 구현 시작값 — [GAP]: 기획서에 재생 볼륨 수치가 없다
        /// (부록 A 미등록, 플레이 테스트로 조정).
        /// </summary>
        public const float GroupVolume = .65f;
        /// <summary>부록 A [외침] 공포 반경 22m — ability judgement, no occlusion (§3.5).</summary>
        public const float FearRadius = 22f;
        /// <summary>부록 A [소리 등급] 술래 청취 반경 ×1.2 (§5.7).</summary>
        public const float SeekerHearing = 1.2f;
        /// <summary>부록 A [역할별 인지 배율] 술래 "반경 ×1.2, 지속 ×1.5 (서버 판정 단계에 적용되는 기능적 수치)" — 지속 쪽 (§5.7).</summary>
        public const float SeekerDuration = 1.5f;
        /// <summary>부록 A [메아리] "소나: 맵 전체 파문 관측, 잔류 3초 감쇠" (§3.2-1) — 메아리 몸 청취자의 링 지속(역할 배율 대신).</summary>
        public const float EchoSonarSeconds = 3f;
        /// <summary>부록 A [사운드 차폐] "일반 벽 통과당 반경 배율 ×0.5 (중첩 가능)" · "수면(물 표면) ×0.5" (§5.6). 층간 바닥 ×0.25는 벽 2장으로 센다.</summary>
        public const float WallOcclusion = .5f, WaterOcclusion = .5f;
        /// <summary>부록 A [사운드 차폐] "차폐 시 볼륨 ×0.5 고정(벽 수와 무관)" (§5.6).</summary>
        public const float OccludedVolume = .5f;
        /// <summary>부록 A [숨 게이지] 질식 페널티 "고함급(22m) 파문 1회" — 지속은 고함 등급 정의를 따른다 (11-15).</summary>
        public const float Drowning = Shout, DrowningSeconds = ShoutSeconds;
        // 부록 A [소리 등급] Valve 12m; valve A sits in machine ambience × 0.5 → 6m (§6.1-0).
        public const float Valve = 12f, ValveAMachine = 6f;
        /// <summary>
        /// §5.1 밸브 행 "수중 · 배수구 강제 파문은 2.5초(주기와 같다)" · 부록 A [수중 강제 파문] "수중 밸브·배수구 작업 중 2.5초 주기, 12m 등급".
        /// One number for both: the period between forced pulses and each pulse's duration.
        /// </summary>
        public const float UnderwaterForcedSeconds = 2.5f;
        // §6.5-3 drain hatch work: forced pulse like an underwater valve, 12m every 2.5s — the same rule, so the same constant.
        public const float DrainWork = 12f, DrainWorkPeriod = UnderwaterForcedSeconds;
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
