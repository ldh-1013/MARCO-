using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Marco.Core.WinCondition;
using Marco.Network.Escape;
using Marco.Network.Player;
using Marco.Network.Roles;
using Marco.Network.Valves;
using UnityEngine;

namespace Marco.Network.Round
{
    // ValvesOnly: fewer than 2 Runners (RoundState only supports 2~5), so only board.Tick runs.
    // Escape and tagging still work there, but no result is ever decided.
    public enum RoundMode : byte { NotStarted, ValvesOnly, Full }

    public struct ValveSnapshot
    {
        public bool Active;
        public ValveStatus Status;
        public float Progress;
    }

    // Scene object. The server assembles WinCondition (INTERFACES.md §0), ticks it and validates
    // valve / drain / escape / tag requests; clients only read the synced snapshot.
    public sealed class NetworkRound : NetworkBehaviour
    {
        private const int ValveCount = 5;
        private const float ProgressSyncEpsilon = 0.001f;

        public static NetworkRound Instance { get; private set; }

        [SerializeField] private float interactRange = 2f;
        [SerializeField] private float tagRange = 2f;
        [SerializeField] private float tagCooldownSeconds = 1f;
        // Design value is 90 s; lower it in the inspector to test the phase timeout quickly.
        [SerializeField] private float lastSurvivorPhaseSeconds = 90f;

        // Index = (int)ValveId.
        private readonly SyncList<ValveSnapshot> valveStates = new();
        private readonly SyncVar<RoundMode> mode = new();
        private readonly SyncVar<bool> gateOpen = new();
        private readonly SyncVar<RoundResult> result = new();
        private readonly SyncVar<float> remainingSeconds = new();
        private readonly SyncVar<bool> lastSurvivorPhase = new();
        private readonly SyncVar<float> lastSurvivorRemainingSeconds = new();
        private readonly SyncVar<bool> hasDrainHatch = new();
        private readonly SyncVar<DrainHatchId> activeDrainHatch = new();
        private readonly SyncVar<float> drainHatchProgress = new();

        private readonly Dictionary<int, float> nextTagTimes = new();
        private NetworkPlayerRoster roster;
        private ValveBoard board;
        private Gate gate;
        private ValveInteractionService valves;
        private RoundState round;
        private DrainHatchInteractionService drains;

        public float InteractRange => interactRange;
        public RoundMode Mode => mode.Value;
        public bool GateOpen => gateOpen.Value;
        public RoundResult Result => result.Value;
        public float RemainingSeconds => remainingSeconds.Value;
        public bool InLastSurvivorPhase => lastSurvivorPhase.Value;
        public float LastSurvivorRemainingSeconds => lastSurvivorRemainingSeconds.Value;
        public float DrainHatchProgress => drainHatchProgress.Value;

        public bool TryGetValveState(ValveId id, out ValveSnapshot state)
        {
            int index = (int)id;
            state = index < valveStates.Count ? valveStates[index] : default;
            return index < valveStates.Count;
        }

        public bool TryGetActiveDrainHatch(out DrainHatchId id)
        {
            id = activeDrainHatch.Value;
            return hasDrainHatch.Value;
        }

        private void Awake()
        {
            Instance = this;
            Debug.Log($"[MARCO][Round] Awake (scene object '{name}')");
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            Debug.Log($"[MARCO][Round] OnStartNetwork (ObjectId {ObjectId})");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log($"[MARCO][Round] OnStartClient (mode {mode.Value})");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Scene object survives Leave → Host again, so start every session from a clean state.
            ResetRound();
            ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            Debug.Log("[MARCO][Round] OnStartServer");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            TimeManager.OnTick -= OnServerTick;
            ClearCore();
        }

