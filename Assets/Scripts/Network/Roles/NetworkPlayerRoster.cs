using Marco.Core.WinCondition;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Roles
{
    // IPlayerRoster backed by the spawned NetworkPlayers. playerId = FishNet ClientId.
    // Alive/escaped state does not exist yet: everyone is alive and nobody has escaped.
    public sealed class NetworkPlayerRoster : MonoBehaviour, IPlayerRoster
    {
        // Unknown ids (not spawned / already left) answer Echo so valve and drain work rejects them.
        public PlayerRole GetRole(int playerId) =>
            TryFind(playerId, out var player) ? player.Role : PlayerRole.Echo;

        // TODO: back with real alive/escaped state once tagging and escape exist.
        public bool IsAlive(int playerId) => TryFind(playerId, out _);
        public bool HasEscaped(int playerId) => false;

        public int AliveRunnerCount => CountRunners();

        // RoundState reads this once at construction, so build it after roles are assigned.
        public int TotalRunnerCount => CountRunners();

        private static bool TryFind(int playerId, out NetworkPlayer result)
        {
            foreach (var player in NetworkPlayer.All)
            {
                if (player.PlayerId != playerId) continue;
                result = player;
                return true;
            }
            result = null;
            return false;
        }

        private static int CountRunners()
        {
            int count = 0;
            foreach (var player in NetworkPlayer.All)
                if (player.Role == PlayerRole.Runner) count++;
            return count;
        }
    }
}
