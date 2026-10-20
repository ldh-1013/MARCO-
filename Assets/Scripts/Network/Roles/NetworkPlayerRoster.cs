using Marco.Core.WinCondition;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Roles
{
    // IPlayerRoster backed by the spawned NetworkPlayers. playerId = FishNet ClientId.
    // Alive = still in the round: not escaped, not tagged, not a late-joining spectator.
    public sealed class NetworkPlayerRoster : MonoBehaviour, IPlayerRoster
    {
        // Unknown ids (not spawned / already left) answer Echo so valve and drain work rejects them.
        public PlayerRole GetRole(int playerId) =>
            NetworkPlayer.TryGet(playerId, out var player) ? player.Role : PlayerRole.Echo;

        public bool IsAlive(int playerId) => HasStatus(playerId, PlayerStatus.Alive);
        public bool HasEscaped(int playerId) => HasStatus(playerId, PlayerStatus.Escaped);

        public int AliveRunnerCount => CountRunners(aliveOnly: true);

        // RoundState reads this once at construction, so build it after roles are assigned.
        public int TotalRunnerCount => CountRunners(aliveOnly: false);

        private static bool HasStatus(int playerId, PlayerStatus status) =>
            NetworkPlayer.TryGet(playerId, out var player) && player.Status == status;

        private static int CountRunners(bool aliveOnly)
        {
            int count = 0;
            foreach (var player in NetworkPlayer.All)
                if (player.Role == PlayerRole.Runner && (!aliveOnly || player.Status == PlayerStatus.Alive)) count++;
            return count;
        }
    }
}
