using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Marco.Core.WinCondition;
using Marco.Network.Roles;
using Marco.Network.Valves;
using UnityEngine;

namespace Marco.Network.Round
{
    // ValvesOnly: fewer than 2 Runners (RoundState only supports 2~5), so only board.Tick runs.
    public enum RoundMode : byte { NotStarted, ValvesOnly, Full }

    public struct ValveSnapshot
    {
        public bool Active;
        public ValveStatus Status;
        public float Progress;
    }

    // Scene object. The server assembles WinCondition (INTERFACES.md §0), ticks it and validates
    // valve requests; clients only read the synced snapshot.
    public sealed class NetworkRound : NetworkBehaviour
    {
        private const int ValveCount = 5;
        private const float ProgressSyncEpsilon = 0.001f;

        public static NetworkRound Instance { get; private set; }

        [SerializeField] private float interactRange = 2f;

        // Index = (int)ValveId.
        private readonly SyncList<ValveSnapshot> valveStates = new();
        private readonly SyncVar<RoundMode> mode = new();
        private readonly SyncVar<bool> gateOpen = new();
        private readonly SyncVar<RoundResult> result = new();
        private readonly SyncVar<float> remainingSeconds = new();

        private ValveBoard board;
        private ValveInteractionService valves;
        private RoundState round;

        public float InteractRange => interactRange;
        public RoundMode Mode => mode.Value;
        public bool GateOpen => gateOpen.Value;
        public RoundResult Result => result.Value;
        public float RemainingSeconds => remainingSeconds.Value;

        public bool TryGetValveState(ValveId id, out ValveSnapshot state)
        {
            int index = (int)id;
            state = index < valveStates.Count ? valveStates[index] : default;
            return index < valveStates.Count;
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
            valveStates.Clear();
            for (int i = 0; i < ValveCount; i++) valveStates.Add(default);
            mode.Value = RoundMode.NotStarted;
            gateOpen.Value = false;
            result.Value = RoundResult.InProgress;
            remainingSeconds.Value = 0;
            ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            Debug.Log("[MARCO][Round] OnStartServer");
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            TimeManager.OnTick -= OnServerTick;
            board = null;
            valves = null;
            round = null;
        }

        [Server]
        public void StartRound()
        {
            Debug.Log($"[MARCO][Round] StartRound (mode {mode.Value})");
            if (mode.Value != RoundMode.NotStarted) return;
            var roster = NetworkManager.GetComponent<NetworkPlayerRoster>();

            // INTERFACES.md §0 assembly order.
            var config = new WinConditionConfig();
            var active = ValveActivation.SelectActiveValves(new System.Random().Next);
            board = new ValveBoard(active, config);
            var gate = new Gate(config.RequiredOpenValveCount);
            var events = new RoundEventRelay(this);
            valves = new ValveInteractionService(board, gate, roster, new LogValveSoundSink(), events);
            try
            {
                round = new RoundState(config, roster, gate, valves, events);
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
            if (round.Result != RoundResult.InProgress) TimeManager.OnTick -= OnServerTick;
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

        [Server]
        internal void MarkGateOpened() => gateOpen.Value = true;

        [Server]
        internal void SetResult(RoundResult value) => result.Value = value;

        // Raised before FishNet despawns the leaver's objects, so the roster still knows their role.
        private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            if (round != null) round.NotifyPlayerLeft(conn.ClientId);
            else valves?.RemovePlayerFromAllValves(conn.ClientId);
        }
    }
}
