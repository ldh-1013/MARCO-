using FishNet.Connection;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using Marco.Network.Escape;
using Marco.Network.Round;
using Marco.Network.Valves;
using UnityEngine;

namespace Marco.Network.Player
{
    // On the player prefab. The owner's input becomes ServerRpcs; the server re-checks range, role
    // and status (NetworkRound / WinCondition via IPlayerRoster). Clients decide nothing.
    // E: Runner pickup/treatment, exit, drain hatch or valve. G starts a bandage channel.
    // Left click: swing (Seeker); the server decides the tag and all clients play the attack.
    [RequireComponent(typeof(NetworkPlayer))]
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        private NetworkPlayer player;
        private ValveId? workingValve;
        private DrainHatchId? workingDrain;
        private bool workingTreatment;
        private bool workingBandage;
        // Owner-side swing cooldown. It starts when the server's answer arrives, so it always ends
        // after the server's own cooldown and never sends a request the server would reject.
        private bool swingPending;
        private float nextSwingTime;

        // Seeker only: seconds left on the swing cooldown, for the HUD.
        public float SwingCooldownRemaining => Mathf.Max(0, nextSwingTime - Time.time);

        public bool CanTreatNearbyRunner
        {
            get
            {
                var round = NetworkRound.Instance;
                return IsOwner && player != null && player.Role == PlayerRole.Runner &&
                    player.Status == PlayerStatus.Alive && !player.HasHealingAction && round != null &&
                    (!round.HasBandageCapacity(player) || !round.TryFindBandagePickup(transform.position, round.InteractRange, out _)) &&
                    round.TryFindInjuredRunner(player, round.InteractRange, out _);
            }
        }

        private void Awake() => player = GetComponent<NetworkPlayer>();

        private void Update()
        {
            if (!IsOwner) return;
            var round = NetworkRound.Instance;
            if (round == null) return;

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            bool held = Input.GetKey(KeyCode.E) && locked;
            float range = round.InteractRange;

            if (workingBandage)
            {
                if (!Input.GetKey(KeyCode.G) || !locked)
                {
                    CancelHealingServer();
                    workingBandage = false;
                }
            }
            else if (locked && Input.GetKeyDown(KeyCode.G) && !workingTreatment &&
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
                if (!held || !ValveView.TryGet(valve, out var view) || !view.IsInRange(transform.position, range))
                {
                    EndWorkServer(valve);
                    workingValve = null;
                }
            }
            else if (workingDrain is DrainHatchId drain)
            {
                if (!held || !DrainHatchView.TryGet(drain, out var view) || !view.IsInRange(transform.position, range))
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
            else if (Input.GetKeyDown(KeyCode.E) && held && player.Status == PlayerStatus.Alive)
            {
                // Only on a fresh press: an opened valve drops its workers, and INTERFACES.md §4
                // expects a new TryBeginWork (release and press again) to rework it.
                BeginInteraction(round, range);
            }

            if (locked && Input.GetMouseButtonDown(0) && player.Role == PlayerRole.Seeker) Swing(round);
        }

        // Clicks during the cooldown (or while the last swing is unanswered) send nothing (기획서 3.1-2).
        private void Swing(NetworkRound round)
        {
            if (swingPending || Time.time < nextSwingTime) return;
            swingPending = true;
            SwingServer(round.TryFindTagTarget(transform, round.TagRadius, out _));
        }

        private void BeginInteraction(NetworkRound round, float range)
        {
            if (player.Role == PlayerRole.Runner && round.HasBandageCapacity(player) &&
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
            base.OnStopClient();
            workingValve = null;
            workingDrain = null;
            workingTreatment = false;
            workingBandage = false;
            swingPending = false;
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

        [ServerRpc]
        private void BeginWorkServer(ValveId valveId) =>
            NetworkRound.Instance?.TryBeginWork(OwnerId, valveId, transform.position);

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
            nextSwingTime = Time.time + (result == SwingResult.Missed ? round.TagMissCooldown : round.TagHitCooldown);
        }
    }
}
