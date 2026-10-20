using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.WinCondition;
using UnityEngine;

namespace Marco.Network.Player
{
    // Spectating: joined after the round started; takes part from the next round.
    public enum PlayerStatus : byte { Alive, Escaped, Tagged, Spectating }

    // Turns the camera/input on only for the local owner's capsule and swaps out the scene camera.
    // Also carries the server-assigned PlayerRole and round status, synced to every client:
    // role sets the capsule color, a tagged Runner turns gray and an escaped one is hidden.
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        private static readonly List<NetworkPlayer> spawned = new();

        // Every spawned player on this machine (server: all, client: all observed). Id = FishNet ClientId.
        public static IReadOnlyList<NetworkPlayer> All => spawned;

        [SerializeField] private GameObject ownerCamera;
        [SerializeField] private Color seekerColor = new(0.9f, 0.15f, 0.15f);
        [SerializeField] private Color runnerColor = new(0.15f, 0.35f, 0.9f);
        [SerializeField] private Color echoColor = new(0.6f, 0.6f, 0.6f);

        private readonly SyncVar<PlayerRole> role = new();
        private readonly SyncVar<PlayerStatus> status = new();
        private Renderer body;
        private Camera sceneCamera;

        public int PlayerId => OwnerId;
        public PlayerRole Role => role.Value;
        public PlayerStatus Status => status.Value;

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
            body = GetComponent<Renderer>();
            role.OnChange += (_, _, _) => ApplyLook();
            status.OnChange += (_, _, _) => ApplyLook();
        }

        [Server]
        public void SetRole(PlayerRole value) => role.Value = value;

        [Server]
        public void SetStatus(PlayerStatus value) => status.Value = value;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            spawned.Add(this);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            spawned.Remove(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // OnChange does not fire when the synced value equals the default (Runner / Alive).
            ApplyLook();
            ownerCamera.SetActive(IsOwner);
            if (!IsOwner) return;

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
            body.enabled = status.Value != PlayerStatus.Escaped;
            body.material.color = status.Value == PlayerStatus.Tagged ? echoColor : role.Value switch
            {
                PlayerRole.Seeker => seekerColor,
                PlayerRole.Echo => echoColor,
                _ => runnerColor,
            };
        }
    }
}
