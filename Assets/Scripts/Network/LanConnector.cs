using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace Marco.Network
{
    public enum LanConnectionStatus { Offline, Connecting, Connected, Failed }

    // LAN(IP) Host/Join over FishNet's default Tugboat transport (UDP 7770).
    // Steam transport will replace Tugboat later; callers only use Host/Join/Leave.
    [RequireComponent(typeof(NetworkManager))]
    public sealed class LanConnector : MonoBehaviour
    {
        private NetworkManager networkManager;

        public LanConnectionStatus Status { get; private set; } = LanConnectionStatus.Offline;
        public bool IsHost => networkManager.IsServerStarted;

        // ServerManager.ShareIds (default on) sends every client id to each client,
        // so the client-side list already counts everyone including the host.
        public int PlayerCount => IsHost
            ? networkManager.ServerManager.Clients.Count
            : networkManager.ClientManager.Clients.Count;

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }

        private void OnDestroy()
        {
            if (networkManager != null) networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
        }

        public void Host()
        {
            if (Status != LanConnectionStatus.Offline && Status != LanConnectionStatus.Failed) return;
            networkManager.ServerManager.StartConnection();
            networkManager.ClientManager.StartConnection("localhost");
        }

        public bool Join(string address)
        {
            if (Status != LanConnectionStatus.Offline && Status != LanConnectionStatus.Failed) return false;
            address = address?.Trim();
            if (string.IsNullOrEmpty(address)) return false;
            return networkManager.ClientManager.StartConnection(address);
        }

        public void Leave()
        {
            if (networkManager.IsClientStarted) networkManager.ClientManager.StopConnection();
            if (networkManager.IsServerStarted) networkManager.ServerManager.StopConnection(true);
            Status = LanConnectionStatus.Offline;
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            switch (args.ConnectionState)
            {
                case LocalConnectionState.Starting: Status = LanConnectionStatus.Connecting; break;
                case LocalConnectionState.Started: Status = LanConnectionStatus.Connected; break;
                case LocalConnectionState.Stopped:
                    // Stopped without ever connecting means the address was unreachable or timed out.
                    Status = Status == LanConnectionStatus.Connecting ? LanConnectionStatus.Failed : LanConnectionStatus.Offline;
                    if (networkManager.IsServerStarted) networkManager.ServerManager.StopConnection(true);
                    break;
            }
        }
    }
}
