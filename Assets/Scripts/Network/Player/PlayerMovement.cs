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
    public sealed partial class PlayerMovement : NetworkBehaviour
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
        private float sprintRecoveryDelayRemaining;
        private float serverSprintStamina;
        private float serverSprintExhaustionRemaining;
        private float serverSprintRecoveryDelayRemaining;
        private float nextSprintStateSyncTime;
        private bool serverWantsSprint;
        private bool lastReportedSprintIntent;
        private bool hasReportedSprintIntent;

        public bool IsSprinting { get; private set; }
        public bool IsCrouching => crouching;
        public float SprintStaminaRatio => Mathf.Clamp01(sprintStamina /
            Mathf.Max(0.01f, MovementConfig.RunnerSprintStaminaSeconds));
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
            ResetSwimmingServer();
            serverSprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
            serverSprintExhaustionRemaining = 0f;
            serverSprintRecoveryDelayRemaining = 0f;
            serverWantsSprint = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;
            sprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
            sprintExhaustionRemaining = 0f;
            sprintRecoveryDelayRemaining = 0f;
            hasReportedSprintIntent = false;
        }

        [Server]
        public void ResetSprintStamina()
        {
            ResetSwimmingServer();
            serverSprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
            serverSprintExhaustionRemaining = 0f;
            serverSprintRecoveryDelayRemaining = 0f;
            serverWantsSprint = false;
            if (IsOwner)
            {
                sprintStamina = serverSprintStamina;
                sprintExhaustionRemaining = 0f;
                sprintRecoveryDelayRemaining = 0f;
                IsSprinting = false;
                hasReportedSprintIntent = false;
            }
            SprintStaminaResetTarget(Owner);
        }

        // Owner only. An enabled CharacterController overrides direct transform writes, and
        // NetworkTransform.Teleport makes other clients snap instead of sliding across the map.
        public void Teleport(Vector3 position)
        {
            waterFxReady = false;
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.position = position;
            controller.enabled = wasEnabled;
            verticalVelocity = 0;
            if (TryGetComponent(out NetworkTransform networkTransform)) networkTransform.Teleport();
        }

        // A new round starts facing the spawn's open route, not the previous round's view.
        public void TeleportToSpawn(Vector3 position, Quaternion rotation)
        {
            transform.rotation = rotation;
            pitch = 0f;
            if (cameraPivot != null) cameraPivot.localRotation = Quaternion.identity;
            Teleport(position);
        }

        // NetworkPlayer calls this when a crouch SyncVar changes on remote clients. The owner
        // applies the same dimensions immediately on input so movement feels responsive.
        public void ApplyCrouchPresentation(bool value)
        {
            if (swimming) return;
            crouching = value;
            float height = value ? Mathf.Min(crouchedHeight, standingHeight) : standingHeight;
            controller.height = height;
            controller.center = standingCenter + Vector3.down * ((standingHeight - height) * 0.5f);
            if (cameraPivot != null)
                cameraPivot.localPosition = standingCameraPosition + (value ? Vector3.down * crouchCameraDrop : Vector3.zero);
        }

        private void Update()
        {
            UpdateWaterPresentation();
            if (IsServerInitialized) { TickServerBreath(Time.deltaTime); TickServerSprintStamina(Time.deltaTime); }
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
                if (echoRiseRemaining > 0f)
                {
                    echoRiseRemaining = Mathf.Max(0, echoRiseRemaining - Time.deltaTime);
                    Vector3 position = transform.position;
                    position.y = Mathf.Lerp(echoRiseStartY, waterSurface + .1f, 1f - echoRiseRemaining);
                    transform.position = position;
                    return;
                }
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
            bool canCrouchRole = !IsSwimming && player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive;
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
            if (TickSwimming(input, locked && !player.IsTreatmentLocked && !startLocked))
            {
                ReportSprintIntent(false);
                IsSprinting = StepSprintStamina(ref sprintStamina, ref sprintExhaustionRemaining,
                    ref sprintRecoveryDelayRemaining, false, Time.deltaTime);
                return;
            }
            bool wantsSprint = moving && !player.IsTreatmentLocked && !startLocked && !crouching &&
                player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive && Input.GetKey(KeyCode.LeftShift);
            ReportSprintIntent(wantsSprint);
            IsSprinting = StepSprintStamina(ref sprintStamina, ref sprintExhaustionRemaining,
                ref sprintRecoveryDelayRemaining, wantsSprint, Time.deltaTime);

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
                if (NetworkRound.Instance != null && NetworkRound.Instance.IsSurfaceSwimming(player))
                    speed = MovementConfig.SurfaceSwimSpeed;
                else if (sneaking)
                    speed = MovementConfig.SeekerSneakSpeed;
                else
                {
                    float seekerSpeed = NetworkRound.Instance != null
                        ? NetworkRound.Instance.SeekerChaseSpeed
                        : MovementConfig.SeekerBaseMoveSpeed * MovementConfig.GetSeekerPopulationSpeedMultiplier(NetworkPlayer.All.Count);
                    float suddenDeathMultiplier = NetworkRound.Instance != null
                        ? NetworkRound.Instance.SeekerSpeedMultiplierFor(player.Role) : 1f;
                    speed = seekerSpeed * suddenDeathMultiplier;
                }
            }
            else
            {
                bool swimming = NetworkRound.Instance != null && NetworkRound.Instance.IsSurfaceSwimming(player);
                speed = swimming ? MovementConfig.SurfaceSwimSpeed
                    : (IsSprinting ? MovementConfig.RunnerSprintSpeed : MovementConfig.RunnerWalkSpeed) * healthMultiplier;
                if (!swimming && sprintExhaustionRemaining > 0f)
                    speed *= MovementConfig.RunnerSprintExhaustionSpeedMultiplier;
            }
            if (SwimPenaltyRemaining > 0f) speed *= .8f;
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
                player.Status == PlayerStatus.Alive && !player.IsTreatmentLocked && !player.IsCrouching && !IsSwimming;
        }

        private void TickServerSprintStamina(float deltaTime)
        {
            bool eligible = serverWantsSprint && player != null && player.Role == PlayerRole.Runner &&
                player.Status == PlayerStatus.Alive && !player.IsTreatmentLocked && !player.IsCrouching && !IsSwimming;
            StepSprintStamina(ref serverSprintStamina, ref serverSprintExhaustionRemaining, ref serverSprintRecoveryDelayRemaining, eligible, deltaTime);
            if (Time.time < nextSprintStateSyncTime) return;
            nextSprintStateSyncTime = Time.time + MovementConfig.RunnerSprintStateSyncSeconds;
            SprintStaminaTarget(Owner, serverSprintStamina, serverSprintExhaustionRemaining, serverSprintRecoveryDelayRemaining);
        }

        private bool StepSprintStamina(ref float stamina, ref float exhaustionRemaining, ref float recoveryDelayRemaining, bool wantsSprint, float deltaTime)
        {
            WinConditionConfig config = MovementConfig;
            if (exhaustionRemaining > 0f)
            {
                exhaustionRemaining = Mathf.Max(0f, exhaustionRemaining - deltaTime);
                if (recoveryDelayRemaining > 0f) recoveryDelayRemaining = Mathf.Max(0f, recoveryDelayRemaining - deltaTime);
                else stamina = Mathf.Min(config.RunnerSprintStaminaSeconds,
                    stamina + config.RunnerSprintStaminaRecoveryPerSecond * deltaTime);
                return false;
            }

            if (wantsSprint && stamina > 0f)
            {
                recoveryDelayRemaining = 2f;
                stamina = Mathf.Max(0f, stamina - config.RunnerSprintStaminaDrainPerSecond * deltaTime);
                if (stamina <= 0f)
                {
                    exhaustionRemaining = config.RunnerSprintExhaustionSeconds;
                    return false;
                }
                return true;
            }

            if (recoveryDelayRemaining > 0f) recoveryDelayRemaining = Mathf.Max(0f, recoveryDelayRemaining - deltaTime);
            else stamina = Mathf.Min(config.RunnerSprintStaminaSeconds,
                stamina + config.RunnerSprintStaminaRecoveryPerSecond * deltaTime);
            return false;
        }

        [TargetRpc]
        private void SprintStaminaTarget(NetworkConnection conn, float stamina, float exhaustionRemaining, float recoveryDelayRemaining)
        {
            sprintStamina = Mathf.Clamp(stamina, 0f, MovementConfig.RunnerSprintStaminaSeconds);
            sprintExhaustionRemaining = Mathf.Max(0f, exhaustionRemaining);
            sprintRecoveryDelayRemaining = Mathf.Max(0f, recoveryDelayRemaining);
        }

        [TargetRpc]
        private void SprintStaminaResetTarget(NetworkConnection conn)
        {
            sprintStamina = MovementConfig.RunnerSprintStaminaSeconds;
            sprintExhaustionRemaining = 0f;
            sprintRecoveryDelayRemaining = 0f;
            IsSprinting = false;
            hasReportedSprintIntent = false;
        }

        [ServerRpc]
        private void SetCrouchServer(bool value) => player.SetCrouching(value);
    }
}
