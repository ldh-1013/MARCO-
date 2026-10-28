using System;
using System.Net;
using System.Net.Sockets;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.UTP;
using UnityEngine;

namespace Marco.Network
{
    public enum LanConnectionStatus { Offline, Connecting, Connected, Failed }

    // Starts and stops FishNet sessions over LAN (Tugboat) or Unity Relay (FishyUnityTransport).
    // With Multipass (GlobalServerActions off), only the selected transport's server runs.
    [RequireComponent(typeof(NetworkManager))]
    public sealed class LanConnector : MonoBehaviour
    {
        // Prototype: "접속 시간 초과" after 8 seconds.
        [SerializeField] private float connectTimeout = 8f;

        private NetworkManager networkManager;
        private Multipass multipass;
        private float connectDeadline;
        private bool startRelayClientWhenServerStarts;
        private bool runInBackgroundBefore;
        private bool keepingAliveInBackground;

        public LanConnectionStatus Status { get; private set; } = LanConnectionStatus.Offline;
        public bool IsHost => networkManager.IsServerStarted;
        public bool UsingRelay { get; private set; }
        public bool TimedOut { get; private set; }
        public string AdvertisedIp { get; private set; } = "127.0.0.1";
        // "IP:port" a LAN guest types in, as the prototype's 초대 IP.
        public string InviteAddress => AdvertisedIp + ":" + LanTransport.GetPort();

        // ServerManager.ShareIds (default on) sends every client id to each client,
        // so the client-side list already counts everyone including the host.
        public int PlayerCount => IsHost
            ? networkManager.ServerManager.Clients.Count
            : networkManager.ClientManager.Clients.Count;

        public event Action<ServerConnectionStateArgs> ServerStateChanged;

        private Transport LanTransport => multipass != null ? Find<Tugboat>() : networkManager.TransportManager.Transport;

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            multipass = networkManager.TransportManager.Transport as Multipass;
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            networkManager.ServerManager.OnServerConnectionState += OnServerConnectionState;
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) { AdvertisedIp = ip.ToString(); break; }
        }

        private void OnDestroy()
        {
            if (networkManager == null) return;
            networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            networkManager.ServerManager.OnServerConnectionState -= OnServerConnectionState;
        }

        private void Update()
        {
            if (Status != LanConnectionStatus.Connecting || Time.unscaledTime < connectDeadline) return;
            Leave();
            TimedOut = true;
            Status = LanConnectionStatus.Failed;
        }

        private bool CanStart => Status == LanConnectionStatus.Offline || Status == LanConnectionStatus.Failed;

        public void Host()
        {
            if (!CanStart) return;
            UseClientTransport<Tugboat>(false);
            if (!StartServer<Tugboat>()) return;
            StartClient(() => networkManager.ClientManager.StartConnection("localhost"));
        }

        // address: "IP", "IP:port" or the prototype's "MARCO-" code.
        public bool Join(string address)
        {
            if (!CanStart || !TryParseAddress(address, out var ip, out ushort port)) return false;
            UseClientTransport<Tugboat>(false);
            return StartClient(() => networkManager.ClientManager.StartConnection(ip.ToString(), port));
        }

        // The relay host starts its local FishNet client after its relay server has started.
        public bool HostRelay()
        {
            if (!CanStart || multipass == null || Find<FishyUnityTransport>() == null) return false;
            UseClientTransport<FishyUnityTransport>(true);
            startRelayClientWhenServerStarts = true;
            if (StartServer<FishyUnityTransport>()) return true;
            startRelayClientWhenServerStarts = false;
            return false;
        }

        public bool JoinRelay()
        {
            if (!CanStart || multipass == null || Find<FishyUnityTransport>() == null) return false;
            UseClientTransport<FishyUnityTransport>(true);
            return StartClient(() => networkManager.ClientManager.StartConnection());
        }

        public void Leave()
        {
            startRelayClientWhenServerStarts = false;
            if (networkManager.IsClientStarted) networkManager.ClientManager.StopConnection();
            StopServer();
            Status = LanConnectionStatus.Offline;
            RestoreBackgroundMode();
        }

        public static string EncodeAddress(string endpoint) =>
            "MARCO-" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(endpoint)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        // Same rules as the prototype: IPv4 only, optional port, or a MARCO- code of "IP:port".
        public bool TryParseAddress(string input, out IPAddress ip, out ushort port)
        {
            ip = null;
            port = LanTransport.GetPort();
            try
            {
                input = input?.Trim() ?? "";
                if (input.StartsWith("MARCO-", StringComparison.Ordinal))
                {
                    string encoded = input.Substring(6).Replace('-', '+').Replace('_', '/');
                    encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
                    input = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                }
                string[] parts = input.Split(':');
                if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
                return parts.Length == 1 || (ushort.TryParse(parts[1], out port) && port > 0);
            }
            catch (FormatException) { return false; }
        }

        private bool StartClient(Func<bool> start)
        {
            TimedOut = false;
            KeepAliveInBackground();
            connectDeadline = Time.unscaledTime + connectTimeout;
            if (start()) return true;
            Leave();
            Status = LanConnectionStatus.Failed;
            return false;
        }

        private void UseClientTransport<T>(bool relay) where T : Transport
        {
            UsingRelay = relay;
            if (multipass != null) multipass.SetClientTransport<T>();
        }

        private bool StartServer<T>() where T : Transport
        {
            KeepAliveInBackground();
            bool started = multipass != null
                ? multipass.StartConnection(true, IndexOf<T>())
                : networkManager.ServerManager.StartConnection();
            if (!started) { Status = LanConnectionStatus.Failed; RestoreBackgroundMode(); }
            return started;
        }

        private void StopServer()
        {
            if (multipass == null)
            {
                if (networkManager.IsServerStarted) networkManager.ServerManager.StopConnection(true);
                return;
            }
            for (int i = 0; i < multipass.Transports.Count; i++)
                if (multipass.GetConnectionState(true, i) != LocalConnectionState.Stopped) multipass.StopConnection(true, i);
        }

        private T Find<T>() where T : Transport
        {
            foreach (var transport in multipass.Transports) if (transport is T match) return match;
            return null;
        }

        private int IndexOf<T>() where T : Transport
        {
            for (int i = 0; i < multipass.Transports.Count; i++) if (multipass.Transports[i] is T) return i;
            return -1;
        }

        // Prototype: the session keeps running while the window is in the background.
        private void KeepAliveInBackground()
        {
            if (!keepingAliveInBackground) runInBackgroundBefore = Application.runInBackground;
            keepingAliveInBackground = true;
            Application.runInBackground = true;
        }

        private void RestoreBackgroundMode()
        {
            if (!keepingAliveInBackground || networkManager.IsServerStarted || networkManager.IsClientStarted) return;
            Application.runInBackground = runInBackgroundBefore;
            keepingAliveInBackground = false;
        }

        private void OnServerConnectionState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started && startRelayClientWhenServerStarts)
            {
                startRelayClientWhenServerStarts = false;
                StartClient(() => networkManager.ClientManager.StartConnection());
            }
            ServerStateChanged?.Invoke(args);
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
                    StopServer();
                    RestoreBackgroundMode();
                    break;
            }
        }
    }
}
