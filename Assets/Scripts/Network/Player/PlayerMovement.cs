using FishNet.Component.Transforming;
using FishNet.Object;
using UnityEngine;

namespace Marco.Network.Player
{
    // Owner-only first-person WASD + mouse look. Values follow PrototypeFirstPersonController.
    // Position/rotation reach other clients through a client-authoritative NetworkTransform.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private Transform cameraPivot;

        private CharacterController controller;
        private float pitch;

        private void Awake() => controller = GetComponent<CharacterController>();

        // Owner only. An enabled CharacterController overrides direct transform writes, and
        // NetworkTransform.Teleport makes other clients snap instead of sliding across the map.
        public void Teleport(Vector3 position)
        {
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            if (TryGetComponent(out NetworkTransform networkTransform)) networkTransform.Teleport();
        }

        private void Update()
        {
            if (!IsOwner) return;

            if (Input.GetKeyDown(KeyCode.Escape))
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            // Clicking the HUD must not relock: locking recenters the cursor, so the IMGUI button
            // never sees its mouse-up and the click is lost.
            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0) && !NetworkTestHud.IsPointerOver)
                Cursor.lockState = CursorLockMode.Locked;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                transform.Rotate(0, Input.GetAxis("Mouse X") * mouseSensitivity, 0);
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -70, 85);
                if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
            }

            // SimpleMove applies gravity; keep falling even while the cursor is unlocked.
            var input = Cursor.lockState == CursorLockMode.Locked
                ? (transform.forward * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal")).normalized
                : Vector3.zero;
            controller.SimpleMove(input * moveSpeed);
        }
    }
}
