using FishNet.Connection;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using Marco.Network.Escape;
using Marco.Network.Round;
using Marco.Network.Valves;
using Marco.Network.Vision;
using UnityEngine;

namespace Marco.Network.Player
{
    // On the player prefab. The owner's input becomes ServerRpcs; the server re-checks range, role
    // and status (NetworkRound / WinCondition via IPlayerRoster). Clients decide nothing.
    // E: Runner pickup/treatment, exit, drain hatch or valve. G starts a bandage channel; Q/right-click uses Clicker.
    // Left click: swing (Seeker); the server decides the tag and all clients play the attack.
    [RequireComponent(typeof(NetworkPlayer))]
    public sealed partial class PlayerInteractor : NetworkBehaviour
    {
        private NetworkPlayer player;
        private ValveId? workingValve;
        private DrainHatchId? workingDrain;
        private bool workingTreatment;
        private bool workingBandage;
        private int selectedItemSlot = 1;
        private bool aimingDecoy;
        private DecoyTrajectoryPreview decoyPreview;
        // Owner-side swing cooldown. It starts when the server's answer arrives, so it always ends
        // after the server's own cooldown and never sends a request the server would reject.
        private bool swingPending;
        private float nextSwingTime;
        private float swingCooldownDuration;

        // Seeker only: seconds left on the swing cooldown, for the HUD.
        public float SwingCooldownRemaining => Mathf.Max(0, nextSwingTime - Time.time);
        public float SwingCooldownFraction => Mathf.Clamp01(SwingCooldownRemaining / Mathf.Max(0.01f, swingCooldownDuration));
        public bool SwingPending => swingPending;
        public int SelectedItemSlot => selectedItemSlot;
        public bool IsAimingDecoy => aimingDecoy;

        public bool CanTreatNearbyRunner
        {
            get
            {
                var round = NetworkRound.Instance;
                return IsOwner && player != null && player.Role == PlayerRole.Runner &&
                    player.Status == PlayerStatus.Alive && !player.HasHealingAction && round != null &&
                    (player.SoundCanStage != SoundCanStage.None || !round.TryFindSoundCanPickup(transform.position, out _)) &&
                    (player.DecoyCount > 0 || !round.TryFindDecoyPickup(transform.position, out _)) &&
                    (player.ClickerCount > 0 || !round.TryFindClickerPickup(transform.position, round.ClickerInteractRange, out _)) &&
                    (!round.HasBandageCapacity(player) || !round.TryFindBandagePickup(transform.position, round.InteractRange, out _)) &&
                    round.TryFindInjuredRunner(player, round.InteractRange, out _);
            }
        }

        private void Awake() => player = GetComponent<NetworkPlayer>();

        private void Update()
        {
            if (!IsOwner) return;
            var round = NetworkRound.Instance;
            if (round == null) { aimingDecoy = false; return; }

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool held = Input.GetKey(KeyCode.E) && locked;
            float range = round.InteractRange;

            if (locked && player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) selectedItemSlot = 1;
                else if (Input.GetKeyDown(KeyCode.Alpha2)) selectedItemSlot = 2;
                else if (Input.GetKeyDown(KeyCode.Alpha3)) selectedItemSlot = 3;
                else if (Input.GetKeyDown(KeyCode.Alpha4)) selectedItemSlot = 4;
            }

            if (workingBandage)
            {
                if (!Input.GetKey(KeyCode.G) || !locked)
                {
                    CancelHealingServer();
                    workingBandage = false;
                }
            }
            else if (locked && selectedItemSlot == 1 && Input.GetKeyDown(KeyCode.G) && !workingTreatment &&
                     workingValve == null && workingDrain == null && player.Role == PlayerRole.Runner &&
                     player.Status == PlayerStatus.Alive)
            {
                BeginBandageServer();
                workingBandage = true;
            }

            if (workingBandage)
            {
                // G must remain held for the entire six second bandage channel.
            }
            else if (workingValve is ValveId valve)
            {
                // Releasing E or walking away both end the work.
                if (!held || ((valve == ValveId.B || valve == ValveId.E) && GetComponent<PlayerMovement>().IsSwimming && !GetComponent<PlayerMovement>().IsDiving) || !ValveView.TryGet(valve, out var view) || !view.IsInRange(transform.position, range))
                {
                    EndWorkServer(valve);
                    workingValve = null;
                }
            }
            else if (workingDrain is DrainHatchId drain)
            {
                if (!held || (GetComponent<PlayerMovement>().IsSwimming && !GetComponent<PlayerMovement>().IsDiving) || !DrainHatchView.TryGet(drain, out var view) || !view.IsInRange(transform.position, range))
                {
                    EndDrainWorkServer(drain);
                    workingDrain = null;
                }
            }
            else if (workingTreatment)
            {
                if (!held)
                {
                    CancelHealingServer();
                    workingTreatment = false;
                }
            }
            else if (Input.GetKeyDown(KeyCode.E) && held && player.Status == PlayerStatus.Alive && !player.IsPlacingSoundCan)
            {
                // Only on a fresh press: an opened valve drops its workers, and INTERFACES.md §4
                // expects a new TryBeginWork (release and press again) to rework it.
                BeginInteraction(round, range);
            }

