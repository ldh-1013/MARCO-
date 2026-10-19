using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Roles
{
    // Server-only role policy: the host's own player is the Seeker, everyone else a Runner.
    // Clients never decide roles; they only read NetworkPlayer.Role through its SyncVar.
    [RequireComponent(typeof(PlayerSpawner))]
    public sealed class RoleAssigner : MonoBehaviour
    {
        private NetworkManager networkManager;
        private PlayerSpawner spawner;

        private void Awake()
        {
            networkManager = GetComponentInParent<NetworkManager>();
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
            player.SetRole(IsHostConnection(nob.OwnerId) ? PlayerRole.Seeker : PlayerRole.Runner);
        }

        // Compare ids rather than connection objects: host server/client sides may hold separate instances.
        private bool IsHostConnection(int clientId) =>
            networkManager.IsClientStarted && networkManager.ClientManager.Connection.ClientId == clientId;
    }
}
