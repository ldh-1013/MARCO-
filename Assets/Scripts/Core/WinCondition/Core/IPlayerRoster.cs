namespace Marco.Core.WinCondition
{
    // 재운(UI/UX·이동·플레이어 정보) 파트가 구현. 여기서는 역할/생존/탈출 여부만 조회함.
    public interface IPlayerRoster
    {
        PlayerRole GetRole(int playerId);
        bool IsAlive(int playerId);
        bool HasEscaped(int playerId);

        // 현재 살아있는 Runner 수 (최후 생존자 페이즈 진입 판정 등에 쓰일 예정)
        int AliveRunnerCount { get; }

        // 라운드 시작 시의 전체 Runner 수 (탈출 요구 인원 계산 등에 쓰일 예정)
        int TotalRunnerCount { get; }
    }
}
