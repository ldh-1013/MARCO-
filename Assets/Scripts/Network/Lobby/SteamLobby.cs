using UnityEngine;

namespace Marco.Network.Lobby
{
    // Kept as a serialized-script compatibility shim for scenes that still reference this GUID.
    // New scenes should add UnityRelayLobby directly.
    [RequireComponent(typeof(LanConnector))]
    public sealed class SteamLobby : UnityRelayLobby { }
}