        [Server]
        public void StartRound()
        {
            Debug.Log($"[MARCO][Round] StartRound (mode {mode.Value})");
            if (mode.Value != RoundMode.NotStarted) return;
            roster = NetworkManager.GetComponent<NetworkPlayerRoster>();
            // Everyone connected now takes part, including last round's spectators.
            foreach (var player in NetworkPlayer.All) player.SetStatus(PlayerStatus.Alive);

            // INTERFACES.md §0 assembly order.
            var config = new WinConditionConfig { LastSurvivorPhaseDurationSeconds = lastSurvivorPhaseSeconds };
            var active = ValveActivation.SelectActiveValves(new System.Random().Next);
            board = new ValveBoard(active, config);
            gate = new Gate(config.RequiredOpenValveCount);
            var events = new RoundEventRelay(this);
            valves = new ValveInteractionService(board, gate, roster, new LogValveSoundSink(), events);
            try
            {
                round = new RoundState(config, roster, gate, valves, events);
                drains = new DrainHatchInteractionService(round.LastSurvivorPhase, roster);
                WatchDrainHatch(DrainHatchId.First);
                WatchDrainHatch(DrainHatchId.Second);
                mode.Value = RoundMode.Full;
            }
            catch (ArgumentOutOfRangeException)
            {
                round = null;
                mode.Value = RoundMode.ValvesOnly;
                Debug.Log($"[MARCO] Runner {roster.TotalRunnerCount}명: RoundState는 2~5명만 지원 → 밸브 전용 모드");
            }

            foreach (var id in active)
            {
                int index = (int)id;
                var state = valveStates[index];
                state.Active = true;
                valveStates[index] = state;
            }
            TimeManager.OnTick += OnServerTick;
        }

        // Host's "다시 시작": throw the finished Core objects away and assemble a fresh round.
        [Server]
        public void RestartRound()
        {
            Debug.Log("[MARCO][Round] RestartRound");
            ResetRound();
            StartRound();
        }

        private void ResetRound()
        {
            TimeManager.OnTick -= OnServerTick;
            ClearCore();
            nextTagTimes.Clear();
            valveStates.Clear();
            for (int i = 0; i < ValveCount; i++) valveStates.Add(default);
            mode.Value = RoundMode.NotStarted;
            gateOpen.Value = false;
            result.Value = RoundResult.InProgress;
            remainingSeconds.Value = 0;
            lastSurvivorPhase.Value = false;
            lastSurvivorRemainingSeconds.Value = 0;
            hasDrainHatch.Value = false;
            drainHatchProgress.Value = 0;
        }

        private void ClearCore()
        {
            board = null;
            gate = null;
            valves = null;
            round = null;
            drains = null;
        }

        // RoundState escapes drain workers internally without naming them, so mirror that into the
        // roster here. RoundState subscribed first (in its constructor), so its handler has already run.
        private void WatchDrainHatch(DrainHatchId id)
        {
            var hatch = round.LastSurvivorPhase.GetDrainHatch(id);
            hatch.StatusChanged += status =>
            {
                if (status != DrainHatchStatus.Completed) return;
                foreach (int playerId in hatch.WorkerIds)
                    if (TryGetAliveRunner(playerId, out var player)) player.SetStatus(PlayerStatus.Escaped);
            };
        }

        // §0 tick order: board first, then round.
        private void OnServerTick()
        {
            float dt = (float)TimeManager.TickDelta;
            board.Tick(dt);
            round?.Tick(dt);

            foreach (var pair in board.Valves)
            {
                int index = (int)pair.Key;
                var current = valveStates[index];
                if (current.Status == pair.Value.Status && Mathf.Abs(current.Progress - pair.Value.Progress) < ProgressSyncEpsilon)
                    continue;
                current.Status = pair.Value.Status;
                current.Progress = pair.Value.Progress;
                valveStates[index] = current;
            }

            if (round == null) return;
            remainingSeconds.Value = round.RemainingSeconds;
            SyncLastSurvivorPhase(round.LastSurvivorPhase);
            if (round.Result != RoundResult.InProgress) TimeManager.OnTick -= OnServerTick;
        }

        private void SyncLastSurvivorPhase(LastSurvivorPhase phase)
        {
            if (!phase.HasEntered) return;
            lastSurvivorRemainingSeconds.Value = Mathf.Max(0, phase.RemainingSeconds);
            if (phase.ActiveDrainHatchId is not DrainHatchId id) return;
            float progress = phase.GetDrainHatch(id).Progress;
            if (Mathf.Abs(drainHatchProgress.Value - progress) >= ProgressSyncEpsilon) drainHatchProgress.Value = progress;
        }

        // Role is checked inside ValveInteractionService via IPlayerRoster; distance is checked here
        // against the server's copy of the player transform (client-authoritative movement for now).
        [Server]
        public bool TryBeginWork(int playerId, ValveId id, Vector3 playerPosition)
        {
            if (valves == null || !ValveView.TryGet(id, out var view)) return false;
            if (!view.IsInRange(playerPosition, interactRange)) return false;
            return valves.TryBeginWork(playerId, id);
        }

