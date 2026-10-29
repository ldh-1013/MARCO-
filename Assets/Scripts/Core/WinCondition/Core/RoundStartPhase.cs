namespace Marco.Core.WinCondition
{
    // 라운드 시작 순서(부록 A [라운드 시작], 10-26).
    // RoleReveal: 역할 확인, 전원 이동 잠금 / SeekerHold: InGame 시작, 술래만 격리 / Released: 전원 이동 가능
    public enum RoundStartPhase : byte
    {
        RoleReveal,
        SeekerHold,
        Released,
    }
}
