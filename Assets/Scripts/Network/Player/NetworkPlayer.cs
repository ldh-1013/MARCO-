using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.WinCondition;
using UnityEngine;

namespace Marco.Network.Player
{
    // Turns the camera/input on only for the local owner's capsule and swaps out the scene camera.
    // Also carries the server-assigned PlayerRole, synced to every client and shown as capsule color.
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
        private Renderer body;
        private Camera sceneCamera;

        public int PlayerId => OwnerId;
        public PlayerRole Role => role.Value;

        private void Awake()
        {
            body = GetComponent<Renderer>();
            role.OnChange += (_, next, _) => ApplyColor(next);
        }

        [Server]
        public void SetRole(PlayerRole value) => role.Value = value;

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
            // OnChange does not fire when the synced value equals the default (Runner).
            ApplyColor(role.Value);
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

        private void ApplyColor(PlayerRole value)
        {
            body.material.color = value switch
            {
                PlayerRole.Seeker => seekerColor,
                PlayerRole.Echo => echoColor,
                _ => runnerColor,
            };
        }
    }
}