        [Server]
        public void EndWork(int playerId, ValveId id) => valves?.EndWork(playerId, id);

        // Role and alive state are checked inside DrainHatchInteractionService via IPlayerRoster.
        [Server]
        public bool TryBeginDrainWork(int playerId, DrainHatchId id, Vector3 playerPosition)
        {
            if (drains == null || !DrainHatchView.TryGet(id, out var view)) return false;
            if (!view.IsInRange(playerPosition, interactRange)) return false;
            return drains.TryBeginWork(playerId, id);
        }

        [Server]
        public void EndDrainWork(int playerId, DrainHatchId id) => drains?.EndWork(playerId, id);

        // Gate state is checked by RoundState; in ValvesOnly mode there is no RoundState, so here.
        [Server]
        public bool TryEscape(int playerId, Vector3 playerPosition)
        {
            if (valves == null || !TryGetAliveRunner(playerId, out var player)) return false;
            if (!ExitView.AnyInRange(playerPosition, interactRange)) return false;
            if (round != null ? !round.NotifyRunnerEscaped(playerId) : !gate.IsOpen) return false;

            RemoveFromWork(playerId);
            player.SetStatus(PlayerStatus.Escaped);
            Debug.Log($"[MARCO] P{playerId} escaped");
            return true;
        }

        // The client only says "I swung"; the server picks the nearest alive Runner in range.
        [Server]
        public bool TryTag(int seekerId, Vector3 seekerPosition)
        {
            if (valves == null || roster.GetRole(seekerId) != PlayerRole.Seeker) return false;
            if (nextTagTimes.TryGetValue(seekerId, out float next) && Time.time < next) return false;
            nextTagTimes[seekerId] = Time.time + tagCooldownSeconds;

            if (!TryFindTagTarget(seekerPosition, out var target)) return false;
            if (round != null && !round.NotifyRunnerTagged(target.PlayerId)) return false;

            RemoveFromWork(target.PlayerId);
            target.SetStatus(PlayerStatus.Tagged);
            Debug.Log($"[MARCO] P{seekerId} tagged P{target.PlayerId}");
            return true;
        }

        private bool TryFindTagTarget(Vector3 seekerPosition, out NetworkPlayer target)
        {
            target = null;
            float best = tagRange * tagRange;
            foreach (var player in NetworkPlayer.All)
            {
                if (player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive) continue;
                var offset = player.transform.position - seekerPosition;
                offset.y = 0;
                if (offset.sqrMagnitude > best) continue;
                best = offset.sqrMagnitude;
                target = player;
            }
            return target != null;
        }

        private bool TryGetAliveRunner(int playerId, out NetworkPlayer player) =>
            NetworkPlayer.TryGet(playerId, out player) && player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive;

        // RoundState already drops tagged/escaped players from valves, but not from the drain hatch,
        // and ValvesOnly mode has no RoundState at all.
        private void RemoveFromWork(int playerId)
        {
            valves.RemovePlayerFromAllValves(playerId);
            if (round?.LastSurvivorPhase.ActiveDrainHatchId is DrainHatchId id) drains.EndWork(playerId, id);
        }

        // Raised before FishNet despawns the leaver's objects, so the roster still knows their role.
        private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped || valves == null) return;
            nextTagTimes.Remove(conn.ClientId);
            RemoveFromWork(conn.ClientId);
            // A spectator never entered RoundState's count; reporting them would count as a tag.
            bool spectating = NetworkPlayer.TryGet(conn.ClientId, out var player) && player.Status == PlayerStatus.Spectating;
            if (round != null && !spectating) round.NotifyPlayerLeft(conn.ClientId);
        }

        [Server]
        internal void MarkGateOpened() => gateOpen.Value = true;

        [Server]
        internal void SetResult(RoundResult value) => result.Value = value;

        [Server]
        internal void MarkLastSurvivorPhase() => lastSurvivorPhase.Value = true;

        [Server]
        internal void MarkDrainHatchActivated(DrainHatchId id)
        {
            activeDrainHatch.Value = id;
            hasDrainHatch.Value = true;
        }
    }
}
