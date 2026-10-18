using System.Collections.Generic;
using Marco.Core.WinCondition;

namespace Marco.Core.WinCondition.Tests
{
    // IPlayerRoster 가짜 구현. 기본값은 "등록 안 하면 살아있는 Runner"로 둬서 테스트에서 예외 케이스만 설정하면 됨.
    public sealed class FakePlayerRoster : IPlayerRoster
    {
        private readonly Dictionary<int, PlayerRole> _roles = new Dictionary<int, PlayerRole>();
        private readonly Dictionary<int, bool> _aliveOverrides = new Dictionary<int, bool>();
        private readonly Dictionary<int, bool> _escapedOverrides = new Dictionary<int, bool>();

        public int AliveRunnerCount { get; set; }
        public int TotalRunnerCount { get; set; }

        public void SetRole(int playerId, PlayerRole role) => _roles[playerId] = role;
        public void SetAlive(int playerId, bool alive) => _aliveOverrides[playerId] = alive;
        public void SetEscaped(int playerId, bool escaped) => _escapedOverrides[playerId] = escaped;

        public PlayerRole GetRole(int playerId) =>
            _roles.TryGetValue(playerId, out var role) ? role : PlayerRole.Runner;

        public bool IsAlive(int playerId) =>
            !_aliveOverrides.TryGetValue(playerId, out var alive) || alive;

        public bool HasEscaped(int playerId) =>
            _escapedOverrides.TryGetValue(playerId, out var escaped) && escaped;
    }
}
