using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using UnityEngine;

namespace Marco.Network.Player
{
    // Spectating: joined after the round started; takes part from the next round.
    public enum PlayerStatus : byte { Alive, Escaped, Tagged, Spectating }

    // Turns the camera/input on only for the local owner's capsule and swaps out the scene camera.
    // Also carries the server-assigned PlayerRole and round status, synced to every client:
    // role picks the character model (or the capsule color when none is registered), an escaped
    // player is hidden, and a tagged Runner becomes an Echo (기획서 3.2): a see-through ghost without
    // collision that only other Echoes can see.
    // Lives are not here on purpose: the server sends them to the owner alone (기획서 14.2).
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        private static readonly List<NetworkPlayer> spawned = new();

        // Every spawned player on this machine (server: all, client: all observed). Id = FishNet ClientId.
        public static IReadOnlyList<NetworkPlayer> All => spawned;
        // This machine's own player (null on a dedicated server or before spawn).
        public static NetworkPlayer Local { get; private set; }

        [SerializeField] private GameObject ownerCamera;
        [SerializeField] private Color seekerColor = new(0.9f, 0.15f, 0.15f);
        [SerializeField] private Color runnerColor = new(0.15f, 0.35f, 0.9f);
        [SerializeField] private Color echoColor = new(0.6f, 0.6f, 0.6f);

        private readonly SyncVar<PlayerRole> role = new();
        private readonly SyncVar<PlayerStatus> status = new();
        // Lobby (ported from RoomSessionPrototype): nickname and whether this is the room's host.
        private readonly SyncVar<string> nickname = new();
        private readonly SyncVar<bool> roomHost = new();
        private Renderer body;
        private CharacterBody character;
        private CharacterController controller;
        private Camera sceneCamera;

        public int PlayerId => OwnerId;
        public PlayerRole Role => role.Value;
        public PlayerStatus Status => status.Value;
        public bool IsEcho => status.Value == PlayerStatus.Tagged;
        public string Nickname => nickname.Value;
        public bool IsRoomHost => roomHost.Value;
        // Owner only: lives in 0.5 steps (2.0 = 4) as last sent by the server; -1 = untouched (full).
        public int HealthHalves { get; private set; } = -1;

        public static bool TryGet(int playerId, out NetworkPlayer result)
        {
            foreach (var player in spawned)
            {
                if (player.PlayerId != playerId) continue;
                result = player;
                return true;
            }
            result = null;
            return false;
        }

        private void Awake()
        {
            // The capsule mesh is a child so it can be shaped to the CharacterController.
            body = GetComponentInChildren<Renderer>();
            character = GetComponent<CharacterBody>();
            controller = GetComponent<CharacterController>();
            role.OnChange += (_, _, _) => ApplyLook();
            status.OnChange += (_, _, _) => OnStatusChanged();
        }

        // Whether Echoes are visible depends on the local player's own status, so a change to it
        // re-applies every player's look.
        private void OnStatusChanged()
        {
            if (this != Local) ApplyLook();
            else foreach (var player in spawned) player.ApplyLook();
        }

        [ServerRpc]
        public void SetNicknameServer(string value) => nickname.Value = CleanNickname(value);

        // Prototype rules: no control characters or angle brackets, trimmed, at most 20 characters.
        public static string CleanNickname(string value)
        {
            var cleaned = new System.Text.StringBuilder();
            foreach (char c in value ?? "") if (!char.IsControl(c) && c != '<' && c != '>') cleaned.Append(c);
            string result = cleaned.ToString().Trim();
            return result.Length == 0 ? "Player" : result.Substring(0, System.Math.Min(20, result.Length));
        }

        [Server]
        public void SetRole(PlayerRole value) => role.Value = value;

        [Server]
        public void SetStatus(PlayerStatus value) => status.Value = value;

        // Movement is client-authoritative, so the server asks the owning client to move itself.
        [Server]
        public void TeleportOwner(Vector3 position) => TeleportTarget(Owner, position);

        [Server]
        public void SendHealth(int halves) => HealthTarget(Owner, halves);

        [TargetRpc]
        private void HealthTarget(NetworkConnection conn, int halves) => HealthHalves = halves;

        [TargetRpc]
        private void TeleportTarget(NetworkConnection conn, Vector3 position) =>
            GetComponent<PlayerMovement>().Teleport(position);

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            spawned.Add(this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            roomHost.Value = Owner.IsLocalClient;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            spawned.Remove(this);
            if (Local == this) Local = null;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // OnChange does not fire when the synced value equals the default (Runner / Alive).
            ApplyLook();
            ownerCamera.SetActive(IsOwner);
            if (TryGetComponent(out FirstPersonBody firstPerson)) firstPerson.enabled = IsOwner;
            if (!IsOwner) return;

            Local = this;
            foreach (var player in spawned) player.ApplyLook();
            sceneCamera = Camera.main;
            if (sceneCamera != null && sceneCamera.gameObject != ownerCamera) sceneCamera.gameObject.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (!IsOwner) return;

            if (sceneCamera != null) sceneCamera.gameObject.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
        }

        private void ApplyLook()
        {
            // Runners and the Seeker never render Echoes; Echoes see each other.
            bool visible = status.Value != PlayerStatus.Escaped && (!IsEcho || (Local != null && Local.IsEcho));
            // An Echo is tag-proof and passes through walls and players on every machine.
            controller.enabled = !IsEcho;
            // A registered model replaces the capsule; the capsule stays as the fallback.
            bool modelShown = character != null && character.Show(role.Value, visible, IsEcho);
            body.enabled = visible && !modelShown;
            body.material.color = status.Value == PlayerStatus.Tagged ? echoColor : role.Value switch
            {
                PlayerRole.Seeker => seekerColor,
                PlayerRole.Echo => echoColor,
                _ => runnerColor,
            };
        }
    }
}
