using FishNet.Connection;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Audio;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Player
{
    public sealed partial class PlayerMovement
    {
        public const float MaxBreath = 14f;
        public const float SwimmingEyeOffset = .25f;
        private readonly SoundWorld swimWorld = new();
        private bool swimming, serverDiveHeld, diveReported, forcedSurface, serverForcedSurface;
        private float breath = MaxBreath, serverBreath = MaxBreath, lastBreathUse = -10f;
        private float penaltyUntil, serverPenaltyUntil, nextBreathSync, nextDiveSync;
        private float waterSurface;
        private float echoRiseRemaining, echoRiseStartY;
        public bool IsSwimming => swimming;
        public bool IsDiving => swimming && WaterEyeHeight < waterSurface - .03f;
        // Server-side work eligibility uses accepted input, remaining breath and measured depth.
        public bool CanWorkUnderwater => IsServerInitialized && serverDiveHeld && !serverForcedSurface &&
            serverBreath > 0f && player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive &&
            !player.IsTreatmentLocked &&
            (NetworkRound.Instance == null || !NetworkRound.Instance.IsStartLocked(player.Role)) &&
            swimWorld.WaterState(player, out _) == 2;
        public float BreathSeconds => breath;
        public float SwimPenaltyRemaining => Mathf.Max(0, penaltyUntil - Time.time);
        public float WaterEyeHeight => transform.position.y + (swimming ? SwimmingEyeOffset : standingCameraPosition.y);

        // Never use the resized swimming capsule to decide whether to undo that resize.
        // The standing footprint stays at the same world height during crouch/swim presentation.
        public bool HasWaterContact(float surface)
        {
            if (controller == null || !controller.enabled) return false;
            float feet = transform.TransformPoint(standingCenter).y - standingHeight * Mathf.Abs(transform.lossyScale.y) * .5f;
            return feet < surface + (swimming ? .05f : -.05f);
        }

        public int WaterStateAtSurface(float surface)
        {
            if (!HasWaterContact(surface)) return 0;
            // Use the effective wet eye height even if queried just before Update applies the pose.
            return transform.position.y + SwimmingEyeOffset < surface - .03f ? 2 : 1;
        }

        private void UpdateWaterPresentation()
        {
            bool wet = player.Status == PlayerStatus.Alive && !player.IsEcho &&
                swimWorld.TryPool(transform.position, out var pool) && controller.enabled &&
                HasWaterContact(pool.center.y);
            if (swimWorld.TryPool(transform.position, out var area)) waterSurface = area.center.y;
            if (wet == swimming) return;
            if (swimming && player.IsEcho && IsOwner && IsDiving)
            {
                echoRiseRemaining = 1f;
                echoRiseStartY = transform.position.y;
            }
            swimming = wet;
            if (wet)
            {
                crouching = false;
                if (IsOwner) SetCrouchServer(false);
                controller.height = .7f;
                controller.center = Vector3.zero;
                if (cameraPivot != null) cameraPivot.localPosition = new Vector3(standingCameraPosition.x, SwimmingEyeOffset, standingCameraPosition.z);
                verticalVelocity = 0;
            }
            else ApplyCrouchPresentation(false);
        }

        private bool TickSwimming(Vector3 input, bool allowed)
        {
            bool held = allowed && swimming && player.Role == PlayerRole.Runner && Input.GetKey(KeyCode.LeftControl);
            // Retry while held: the first request can arrive before the replicated position
            // reaches the pool on a remote server. Release is still sent immediately.
            if (held != diveReported || (held && Time.time >= nextDiveSync))
            {
                diveReported = held;
                nextDiveSync = Time.time + .1f;
                SetDiveServer(held);
            }
            if (!swimming) return false;
            bool dive = held && !forcedSurface && breath > 0f;
            Vector3 direction = input;
            if (dive && input.sqrMagnitude > .001f && cameraPivot != null)
                direction = Vector3.ClampMagnitude(cameraPivot.forward * Input.GetAxisRaw("Vertical") +
                    transform.right * Input.GetAxisRaw("Horizontal"), 1f);
            float speed = MovementConfig.SurfaceSwimSpeed;
            if (player.Role == PlayerRole.Runner)
            {
                speed *= NetworkRound.Instance != null
                    ? NetworkRound.Instance.SpeedMultiplierFor(player.HealthHalves) : 1f;
                if (sprintExhaustionRemaining > 0f)
                    speed *= MovementConfig.RunnerSprintExhaustionSpeedMultiplier;
            }
            if (SwimPenaltyRemaining > 0f) speed *= .8f;
            // Ctrl initiates a descent, then looking up/down steers. Releasing always surfaces.
            float targetY = waterSurface - .1f;
            float vy = dive ? (IsDiving ? direction.y * speed : -2.5f) :
                Mathf.Clamp((targetY - transform.position.y) / Mathf.Max(Time.deltaTime, .001f), -2.5f, 3.5f);
            if (dive && IsDiving && input.sqrMagnitude < .001f) vy = 0f;
            controller.Move((Vector3.ProjectOnPlane(direction, Vector3.up) * speed + Vector3.up * vy) * Time.deltaTime);
            if (allowed && !IsDiving && Input.GetKeyDown(KeyCode.Space)) TryLeavePool(input);
            return true;
        }

        // Only climb the actual pool edge into a clear, supported standing capsule.
        private void TryLeavePool(Vector3 input)
        {
            if (!swimWorld.TryPool(transform.position, out var pool)) return;
            Vector3 direction = input.sqrMagnitude > .01f ? input.normalized : transform.forward;
            Vector3 destination = transform.position + direction * 1.1f;
            if (swimWorld.TryPool(destination, out _)) return;
            if (!Physics.Raycast(new Vector3(destination.x, pool.center.y + 1f, destination.z), Vector3.down,
                out var floor, 1.5f, ~0, QueryTriggerInteraction.Ignore) || floor.normal.y < .7f) return;
            destination.y = floor.point.y - standingCenter.y + standingHeight * .5f + .06f;
            Vector3 center = destination + standingCenter;
            float half = standingHeight * .5f - controller.radius;
            if (Physics.CheckCapsule(center + Vector3.up * half, center - Vector3.up * half,
                controller.radius, ~0, QueryTriggerInteraction.Ignore)) return;
            Vector3 raised = new Vector3(transform.position.x, destination.y, transform.position.z);
            Vector3 travel = destination - raised;
            if (Physics.CapsuleCast(raised + standingCenter + Vector3.up * half,
                raised + standingCenter - Vector3.up * half, controller.radius,
                travel.normalized, travel.magnitude, ~0, QueryTriggerInteraction.Ignore)) return;
            controller.Move(Vector3.up * (destination.y - transform.position.y));
            if (Mathf.Abs(transform.position.y - destination.y) > .1f) return;
            controller.Move(travel);
            UpdateWaterPresentation();
        }

        [ServerRpc]
        private void SetDiveServer(bool held)
        {
            serverDiveHeld = held && swimming && player.Role == PlayerRole.Runner &&
                player.Status == PlayerStatus.Alive && !player.IsTreatmentLocked &&
                (NetworkRound.Instance == null || !NetworkRound.Instance.IsStartLocked(player.Role));
        }

        private void ResetSwimmingServer()
        {
            ResetShoutServer();
            serverBreath = MaxBreath; lastBreathUse = -10f; serverPenaltyUntil = 0;
            serverDiveHeld = serverForcedSurface = false;
            if (IsOwner) { breath = MaxBreath; forcedSurface = false; penaltyUntil = 0; diveReported = false; }
            BreathTarget(Owner, MaxBreath, false, 0);
        }

        private void TickServerBreath(float dt)
        {
            if (player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive) return;
            // Work stops as soon as the held input, breath or underwater position is invalid.
            if (!CanWorkUnderwater && NetworkRound.Instance != null && gameObject.scene.name == "NetworkPool_v1_2")
            {
                NetworkRound.Instance.EndWork(player.PlayerId, ValveId.B);
                NetworkRound.Instance.EndWork(player.PlayerId, ValveId.E);
                foreach (DrainHatchId id in System.Enum.GetValues(typeof(DrainHatchId)))
                    NetworkRound.Instance.EndDrainWork(player.PlayerId, id);
            }
            if (IsDiving)
            {
                serverBreath = Mathf.Max(0, serverBreath - dt);
                lastBreathUse = Time.time;
                if (serverBreath <= 0 && !serverForcedSurface)
                {
                    serverForcedSurface = true;
                    serverPenaltyUntil = Time.time + 3f;
                    NetworkRound.Instance?.EmitDrowningSound(player, waterSurface);
                }
            }
            else
            {
                if (Time.time > lastBreathUse + 2f)
                    serverBreath = Mathf.Min(MaxBreath, serverBreath + dt * (swimming ? 2f : 4f));
                if (!serverDiveHeld && Time.time >= serverPenaltyUntil) serverForcedSurface = false;
            }
            if (Time.time < nextBreathSync) return;
            nextBreathSync = Time.time + .1f;
            BreathTarget(Owner, serverBreath, serverForcedSurface, Mathf.Max(0, serverPenaltyUntil - Time.time));
        }

        // Shared resource entry point for the shout/scream suppression ability.
        [Server]
        public bool TrySpendSuppressionBreath()
        {
            if (player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive || IsDiving || serverBreath < 4.5f) return false;
            serverBreath -= 4.5f; lastBreathUse = Time.time;
            return true;
        }

        [TargetRpc]
        private void BreathTarget(NetworkConnection connection, float value, bool force, float penalty)
        {
            breath = value; forcedSurface = force; penaltyUntil = Time.time + penalty;
        }
    }
}