            if (locked && Input.GetMouseButtonDown(0) && player.Role == PlayerRole.Seeker) Swing(round);
            bool useClickerPressed = Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Q);
            HandleDecoyThrow(round, locked, Input.GetKeyDown(KeyCode.G), Input.GetKeyUp(KeyCode.G));
            TickSoundCanInput(round, locked);
            if (locked && useClickerPressed && !workingBandage && player.Role == PlayerRole.Runner &&
                player.Status == PlayerStatus.Alive && player.ClickerCount > 0 && !player.IsPlacingSoundCan)
                UseClickerServer();
        }

        private bool CanAimDecoy(NetworkRound round, bool locked) => locked && selectedItemSlot == 3 &&
            !workingBandage && !workingTreatment && workingValve == null && workingDrain == null &&
            player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive && player.DecoyCount > 0 &&
            !player.HasHealingAction && !player.IsTreatmentLocked &&
            round.Mode != RoundMode.NotStarted && round.Result == RoundResult.InProgress &&
            !round.IsStartLocked(player.Role) && !GetComponent<PlayerMovement>().IsSwimming;

        private void HandleDecoyThrow(NetworkRound round, bool locked, bool pressed, bool released)
        {
            // Losing eligibility cancels without consuming an item or sending a throw request.
            if (!CanAimDecoy(round, locked)) { aimingDecoy = false; return; }
            if (pressed) aimingDecoy = true;
            if (!released || !aimingDecoy) return;
            aimingDecoy = false;
            var camera = GetComponentInChildren<Camera>();
            if (camera != null) ThrowDecoyServer(camera.transform.forward);
        }

        private void LateUpdate()
        {
            if (!IsOwner || !aimingDecoy) return;
            var round = NetworkRound.Instance;
            if (round == null || !CanAimDecoy(round, Cursor.lockState == CursorLockMode.Locked))
            { aimingDecoy = false; return; }
            var camera = GetComponentInChildren<Camera>();
            var rings = GetComponentInChildren<WalkingFootRings>();
            if (camera == null || rings == null) return;
            decoyPreview ??= new DecoyTrajectoryPreview();
            decoyPreview.Draw(camera, DecoyBallistics.Origin(transform.position, player.IsCrouching),
                camera.transform.forward, rings.RingShader);
        }

        private void OnApplicationFocus(bool focused) { if (!focused) { aimingDecoy = false; CancelSoundCanCapture(); } }
        private void OnApplicationPause(bool paused) { if (paused) { aimingDecoy = false; CancelSoundCanCapture(); } }
        private void OnDisable() { aimingDecoy = false; decoyPreview?.Dispose(); decoyPreview = null; CancelSoundCanCapture(); }
        private void OnDestroy() { decoyPreview?.Dispose(); decoyPreview = null; StopSoundCanCapture(); }

        // Clicks during the cooldown (or while the last swing is unanswered) send nothing (기획서 3.1-2).
        private void Swing(NetworkRound round)
        {
            if (swingPending || Time.time < nextSwingTime) return;
            swingPending = true;
            SwingServer(round.TryFindTagTarget(transform, round.TagRadius, out _));
        }

        private void BeginInteraction(NetworkRound round, float range)
        {
            if (player.Role == PlayerRole.Runner && player.SoundCanStage == SoundCanStage.None &&
                round.TryFindSoundCanPickup(transform.position, out int soundCanIndex))
            {
                PickupSoundCanServer(soundCanIndex);
            }
            else if (player.Role == PlayerRole.Runner && player.DecoyCount == 0 &&
                round.TryFindDecoyPickup(transform.position, out int decoyIndex))
            {
                PickupDecoyServer(decoyIndex);
            }
            else if (player.Role == PlayerRole.Runner && player.ClickerCount == 0 &&
                round.TryFindClickerPickup(transform.position, round.ClickerInteractRange, out int clickerIndex))
            {
                PickupClickerServer(clickerIndex);
            }
            else if (player.Role == PlayerRole.Runner && round.HasBandageCapacity(player) &&
                round.TryFindBandagePickup(transform.position, range, out int pickupIndex))
            {
                PickupBandageServer(pickupIndex);
            }
            else if (player.Role == PlayerRole.Runner && round.TryFindInjuredRunner(player, range, out _))
            {
                BeginTreatmentServer();
                workingTreatment = true;
            }
            else if (ExitView.AnyInRange(transform.position, range))
            {
                EscapeServer();
            }
            else if (round.TryGetActiveDrainHatch(out var drain) &&
                     DrainHatchView.TryGet(drain, out var view) && view.IsInRange(transform.position, range))
            {
                BeginDrainWorkServer(drain);
                workingDrain = drain;
            }
            else if (TryFindNearestValve(range, out var valve))
            {
                BeginWorkServer(valve);
                workingValve = valve;
            }
        }

        public override void OnStopClient()
        {
            StopSoundCanCapture();
            base.OnStopClient();
            workingValve = null;
            workingDrain = null;
            workingTreatment = false;
            workingBandage = false;
            swingPending = false;
            nextSwingTime = 0;
            swingCooldownDuration = 0;
            aimingDecoy = false;
            decoyPreview?.Dispose();
            decoyPreview = null;
        }

        private bool TryFindNearestValve(float range, out ValveId nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (var view in ValveView.All)
            {
                float distance = (view.transform.position - transform.position).sqrMagnitude;
                if (distance >= best || !view.IsInRange(transform.position, range)) continue;
                best = distance;
                nearest = view.Id;
            }
            return best < float.MaxValue;
        }

        // Any rejection (inactive, already open, not a Runner, out of range) gets the same jam cue, so the
        // reply never tells the Seeker or anyone else which valves are active (기획서 6.1-0, 10-26).
        [ServerRpc]
        private void BeginWorkServer(ValveId valveId)
        {
            var round = NetworkRound.Instance;
            if (round == null || !round.TryBeginWork(OwnerId, valveId, transform.position)) ValveJamTarget(Owner, valveId);
        }

        // Local only: just the presser hears it, and it makes no pulse (기획서 6.1-0).
        [TargetRpc]
        private void ValveJamTarget(NetworkConnection conn, ValveId valveId)
        {
            if (workingValve == valveId) workingValve = null;
            if (ValveView.TryGet(valveId, out var view)) view.PlayJam();
        }

        [ServerRpc]
        private void EndWorkServer(ValveId valveId) => NetworkRound.Instance?.EndWork(OwnerId, valveId);

        [ServerRpc]
        private void BeginDrainWorkServer(DrainHatchId drainHatchId) =>
            NetworkRound.Instance?.TryBeginDrainWork(OwnerId, drainHatchId, transform.position);

        [ServerRpc]
        private void EndDrainWorkServer(DrainHatchId drainHatchId) => NetworkRound.Instance?.EndDrainWork(OwnerId, drainHatchId);

        [ServerRpc]
        private void EscapeServer() => NetworkRound.Instance?.TryEscape(OwnerId, transform.position);

        [ServerRpc]
        private void PickupBandageServer(int pickupIndex) =>
            NetworkRound.Instance?.TryPickupBandage(OwnerId, pickupIndex, transform.position);

        [ServerRpc]
        private void PickupClickerServer(int pickupIndex) =>
            NetworkRound.Instance?.TryPickupClicker(OwnerId, pickupIndex);

        [ServerRpc]
        private void UseClickerServer() => NetworkRound.Instance?.TryUseClicker(OwnerId);

        [ServerRpc]
        private void PickupDecoyServer(int index) => NetworkRound.Instance?.TryPickupDecoy(OwnerId, index);

        [ServerRpc]
        private void ThrowDecoyServer(Vector3 aim) => NetworkRound.Instance?.TryThrowDecoy(OwnerId, aim);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void RequestClickerFlashTest() => ClickerFlashTestServer();

        [ServerRpc]
        private void ClickerFlashTestServer() => NetworkRound.Instance?.TryClickerFlashTest(OwnerId);
