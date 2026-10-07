using UnityEngine;

namespace Marco.Prototype
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PrototypeFirstPersonController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private float ghostSpeed = 6f;
        [SerializeField] private Camera playerCamera;
        private CharacterController controller;
        private float pitch;

        private void Start()
        {
            controller = GetComponent<CharacterController>();
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null) { Debug.LogError("Player camera is missing.", this); enabled = false; return; }
            pitch = playerCamera.transform.localEulerAngles.x;
            if (pitch > 180) pitch -= 360;
            Cursor.lockState = CursorLockMode.Locked;
        }
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Cursor.lockState = CursorLockMode.None;
            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0)) Cursor.lockState = CursorLockMode.Locked;
            transform.Rotate(0, Input.GetAxis("Mouse X") * mouseSensitivity, 0);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -70, 70);
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            var movement = (transform.forward * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal")).normalized;
            var survivor = GetComponent<PrototypeTarget>();
            float multiplier = survivor == null ? 1f : survivor.Health.MovementMultiplier;
            if (survivor != null && survivor.Health.IsEcho)
            {
                movement = (playerCamera.transform.forward * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal"));
                if (Input.GetKey(KeyCode.Space)) movement += Vector3.up;
                if (Input.GetKey(KeyCode.LeftControl)) movement += Vector3.down;
                transform.position += Vector3.ClampMagnitude(movement, 1) * ghostSpeed * Time.deltaTime;
            }
            else if (controller.enabled) controller.SimpleMove(movement * moveSpeed * multiplier);
        }
    }
}
