namespace Marco.Core.WinCondition
{
    public enum RoundResult
    {
        InProgress,
        RunnersWin,
        SeekersWin,

        // v0.6: 술래가 라운드 중 나가면 승패 없이 판 자체가 무효로 끝난다.
        Void,
    }
}
