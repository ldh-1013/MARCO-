using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Player
{
    // Owner-only first-person movement. Position/rotation reach other clients through the
    // client-authoritative NetworkTransform. Echoes fly freely through walls instead.
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovement : NetworkBehaviour
    {
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.25f;
        [SerializeField, Min(0.1f)] private float gravity = 22f;
        [SerializeField] private float mouseSensitivity = 2.2f;
        [SerializeField] private Transform cameraPivot;
        [SerializeField, Min(0.5f)] private float crouchedHeight = 1.1f;
        [SerializeField, Min(0.05f)] private float crouchCameraDrop = 0.35f;

        private CharacterController controller;
        private NetworkPlayer player;
        private float pitch;
        private float verticalVelocity;
        private float standingHeight;
        private Vector3 standingCenter;
        private Vector3 standingCameraPosition;
        private bool crouching;
        private static readonly WinConditionConfig FallbackMovementConfig = new();
        private float sprintStamina;
        private float sprintExhaustionRemaining;
        private float serverSprintStamina;
        private float serverSprintExhaustionRemaining;
        private float nextSprintStateSyncTime;
        private bool serverWantsSprint;
        private bool lastReportedSprintIntent;
        private bool hasReportedSprintIntent;

        public bool IsSprinting { get; private set; }
        public bool IsCrouching => crouching;
        private WinConditionConfig MovementConfig => NetworkRound.Instance != null
            ? NetworkRound.Instance.MovementConfig : FallbackMovementConfig;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            player = GetComponent<NetworkPlayer>();
            standingHeight = controller.height;
            standingCenter = controller.center;
            if (cameraPivot != null) standingCameraPosition = cameraPivot.localPosition;
            sprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
            serverSprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            serverSprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
            serverSprintExhaustionRemaining = 0f;
            serverWantsSprint = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;
            sprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
            sprintExhaustionRemaining = 0f;
            hasReportedSprintIntent = false;
        }

        [Server]
        public void ResetSprintStamina()
        {
            serverSprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
            serverSprintExhaustionRemaining = 0f;
            serverWantsSprint = false;
            if (IsOwner)
            {
                sprintStamina = serverSprintStamina;
                sprintExhaustionRemaining = 0f;
                IsSprinting = false;
                hasReportedSprintIntent = false;
            }
            SprintStaminaResetTarget(Owner);
        }

        // Owner only. An enabled CharacterController overrides direct transform writes, and
        // NetworkTransform.Teleport makes other clients snap instead of sliding across the map.
        public void Teleport(Vector3 position)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = position;
            controller.enabled = wasEnabled;
            verticalVelocity = 0;
            if (TryGetComponent(out NetworkTransform networkTransform)) networkTransform.Teleport();
        }

        // NetworkPlayer calls this when a crouch SyncVar changes on remote clients. The owner
        // applies the same dimensions immediately on input so movement feels responsive.
        public void ApplyCrouchPresentation(bool value)
        {
            crouching = value;
            float height = value ? Mathf.Min(crouchedHeight, standingHeight) : standingHeight;
            controller.height = height;
            controller.center = standingCenter + Vector3.down * ((standingHeight - height) * 0.5f);
            if (cameraPivot != null)
                cameraPivot.localPosition = standingCameraPosition + (value ? Vector3.down * crouchCameraDrop : Vector3.zero);
        }

        private void Update()
        {
            if (IsServerInitialized) TickServerSprintStamina(Time.deltaTime);
            if (!IsOwner) return;

            if (Input.GetKeyDown(KeyCode.Escape))
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            // Clicking the HUD must not relock: locking recenters the cursor, so the IMGUI button
            // never sees its mouse-up and the click is lost. An open cheat panel keeps it free too.
            if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0) &&
                !TestHudSkin.IsPointerOver && !TestHudSkin.HoldsCursor)
                Cursor.lockState = CursorLockMode.Locked;

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (locked)
            {
                transform.Rotate(0, Input.GetAxis("Mouse X") * mouseSensitivity, 0);
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -70, 85);
                if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(pitch, 0, 0);
            }

            if (player.IsEcho)
            {
                IsSprinting = false;
                ReportSprintIntent(false);
                if (!locked) return;
                var look = cameraPivot != null ? cameraPivot.forward : transform.forward;
                var fly = look * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal");
                if (Input.GetKey(KeyCode.Space)) fly += Vector3.up;
                if (Input.GetKey(KeyCode.LeftControl)) fly += Vector3.down;
                transform.position += Vector3.ClampMagnitude(fly, 1) * MovementConfig.EchoMoveSpeed * Time.deltaTime;
                return;
            }

            // Role reveal / Seeker hold at round start (기획서 부록 A [라운드 시작]): looking around stays free.
            bool startLocked = NetworkRound.Instance != null && NetworkRound.Instance.IsStartLocked(player.Role);
            bool canCrouchRole = player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive;
            if (locked && canCrouchRole && !player.IsTreatmentLocked && !startLocked && Input.GetKeyDown(KeyCode.C))
            {
                ApplyCrouchPresentation(!crouching);
                SetCrouchServer(crouching);
            }
            else if (crouching && !canCrouchRole)
            {
                ApplyCrouchPresentation(false);
                SetCrouchServer(false);
            }

            Vector3 input = locked && !player.IsTreatmentLocked && !startLocked
                ? (transform.forward * Input.GetAxisRaw("Vertical") + transform.right * Input.GetAxisRaw("Horizontal"))
                : Vector3.zero;
            input = Vector3.ClampMagnitude(input, 1f);
            bool moving = input.sqrMagnitude > 0.001f;
            bool wantsSprint = moving && !player.IsTreatmentLocked && !startLocked && !crouching &&
                player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive && Input.GetKey(KeyCode.LeftShift);
            ReportSprintIntent(wantsSprint);
            IsSprinting = StepSprintStamina(ref sprintStamina, ref sprintExhaustionRemaining, wantsSprint, Time.deltaTime);

            // CharacterController.SimpleMove includes gravity but cannot jump. Move preserves its
            // grounded behavior while giving both living roles an owner-controlled jump arc.
            bool grounded = controller.isGrounded;
            if (grounded && verticalVelocity < 0f) verticalVelocity = -2f;
            if (locked && !player.IsTreatmentLocked && !startLocked && player.Status == PlayerStatus.Alive && grounded && Input.GetKeyDown(KeyCode.Space))
                verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
            verticalVelocity -= gravity * Time.deltaTime;

            float healthMultiplier = NetworkRound.Instance != null
                ? NetworkRound.Instance.SpeedMultiplierFor(player.HealthHalves) : 1f;
            float speed;
            if (player.Role == PlayerRole.Seeker)
            {
                bool sneaking = moving && locked && Input.GetKey(KeyCode.LeftControl);
                float seekerSpeed = sneaking
                    ? MovementConfig.SeekerSneakSpeed
                    : NetworkRound.Instance != null
                        ? NetworkRound.Instance.SeekerChaseSpeed
                        : MovementConfig.SeekerBaseMoveSpeed * MovementConfig.GetSeekerPopulationSpeedMultiplier(NetworkPlayer.All.Count);
                // Sudden-death +5% remains an additional Seeker multiplier (기획서 6.2-1).
                float suddenDeathMultiplier = NetworkRound.Instance != null
                    ? NetworkRound.Instance.SeekerSpeedMultiplierFor(player.Role) : 1f;
                speed = seekerSpeed * suddenDeathMultiplier;
            }
            else
            {
                speed = (IsSprinting ? MovementConfig.RunnerSprintSpeed : MovementConfig.RunnerWalkSpeed) * healthMultiplier;
                if (sprintExhaustionRemaining > 0f)
                    speed *= MovementConfig.RunnerSprintExhaustionSpeedMultiplier;
            }
            Vector3 velocity = input * speed + Vector3.up * verticalVelocity;
            controller.Move(velocity * Time.deltaTime);
        }

        private void ReportSprintIntent(bool wantsSprint)
        {
            if (hasReportedSprintIntent && wantsSprint == lastReportedSprintIntent) return;
            hasReportedSprintIntent = true;
            lastReportedSprintIntent = wantsSprint;
            SetSprintIntentServer(wantsSprint);
        }

        [ServerRpc]
        private void SetSprintIntentServer(bool wantsSprint)
        {
            serverWantsSprint = wantsSprint && player != null && player.Role == PlayerRole.Runner &&
                player.Status == PlayerStatus.Alive && !player.IsTreatmentLocked && !player.IsCrouching;
        }

        private void TickServerSprintStamina(float deltaTime)
        {
            bool eligible = serverWantsSprint && player != null && player.Role == PlayerRole.Runner &&
                player.Status == PlayerStatus.Alive && !player.IsTreatmentLocked && !player.IsCrouching;
            StepSprintStamina(ref serverSprintStamina, ref serverSprintExhaustionRemaining, eligible, deltaTime);
            if (Time.time < nextSprintStateSyncTime) return;
            nextSprintStateSyncTime = Time.time + MovementConfig.RunnerSprintStateSyncSeconds;
            SprintStaminaTarget(Owner, serverSprintStamina, serverSprintExhaustionRemaining);
        }

        private bool StepSprintStamina(ref float stamina, ref float exhaustionRemaining, bool wantsSprint, float deltaTime)
        {
            WinConditionConfig config = MovementConfig;
            if (exhaustionRemaining > 0f)
            {
                exhaustionRemaining = Mathf.Max(0f, exhaustionRemaining - deltaTime);
                stamina = Mathf.Min(config.RunnerSprintStaminaSeconds,
                    stamina + config.RunnerSprintStaminaRecoveryPerSecond * deltaTime);
                return false;
            }

            if (wantsSprint && stamina > 0f)
            {
                stamina = Mathf.Max(0f, stamina - config.RunnerSprintStaminaDrainPerSecond * deltaTime);
                if (stamina <= 0f)
                {
                    exhaustionRemaining = config.RunnerSprintExhaustionSeconds;
                    return false;
                }
                return true;
            }

            stamina = Mathf.Min(config.RunnerSprintStaminaSeconds,
                stamina + config.RunnerSprintStaminaRecoveryPerSecond * deltaTime);
            return false;
        }

        [TargetRpc]
        private void SprintStaminaTarget(NetworkConnection conn, float stamina, float exhaustionRemaining)
        {
            sprintStamina = Mathf.Clamp(stamina, 0f, MovementConfig.RunnerSprintStaminaSeconds);
            sprintExhaustionRemaining = Mathf.Max(0f, exhaustionRemaining);
        }

        [TargetRpc]
        private void SprintStaminaResetTarget(NetworkConnection conn)
        {
            sprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
            sprintExhaustionRemaining = 0f;
            IsSprinting = false;
            hasReportedSprintIntent = false;
        }

        [ServerRpc]
        private void SetCrouchServer(bool value) => player.SetCrouching(value);
    }
}