#endif

        [ServerRpc]
        private void BeginBandageServer() => NetworkRound.Instance?.TryBeginBandage(OwnerId);

        [ServerRpc]
        private void BeginTreatmentServer() => NetworkRound.Instance?.TryBeginTreatment(OwnerId);

        [ServerRpc]
        private void CancelHealingServer() => NetworkRound.Instance?.CancelHealingAction(OwnerId);

        [ServerRpc]
        private void SwingServer(bool targetInReach)
        {
            var round = NetworkRound.Instance;
            var result = round != null ? round.TrySwing(OwnerId, transform, targetInReach) : SwingResult.Rejected;
            if (result != SwingResult.Rejected) SwingObservers();
            SwingResultTarget(Owner, result);
        }

        // Hit or miss, every client (owner included) plays the accepted swing — the same motion for both.
        [ObserversRpc]
        private void SwingObservers()
        {
            if (TryGetComponent(out CharacterMotion motion)) motion.PlayAttack();
        }

        [TargetRpc]
        private void SwingResultTarget(NetworkConnection conn, SwingResult result)
        {
            swingPending = false;
            var round = NetworkRound.Instance;
            if (round == null || result == SwingResult.Rejected) return;
            swingCooldownDuration = result == SwingResult.Missed ? round.TagMissCooldown : round.TagHitCooldown;
            nextSwingTime = Time.time + swingCooldownDuration;
        }
    }
}
