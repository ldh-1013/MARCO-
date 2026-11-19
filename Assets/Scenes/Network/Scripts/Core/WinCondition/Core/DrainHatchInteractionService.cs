namespace Marco.Core.WinCondition
{
    // 배수구 작업도 ValveInteractionService와 같은 모양의 서버 호출 입구.
    // 역할은 인자로 받지 않고 IPlayerRoster에서 직접 확인하며, 살아있는 Runner만 허용한다.
    public sealed class DrainHatchInteractionService
    {
        private readonly LastSurvivorPhase _phase;
        private readonly IPlayerRoster _playerRoster;

        public DrainHatchInteractionService(LastSurvivorPhase phase, IPlayerRoster playerRoster)
        {
            _phase = phase;
            _playerRoster = playerRoster;
        }

        // 지금 활성화된 배수구가 아니거나, 살아있는 Runner가 아니면 거부한다.
        public bool TryBeginWork(int playerId, DrainHatchId drainHatchId)
        {
            if (_phase.ActiveDrainHatchId != drainHatchId) return false;
            if (!IsAliveRunner(playerId)) return false;

            _phase.GetDrainHatch(drainHatchId).BeginWork(playerId);
            return true;
        }

        public void EndWork(int playerId, DrainHatchId drainHatchId)
        {
            if (_phase.ActiveDrainHatchId != drainHatchId) return;

            _phase.GetDrainHatch(drainHatchId).EndWork(playerId);
        }

        private bool IsAliveRunner(int playerId)
        {
            return _playerRoster.GetRole(playerId) == PlayerRole.Runner && _playerRoster.IsAlive(playerId);
        }
    }
}
