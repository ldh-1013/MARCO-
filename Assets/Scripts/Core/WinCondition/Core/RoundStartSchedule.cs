namespace Marco.Core.WinCondition
{
    // 역할 확인 시작부터의 경과 시간으로 라운드 시작 단계를 계산한다(부록 A [라운드 시작] · 15.4, 10-26).
    // 0 ~ RoleReveal: 역할 확인(전원 잠금) → RoleReveal: InGame 시작(도망자 출발, 제한시간 · 역류 · 최후 생존자 판정 시작)
    // → RoleReveal + SeekerHold: 술래 격리 해제.
    public sealed class RoundStartSchedule
    {
        private readonly float _roleRevealSeconds;
        private readonly float _seekerReleaseSeconds;

        public float ElapsedSeconds { get; private set; }

        public RoundStartSchedule(WinConditionConfig config)
        {
            _roleRevealSeconds = config.RoleRevealSeconds;
            _seekerReleaseSeconds = config.RoleRevealSeconds + config.SeekerHoldSeconds;
        }

        public RoundStartPhase Phase =>
            ElapsedSeconds < _roleRevealSeconds ? RoundStartPhase.RoleReveal
            : ElapsedSeconds < _seekerReleaseSeconds ? RoundStartPhase.SeekerHold
            : RoundStartPhase.Released;

        // InGame 시작 = 도망자 출발 시점.
        public bool HasInGameStarted => Phase != RoundStartPhase.RoleReveal;
        public bool IsSeekerReleased => Phase == RoundStartPhase.Released;

        // 지금 단계가 끝날 때까지 남은 시간. Released면 0.
        public float PhaseRemainingSeconds => Phase switch
        {
            RoundStartPhase.RoleReveal => _roleRevealSeconds - ElapsedSeconds,
            RoundStartPhase.SeekerHold => _seekerReleaseSeconds - ElapsedSeconds,
            _ => 0f,
        };

        // 시간을 흘리고, 그 중 InGame에 들어간 몫을 돌려준다(라운드 · 밸브 Tick에 넘길 dt).
        // 한 틱이 InGame 시작 경계를 넘으면 넘은 만큼만 돌려준다.
        public float Tick(float dt)
        {
            if (dt <= 0f) return 0f;

            float before = ElapsedSeconds;
            ElapsedSeconds += dt;
            if (ElapsedSeconds < _roleRevealSeconds) return 0f;
            return before >= _roleRevealSeconds ? dt : ElapsedSeconds - _roleRevealSeconds;
        }

        // 새 판(다시 시작 포함)은 역할 확인 0초부터 다시 시작한다.
        public void Reset() => ElapsedSeconds = 0f;

        // 라운드 밖(로비)에서는 잠그지 않는다 — 판단은 호출하는 쪽에서 라운드 진행 여부와 함께 한다.
        public static bool IsMovementLocked(RoundStartPhase phase, PlayerRole role) => phase switch
        {
            RoundStartPhase.RoleReveal => true,
            RoundStartPhase.SeekerHold => role == PlayerRole.Seeker,
            _ => false,
        };
    }
}
