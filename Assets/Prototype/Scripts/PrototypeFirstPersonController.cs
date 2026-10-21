using UnityEngine;

namespace Marco.Prototype
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PrototypeFirstPersonController : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 4.5f;
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private float ghostSpeed = 6f;
        [SerializeField, Min(1)] private float sprintMultiplier = 1.6f;
        [SerializeField] private Camera playerCamera;
        [SerializeField, Min(0.05f)] private float walkingRingInterval = 0.45f;
        [SerializeField, Min(0.05f)] private float runningRingInterval = 0.3f;
        [SerializeField, Min(0.05f)] private float walkingRingRadius = 1f;
        [SerializeField, Min(0.05f)] private float walkingRingLifetime = 0.8f;
        private CharacterController controller;
        private WalkingFootRings footRings;
        private float nextFootRingTime;
        private float pitch;
        public bool IsRunning { get; private set; }
        public float LookPitch => pitch;

        private void Start()
        {
            controller = GetComponent<CharacterController>();
            footRings = GetComponent<WalkingFootRings>();
            if (footRings == null) footRings = gameObject.AddComponent<WalkingFootRings>();
            footRings.Configure(walkingRingRadius, walkingRingLifetime, 8);
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null) { Debug.LogError("Player camera is missing.", this); enabled = false; return; }
            pitch = playerCamera.transform.localEulerAngles.x;
            if (pitch > 180) pitch -= 360;
            Cursor.lockState = CursorLockMode.Locked;
        }
        private void Update()
        {
            IsRunning = false;
            if (Input.GetKeyDown(KeyCode.Escape))
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            if (!MatchLaunchContext.IsActive && Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
                Cursor.lockState = CursorLockMode.Locked;
            if (Cursor.lockState != CursorLockMode.Locked) return;
            transform.Rotate(0, Input.GetAxis("Mouse X") * mouseSensitivity, 0);
            pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -70, 85);
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
            else if (controller.enabled)
            {
                IsRunning = movement.sqrMagnitude > 0 && Input.GetKey(KeyCode.LeftShift);
                controller.SimpleMove(movement * moveSpeed * multiplier * (IsRunning ? sprintMultiplier : 1));
                bool walkingOnGround = controller.isGrounded && movement.sqrMagnitude > 0.01f;
                if (walkingOnGround)
                {
                    nextFootRingTime -= Time.deltaTime;
                    if (nextFootRingTime <= 0f)
                    {
                        float groundY = controller.bounds.min.y + 0.025f;
                        footRings.SpawnRing(new Vector3(transform.position.x, groundY, transform.position.z));
                        nextFootRingTime = IsRunning ? runningRingInterval : walkingRingInterval;
                    }
                }
                else
                {
                    nextFootRingTime = 0f;
                }
            }
        }
    }
}
