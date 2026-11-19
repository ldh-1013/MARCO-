using FishNet.Component.Spawning;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Roles
{
    // Server-only role policy: NetworkRound draws one connected player uniformly as Seeker once per
    // round start; every other player is a Runner. Until then everyone waits as a Runner.
    // Clients only read the synced role.
    [RequireComponent(typeof(PlayerSpawner))]
    public sealed class RoleAssigner : MonoBehaviour
    {
        private PlayerSpawner spawner;

        private void Awake()
        {
            spawner = GetComponent<PlayerSpawner>();
            spawner.OnSpawned += OnPlayerSpawned;
        }

        private void OnDestroy()
        {
            if (spawner != null) spawner.OnSpawned -= OnPlayerSpawned;
        }

        // PlayerSpawner raises this on the server right after spawning a connection's player.
        private void OnPlayerSpawned(NetworkObject nob)
        {
            if (!nob.TryGetComponent(out NetworkPlayer player)) return;
            player.SetRole(PlayerRole.Runner);
            // RoundState fixed its Runner count at start, so late joiners wait for the next round.
            var round = NetworkRound.Instance;
            bool started = round != null && round.Mode != RoundMode.NotStarted;
            player.SetStatus(started ? PlayerStatus.Spectating : PlayerStatus.Alive);
        }

        // Called by NetworkRound.StartRound before RoundState reads the Runner count.
        public void AssignRoles()
        {
            var players = NetworkPlayer.All;
            if (players.Count == 0) return;

            int seekerIndex = UnityEngine.Random.Range(0, players.Count);
            for (int i = 0; i < players.Count; i++)
                players[i].SetRole(i == seekerIndex ? PlayerRole.Seeker : PlayerRole.Runner);
        }
    }
}
