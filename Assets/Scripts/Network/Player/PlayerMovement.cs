using FishNet.Component.Transforming;
using FishNet.Object;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Player
{
    // Owner-only first-person WASD + mouse look. Values follow PrototypeFirstPersonController.
    // Position/rotation reach other clients through a client-authoritative NetworkTransform.
    // An Echo flies freely through walls instead (기획서 3.2, Space up / Left Ctrl down);
    // a recovering Runner (lives 1.5) is slower (기획서 3.3-1).
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float echoSpeed = 6f;
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private Transform cameraPivot;

        private CharacterController controller;
        private NetworkPlayer player;
        private float pitch;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            player = GetComponent<NetworkPlayer>();
        }

        // Owner only. An enabled CharacterController overrides direct transform writes, and
        // NetworkTransform.Teleport makes other clients snap instead of sliding across the map.
        public void Teleport(Vector3 position)
        {
            // An Echo's controller stays off (NetworkPlayer), so restore whatever it was.
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = position;
            controller.enabled = wasEnabled;
            if (TryGetComponent(out NetworkTransform networkTransform)) networkTransform.Teleport();
        }

        private void Update()
        {
            if (!IsOwner) return;

            if (Input.GetKeyDown(KeyCode.Escape))
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            // Clicking the HUD must not relock: locking recenters the cursor, so the IMGUI button
            // never sees its mouse-up and the click is lost. An open cheat panel keeps it free too.
            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0) &&
                !TestHudSkin.IsPointerOver && !TestHudSkin.HoldsCursor)
                Cursor.lockState = CursorLockMode.Locked;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                transform.Rotate(0, Input.GetAxis("Mouse X") * mouseSensitivity, 0);
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -70, 85);
                if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (player.IsEcho)
            {
                if (!locked) return;
                var look = cameraPivot != null ? cameraPivot.forward : transform.forward;
                var fly = look * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal");
                if (Input.GetKey(KeyCode.Space)) fly += Vector3.up;
                if (Input.GetKey(KeyCode.LeftControl)) fly += Vector3.down;
                transform.position += Vector3.ClampMagnitude(fly, 1) * echoSpeed * Time.deltaTime;
                return;
            }

            // SimpleMove applies gravity; keep falling even while the cursor is unlocked.
            var input = locked
                ? (transform.forward * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal")).normalized
                : Vector3.zero;
            float multiplier = NetworkRound.Instance != null ? NetworkRound.Instance.SpeedMultiplierFor(player.HealthHalves) : 1;
            controller.SimpleMove(input * moveSpeed * multiplier);
        }
    }
}
