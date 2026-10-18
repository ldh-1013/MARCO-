namespace Marco.Core.WinCondition
{
    // 승민(서버/네트워크) + 재운(UI) 쪽이 구현해서 받는 알림.
    public interface IRoundEventSink
    {
        void OnValveStatusChanged(ValveId valveId, ValveStatus status);

        // 지금은 상태가 바뀔 때 같이 전달됨(매 틱 연속 갱신은 아님). 필요해지면 추후 단계에서 보강.
        void OnValveProgressChanged(ValveId valveId, float progress);

        // 게이트가 열리는 순간 딱 한 번만 호출됨 (latch)
        void OnGateOpened();

        // 라운드 결과가 확정되는 순간 딱 한 번만 호출됨
        void OnRoundEnded(RoundResult result);

        // 살아있는 Runner가 1명이 되는 순간 딱 한 번만 호출됨
        void OnLastSurvivorPhaseEntered();

        // 페이즈 진입 시 게이트가 안 열려있어서 배수구가 활성화된 경우에만 호출됨(게이트가 열려있으면 안 불림)
        void OnDrainHatchActivated(DrainHatchId drainHatchId);
    }
}
