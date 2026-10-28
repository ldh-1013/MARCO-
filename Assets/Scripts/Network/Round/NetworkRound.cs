using System;
using System.Collections.Generic;
using FishNet.Component.Spawning;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Marco.Network.Character;
using Marco.Core.WinCondition;
using Marco.Network.Escape;
using Marco.Network.Map;
using Marco.Network.Player;
using Marco.Network.Roles;
using Marco.Network.Valves;
using Marco.Network.Vision;
using UnityEngine;
using UnityEngine.Serialization;

namespace Marco.Network.Round
{
    // Rejected: not the Seeker, no round, or still cooling down (no swing is played).
    // Wounded: first hit at full lives (2.0 → 1.0). Echoed: hit below full lives → Echo (기획서 3.3-1).
    public enum SwingResult : byte { Rejected, Missed, Wounded, Echoed }

    // ValvesOnly: fewer than 2 Runners (RoundState only supports 2~5), so only board.Tick runs.
    // Escape and tagging still work there, but no result is ever decided.
    public enum RoundMode : byte { NotStarted, ValvesOnly, Full }

    // No "active" flag: the active set stays on the server (기획서 6.1-0 · 14.3 ValveActivated, 10-26).
    // Inactive valves simply stay Closed at 0, like an untouched active one.
    public struct ValveSnapshot
    {
        public ValveStatus Status;
        public float Progress;
    }

    // Scene object. The server assembles WinCondition (INTERFACES.md §0), ticks it and validates
    // valve / drain / escape / tag requests; clients only read the synced snapshot.
    public sealed class NetworkRound : NetworkBehaviour
    {
        private const int ValveCount = 5;
        private const float ProgressSyncEpsilon = 0.001f;
        private const float BandageUseSeconds = 6f;
        private const float TeammateTreatmentSeconds = 9f;
        private const float HealingProgressSyncSeconds = 0.1f;
        private const float ClickerRespawnSeconds = 120f;
        private const float ClickerVisibilityRadius = 6f;
        private const float ClickerStunRadius = 16f;
        private const float ClickerStunSeconds = 3f;
        private const float ClickerPickupRange = 1.5f;
        // One each in the four diagram regions: lobby, 2F lobby, dock, main pool hall.
        private static readonly Vector3[] ClickerSpawnPoints =
        {
            new(44.5f, 0f, 2.5f),  // 로비 · 안내데스크
            new(33f, 3.5f, 18f),   // 2층 관람 로비
            new(50f, 0f, 60f),     // 하역장
            new(1f, 0f, 58f),      // 메인 풀 홀 북서 모서리
        };
        // Map coordinates (x, height, z) from docs/맵설계.md §9.1. The nine fixed pickups
        // match the map diagram: AID 2, main pool hall 2, staff corridor 1, both changing rooms 1,
        // lobby 1, and second-floor spectator lobby 1.
        private static readonly Vector3[] BandageSpawnPoints =
        {
            new(2f, 0f, 21.5f),   // 구조 · 응급실 남쪽
            new(2f, 0f, 29f),     // 구조 · 응급실 북쪽
            new(1f, 0f, 52f),     // 메인 풀 홀 서측
            new(29f, 0f, 60.5f),  // 메인 풀 홀 북측
            new(49f, 0f, 44.5f),  // 직원 통로
            new(16.5f, 0f, 16f),  // 남 탈의실
            new(38.5f, 0f, 16f),  // 여 탈의실
            new(37f, 0f, 6.5f),   // 로비
            new(44f, 3.5f, 24f),  // 2층 관람 로비
        };

        private sealed class HealingChannel
        {
            public int ActorId;
            public int PatientId;
            public bool IsBandage;
            public float Duration;
            public float Elapsed;
            public double StartedAt;
            public double SyncAt;
            public Vector3 ActorStart;
            public Vector3 PatientStart;
            public bool PatientWasCrouching;
        }

        public static NetworkRound Instance { get; private set; }

        [SerializeField] private float interactRange = 2f;
        [Header("Tag (기획서 3.1-2 · 부록 A [태그 판정])")]
        // The Seeker's client only requests a tag when a Runner is this close (3D distance).
        [SerializeField] private float tagRadius = 1.2f;
        // Server re-check limit: latency allowance (sprint 7.5 m/s × 100 ms RTT), not the tag radius.
        [FormerlySerializedAs("tagRange")]
        [SerializeField] private float tagServerMaxDistance = 2f;
        // Half of the horizontal cone in front of the Seeker.
        [SerializeField] private float tagHalfAngle = 45f;
        [SerializeField] private float tagHitCooldown = 2.5f;
        [SerializeField] private float tagMissCooldown = 1.5f;

        [Header("Runner lives (기획서 3.3-1 · 부록 A [도망자 체력])")]
        // Lives move in 0.5 steps and travel as "halves" (2.0 = 4) to avoid float comparisons (14.3).
        [SerializeField] private float startLives = 2f;
        [SerializeField] private float woundedLives = 1f;
        // Recovering (between wounded and full, i.e. 1.5) moves this much slower.
        [SerializeField, Range(0, 0.24f)] private float recoveringSpeedPenalty = 0.15f;
        // Design value is 90 s; lower it in the inspector to test the phase timeout quickly.
        [SerializeField] private float lastSurvivorPhaseSeconds = 90f;
        // GamePoints marker the Seeker is moved to when a round starts.
        [SerializeField] private string seekerSpawnPoint = "SpawnPoint_4";
        // Valve reaching Open: map-wide 2D for everyone, no position, no pulse (기획서 6.1 · 부록 A [밸브], 10-26).
        // Asset not made yet — while empty the cue is only logged.
        [SerializeField] private AudioClip valveOpenedClip;
        // Sudden death low drone from 2:00 left (기획서 6.2-1): presentation only, each client plays it from the
        // synced remaining time. P4 owns the real cue (14.2) — while empty it is only logged.
        [SerializeField] private AudioClip suddenDeathDroneClip;

        // Index = (int)ValveId.
        private readonly SyncList<ValveSnapshot> valveStates = new();
        private readonly SyncList<bool> bandagePickupStates = new();
        private readonly SyncList<bool> clickerPickupStates = new();
        private readonly SyncVar<RoundMode> mode = new();
        private readonly SyncVar<bool> gateOpen = new();
        private readonly SyncVar<RoundResult> result = new();
        private readonly SyncVar<float> remainingSeconds = new();
        private readonly SyncVar<bool> lastSurvivorPhase = new();
        private readonly SyncVar<float> lastSurvivorRemainingSeconds = new();
        private readonly SyncVar<bool> hasDrainHatch = new();
        private readonly SyncVar<DrainHatchId> activeDrainHatch = new();
        private readonly SyncVar<float> drainHatchProgress = new();
        // Set once at 1:00 left and kept until the round is reset (기획서 6.2-1).
        private readonly SyncVar<bool> seekerSpeedBoosted = new();

        private readonly Dictionary<int, float> nextTagTimes = new();
        private readonly Dictionary<int, HealingChannel> healingChannels = new();
        private readonly List<int> healingChannelIds = new();
        private readonly RaycastHit[] treatmentPathHits = new RaycastHit[32];
        // Server only (기획서 14.2): never in a SyncVar, sent to the owner alone. Missing = full.
        private readonly Dictionary<int, int> runnerHalves = new();
        private readonly Dictionary<int, int> bandageCounts = new();
        private readonly Dictionary<int, bool> clickerHoldings = new();
        private readonly Dictionary<int, float> clickerRespawnTimes = new();
        private readonly Dictionary<int, float> clickerStunUntil = new();
        private readonly List<int> expiredClickerStunIds = new();
        private readonly List<Vector3> bandagePositions = new();
        private readonly HashSet<int> resolvedBandageSpawns = new();
        private readonly List<Transform> bandageViews = new();
        private readonly List<Transform> clickerViews = new();
        private Material bandageWrapMaterial, bandageMarkMaterial;
        private Material clickerMaterial;
        private bool clientStarted;
        private NetworkPlayerRoster roster;
        private ValveBoard board;
        private Gate gate;
        private ValveInteractionService valves;
        private RoundState round;
        private DrainHatchInteractionService drains;
        private AudioSource valveOpenedSource;
        private AudioSource suddenDeathDroneSource;
        private bool suddenDeathDronePlaying;
        private static readonly WinConditionConfig DefaultConfig = new();
        private static readonly int RequiredOpenValves = DefaultConfig.RequiredOpenValveCount;

        public float InteractRange => interactRange;
        public float ClickerInteractRange => ClickerPickupRange;
        public float TagRadius => tagRadius;
        public float TagHitCooldown => tagHitCooldown;
        public float TagMissCooldown => tagMissCooldown;
        public bool HasBandageCapacity(NetworkPlayer player) => player != null && player.BandageCount < MaxBandages;
        public int FullHalves => Mathf.RoundToInt(startLives * 2);
        private int WoundedHalves => Mathf.RoundToInt(woundedLives * 2);

        // Owner side: movement multiplier for the lives the server last sent (1.5 → −15%).
        public float SpeedMultiplierFor(int halves) =>
            halves > WoundedHalves && halves < FullHalves ? 1 - recoveringSpeedPenalty : 1;

        // Owner side: sudden death +5% for the Seeker once the server has applied it (기획서 6.2-1, multiplied).
        public float SeekerSpeedMultiplierFor(PlayerRole role) =>
            role == PlayerRole.Seeker && seekerSpeedBoosted.Value ? DefaultConfig.SuddenDeathSeekerSpeedMultiplier : 1;
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

        // What clients may know: how many valves are open, never which ones are active (기획서 12.4 · 6.1-0).
        public int OpenValveCount
        {
            get
            {
                int count = 0;
                foreach (var state in valveStates)
                    if (state.Status == ValveStatus.Open) count++;
                return count;
            }
        }

        public int RequiredOpenValveCount => RequiredOpenValves;

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
            clientStarted = true;
            EnsureBandageViews();
            EnsureClickerViews();
            Debug.Log($"[MARCO][Round] OnStartClient (mode {mode.Value})");
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            clientStarted = false;
            suddenDeathDronePlaying = false;
            if (suddenDeathDroneSource != null) suddenDeathDroneSource.Stop();
            ClearBandageViews();
            ClearClickerViews();
        }

        private void Update()
        {
            if (IsServerInitialized) TickHealingChannels();
            if (IsServerInitialized) TickClickerRespawns();
            if (IsServerInitialized) TickClickerStuns();
            // The map's GamePoints can finish registering after the network scene object starts.
            // Retry on the server and make any late-resolved pickups available for this round.
            if (IsServerInitialized && mode.Value != RoundMode.NotStarted)
            {
                ResolveBandagePositions();
                while (bandagePickupStates.Count < bandagePositions.Count)
                    bandagePickupStates.Add(true);
            }
            if (!clientStarted) return;
            UpdateSuddenDeathDrone();
            EnsureBandageViews();
            EnsureClickerViews();
            for (int i = 0; i < bandageViews.Count; i++)
            {
                var view = bandageViews[i];
                if (view == null) continue;
                bool available = i < bandagePickupStates.Count && bandagePickupStates[i];
                if (view.gameObject.activeSelf != available) view.gameObject.SetActive(available);
                if (!available) continue;
                view.Rotate(Vector3.up, 55f * Time.deltaTime, Space.World);
                Vector3 position = bandagePositions[i];
                position.y += 0.05f * Mathf.Sin(Time.time * 2.5f + i);
                view.position = position;
            }
            for (int i = 0; i < clickerViews.Count; i++)
            {
                Transform view = clickerViews[i];
                if (view == null) continue;
                bool available = i < clickerPickupStates.Count && clickerPickupStates[i];
                if (view.gameObject.activeSelf != available) view.gameObject.SetActive(available);
                if (!available) continue;
                view.Rotate(Vector3.up, 45f * Time.deltaTime, Space.World);
                Vector3 position = ClickerSpawnPoints[i];
                position.y += 0.12f + 0.05f * Mathf.Sin(Time.time * 2.5f + i);
                view.position = position;
            }
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
            CancelAllHealingActions();
            ClearClickerStuns();
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
            // The Seeker is drawn once per round, before RoundState reads the Runner count.
            if (NetworkManager.TryGetComponent(out RoleAssigner roles)) roles.AssignRoles();
            // Everyone connected now takes part, including last round's spectators.
            foreach (var player in NetworkPlayer.All)
            {
                player.SetStatus(PlayerStatus.Alive);
                player.SetCrouching(false);
                player.SetTreatmentLocked(false);
            }
            MovePlayersToSpawn();
            foreach (var player in NetworkPlayer.All)
            {
                bandageCounts[player.PlayerId] = 0;
                player.SendBandageCount(0);
                clickerHoldings[player.PlayerId] = false;
                player.SendClickerCount(0);
            }
            ResetBandagePickups(available: true);
            ResetClickerPickups(available: true);

            // INTERFACES.md §0 assembly order.
            var config = new WinConditionConfig { LastSurvivorPhaseDurationSeconds = lastSurvivorPhaseSeconds };
            // Server only: the active set is never written to a SyncVar or sent to clients (기획서 14.3).
            var active = ValveActivation.SelectActiveValves(new System.Random().Next, config.ActiveValveCount);
            board = new ValveBoard(active, config);
            gate = new Gate(config.RequiredOpenValveCount);
            var events = new RoundEventRelay(this);
            valves = new ValveInteractionService(board, gate, roster, new LogValveSoundSink(), events);
            try
            {
                round = new RoundState(config, roster, gate, valves, events);
                round.SuddenDeath.SeekerSpeedBoosted += () => seekerSpeedBoosted.Value = true;
                round.SuddenDeath.ExitPulseTriggered += EmitExitPulses;
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

            TimeManager.OnTick += OnServerTick;
        }

        // Every round starts from the spawns, so last round's Echoes are not left inside a wall:
        // the Seeker at seekerSpawnPoint, everyone else spread over the PlayerSpawner's spawns.
        // Scenes without GamePoints (NetworkTest) have no Seeker marker and leave the Seeker in place.
        private void MovePlayersToSpawn()
        {
            bool hasSeekerPoint = GamePoints.TryGet(seekerSpawnPoint, out var seekerPoint);
            var spawns = NetworkManager.TryGetComponent(out PlayerSpawner spawner) ? spawner.Spawns : null;
            int next = 0;
            foreach (var player in NetworkPlayer.All)
            {
                if (player.Role == PlayerRole.Seeker)
                {
                    if (hasSeekerPoint) player.TeleportOwner(seekerPoint.position + Vector3.up * GamePoints.PlayerStandHeight);
                }
                else if (spawns != null && spawns.Length > 0)
                {
                    var spawn = spawns[next++ % spawns.Length];
                    if (spawn != null) player.TeleportOwner(spawn.position);
                }
            }
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
            CancelAllHealingActions();
            ClearClickerStuns();
            TimeManager.OnTick -= OnServerTick;
            ClearCore();
            nextTagTimes.Clear();
            ResetLives();
            foreach (var player in NetworkPlayer.All) player.SendBandageCount(0);
            bandageCounts.Clear();
            foreach (var player in NetworkPlayer.All) player.SendClickerCount(0);
            clickerHoldings.Clear();
            ResetClickerPickups(available: false);
            bandagePickupStates.Clear();
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
            seekerSpeedBoosted.Value = false;
        }

        // Both exits, Shout grade 22 m, every 5 s from 0:30 left (기획서 6.2-1). Server-side world-origin
        // pulses go through the pulse delivery path (14.2), which the sound/ripple part (남경) has not
        // built yet — like LogValveSoundSink, only log here and connect it when that path exists.
        private void EmitExitPulses()
        {
            foreach (var exit in ExitView.All)
                Debug.Log($"[MARCO] 종반 출구 파문 {exit.Id} @ {exit.transform.position} (고함급 22m, 전달 경로 미구현)");
        }

        // Client side, from the synced remaining time: on at 2:00 left, off when the round ends or resets.
        private void UpdateSuddenDeathDrone()
        {
            float remaining = remainingSeconds.Value;
            bool shouldPlay = mode.Value == RoundMode.Full && result.Value == RoundResult.InProgress &&
                              remaining > 0f && remaining <= DefaultConfig.SuddenDeathDroneRemainingSeconds;
            if (shouldPlay == suddenDeathDronePlaying) return;
            suddenDeathDronePlaying = shouldPlay;

            if (suddenDeathDroneClip == null)
            {
                Debug.Log(shouldPlay ? "[MARCO] 종반 저역 드론 시작 (에셋 없음)" : "[MARCO] 종반 저역 드론 끝");
                return;
            }
            if (suddenDeathDroneSource == null)
            {
                suddenDeathDroneSource = gameObject.AddComponent<AudioSource>();
                suddenDeathDroneSource.playOnAwake = false;
                suddenDeathDroneSource.loop = true;
                suddenDeathDroneSource.spatialBlend = 0f; // map-wide ambience
                suddenDeathDroneSource.clip = suddenDeathDroneClip;
            }
            if (shouldPlay) suddenDeathDroneSource.Play();
            else suddenDeathDroneSource.Stop();
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

        private void OnServerTick() => Step((float)TimeManager.TickDelta);

        // §0 tick order: board first, then round.
        private void Step(float dt)
        {
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
            if (!ApplyEscape(player)) return false;

            Debug.Log($"[MARCO] P{playerId} escaped");
            return true;
        }

        private bool ApplyEscape(NetworkPlayer player)
        {
            if (round != null ? !round.NotifyRunnerEscaped(player.PlayerId) : !gate.IsOpen) return false;

            ClearClickerHolding(player);
            RemoveFromWork(player.PlayerId);
            player.SetStatus(PlayerStatus.Escaped);
            return true;
        }

        // Local targeting hint; the server repeats role, status, distance and availability checks.
        public bool TryFindBandagePickup(Vector3 playerPosition, float range, out int pickupIndex)
        {
            ResolveBandagePositions();
            pickupIndex = -1;
            float best = range * range;
            int count = Mathf.Min(bandagePositions.Count, bandagePickupStates.Count);
            for (int i = 0; i < count; i++)
            {
                if (!bandagePickupStates[i]) continue;
                float distance = (bandagePositions[i] - playerPosition).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                pickupIndex = i;
            }
            return pickupIndex >= 0;
        }

        public bool TryFindClickerPickup(Vector3 playerPosition, float range, out int pickupIndex)
        {
            ResolveBandagePositions();
            pickupIndex = -1;
            float best = range * range;
            int count = Mathf.Min(ClickerSpawnPoints.Length, clickerPickupStates.Count);
            for (int i = 0; i < count; i++)
            {
                if (!clickerPickupStates[i] || !HasClickerBandageClearance(i)) continue;
                float distance = (ClickerSpawnPoints[i] - playerPosition).sqrMagnitude;
                if (distance > best) continue;
                best = distance;
                pickupIndex = i;
            }
            return pickupIndex >= 0;
        }

        private bool HasClickerBandageClearance(int clickerIndex)
        {
            if (clickerIndex < 0 || clickerIndex >= ClickerSpawnPoints.Length) return false;
            ResolveBandagePositions();
            // Keep the pickup areas apart even if the bandage range is tuned in the Inspector.
            float minimumDistance = Mathf.Max(0f, interactRange) + ClickerPickupRange;
            float minimumDistanceSquared = minimumDistance * minimumDistance;
            foreach (Vector3 bandagePosition in bandagePositions)
                if ((ClickerSpawnPoints[clickerIndex] - bandagePosition).sqrMagnitude < minimumDistanceSquared)
                    return false;
            return true;
        }

        // Select the closest injured Runner so E can request treatment. Health stays private;
        // the injury flag only reveals that help is needed, not the remaining health amount.
        public bool TryFindInjuredRunner(NetworkPlayer healer, float range, out NetworkPlayer target)
        {
            target = null;
            if (healer == null) return false;
            float best = range * range;
            foreach (var player in NetworkPlayer.All)
            {
                if (player == healer || player.Role != PlayerRole.Runner ||
                    player.Status != PlayerStatus.Alive || !player.NeedsTreatment || player.IsTreatmentLocked) continue;
                float distance = (player.transform.position - healer.transform.position).sqrMagnitude;
                if (distance > best || !HasClearTreatmentPath(healer, player)) continue;
                best = distance;
                target = player;
            }
            return target != null;
        }

        [Server]
        public bool TryPickupBandage(int playerId, int pickupIndex, Vector3 playerPosition)
        {
            if (valves == null || result.Value != RoundResult.InProgress || !TryGetAliveRunner(playerId, out var player)) return false;
            ResolveBandagePositions();
            if (pickupIndex < 0 || pickupIndex >= bandagePositions.Count || pickupIndex >= bandagePickupStates.Count ||
                !bandagePickupStates[pickupIndex] ||
                (bandagePositions[pickupIndex] - playerPosition).sqrMagnitude > interactRange * interactRange)
                return false;

            int count = bandageCounts.TryGetValue(playerId, out int stored) ? stored : 0;
            if (count >= MaxBandages) return false;
            bandagePickupStates[pickupIndex] = false;
            bandageCounts[playerId] = ++count;
            player.SendBandageCount(count);
            return true;
        }

        [Server]
        public bool TryPickupClicker(int playerId, int pickupIndex)
        {
            if (valves == null || result.Value != RoundResult.InProgress ||
                !TryGetAliveRunner(playerId, out var player) ||
                pickupIndex < 0 || pickupIndex >= ClickerSpawnPoints.Length || pickupIndex >= clickerPickupStates.Count ||
                !clickerPickupStates[pickupIndex] ||
                !HasClickerBandageClearance(pickupIndex) ||
                (ClickerSpawnPoints[pickupIndex] - player.transform.position).sqrMagnitude > ClickerPickupRange * ClickerPickupRange ||
                (clickerHoldings.TryGetValue(playerId, out bool alreadyHolding) && alreadyHolding))
                return false;

            clickerPickupStates[pickupIndex] = false;
            clickerRespawnTimes[pickupIndex] = Time.time + ClickerRespawnSeconds;
            clickerHoldings[playerId] = true;
            player.SendClickerCount(1);
            return true;
        }

        [Server]
        public bool TryUseClicker(int playerId)
        {
            if (valves == null || result.Value != RoundResult.InProgress ||
                !TryGetAliveRunner(playerId, out var source) ||
                !clickerHoldings.TryGetValue(playerId, out bool isHolding) || !isHolding)
                return false;

            clickerHoldings[playerId] = false;
            source.SendClickerCount(0);
            foreach (NetworkPlayer listener in NetworkPlayer.All)
            {
                if (listener == null || listener.Status == PlayerStatus.Escaped || listener.Status == PlayerStatus.Spectating ||
                    (listener.transform.position - source.transform.position).sqrMagnitude > ClickerVisibilityRadius * ClickerVisibilityRadius ||
                    !HasClearClickerPath(source, listener))
                    continue;
                ClickerFlashTarget(listener.Owner);
            }

            float stunRadiusSquared = ClickerStunRadius * ClickerStunRadius;
            foreach (NetworkPlayer listener in NetworkPlayer.All)
            {
                if (listener == null || listener.Role != PlayerRole.Seeker || listener.Status != PlayerStatus.Alive ||
                    (listener.transform.position - source.transform.position).sqrMagnitude > stunRadiusSquared)
                    continue;

                float stunUntil = Time.time + ClickerStunSeconds;
                if (clickerStunUntil.TryGetValue(listener.PlayerId, out float existingUntil))
                    stunUntil = Mathf.Max(stunUntil, existingUntil);
                clickerStunUntil[listener.PlayerId] = stunUntil;
                listener.SetStunned(true);
            }
            return true;
        }

        private void TickClickerStuns()
        {
            expiredClickerStunIds.Clear();
            foreach (KeyValuePair<int, float> stun in clickerStunUntil)
            {
                if (Time.time < stun.Value) continue;
                if (NetworkPlayer.TryGet(stun.Key, out NetworkPlayer player)) player.SetStunned(false);
                expiredClickerStunIds.Add(stun.Key);
            }

            foreach (int playerId in expiredClickerStunIds) clickerStunUntil.Remove(playerId);
        }

        private void ClearClickerStuns()
        {
            foreach (int playerId in clickerStunUntil.Keys)
                if (NetworkPlayer.TryGet(playerId, out NetworkPlayer player)) player.SetStunned(false);
            clickerStunUntil.Clear();
            expiredClickerStunIds.Clear();
        }

        private static bool HasClearClickerPath(NetworkPlayer source, NetworkPlayer listener)
        {
            if (source == listener) return true;
            Vector3 from = source.transform.position + Vector3.up * 0.6f;
            Vector3 to = listener.transform.position + Vector3.up * 0.6f;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= 0.05f) return true;

            RaycastHit[] hits = Physics.RaycastAll(from, delta / distance, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.GetComponentInParent<NetworkPlayer>() != null) continue;
                return false;
            }
            return true;
        }

        [TargetRpc]
        private void ClickerFlashTarget(NetworkConnection conn)
        {
            NetworkPlayer localPlayer = NetworkPlayer.Local;
            Camera camera = localPlayer != null ? localPlayer.GetComponentInChildren<Camera>(true) : null;
            MicrophoneVisionController vision = camera != null ? camera.GetComponent<MicrophoneVisionController>() : null;
            if (vision == null)
            {
                Debug.LogError("Clicker flash arrived before the local camera vision controller was ready.");
                return;
            }

            vision.TriggerClickerCameraFlash();
        }

        [Server]
        public bool TryBeginBandage(int playerId)
        {
            if (valves == null || result.Value != RoundResult.InProgress || healingChannels.ContainsKey(playerId) ||
                !TryGetAliveRunner(playerId, out var player) || player.IsTreatmentLocked || !HasMissingLives(playerId) ||
                !bandageCounts.TryGetValue(playerId, out int count) || count <= 0)
                return false;

            StartHealingChannel(player, player, true, BandageUseSeconds);
            return true;
        }

        [Server]
        public bool TryBeginTreatment(int healerId)
        {
            if (valves == null || result.Value != RoundResult.InProgress || healingChannels.ContainsKey(healerId) ||
                !TryGetAliveRunner(healerId, out var healer) || healer.IsTreatmentLocked)
                return false;

            NetworkPlayer patient = null;
            float best = interactRange * interactRange;
            foreach (var candidate in NetworkPlayer.All)
            {
                if (candidate == healer || candidate.Role != PlayerRole.Runner ||
                    candidate.Status != PlayerStatus.Alive || !candidate.NeedsTreatment ||
                    candidate.IsTreatmentLocked || healingChannels.ContainsKey(candidate.PlayerId) ||
                    !HasMissingLives(candidate.PlayerId)) continue;
                float distance = (candidate.transform.position - healer.transform.position).sqrMagnitude;
                if (distance > best || !HasClearTreatmentPath(healer, candidate)) continue;
                best = distance;
                patient = candidate;
            }

            if (patient == null) return false;
            StartHealingChannel(healer, patient, false, TeammateTreatmentSeconds);
            return true;
        }

        [Server]
        public void CancelHealingAction(int playerId) => EndHealingChannel(playerId, cancelled: true);

        private bool HasMissingLives(int playerId)
        {
            int halves = runnerHalves.TryGetValue(playerId, out int stored) ? stored : FullHalves;
            return halves > 0 && halves < FullHalves;
        }

        private void StartHealingChannel(NetworkPlayer actor, NetworkPlayer patient, bool isBandage, float duration)
        {
            var channel = new HealingChannel
            {
                ActorId = actor.PlayerId,
                PatientId = patient.PlayerId,
                IsBandage = isBandage,
                Duration = duration,
                StartedAt = Time.realtimeSinceStartupAsDouble,
                SyncAt = Time.realtimeSinceStartupAsDouble + HealingProgressSyncSeconds,
                ActorStart = actor.transform.position,
                PatientStart = patient.transform.position,
                PatientWasCrouching = patient.IsCrouching,
            };
            healingChannels.Add(actor.PlayerId, channel);
            if (!isBandage)
            {
                actor.SetTreatmentLocked(true);
                patient.SetTreatmentLocked(true);
                patient.SetCrouching(true);
            }
            actor.SendHealingProgress(true, isBandage, 0f);
            if (patient != actor) patient.SendHealingProgress(true, false, 0f);
            TreatmentObservers(actor.PlayerId, patient.PlayerId, duration);
        }

        private void TickHealingChannels()
        {
            if (healingChannels.Count == 0) return;
            double now = Time.realtimeSinceStartupAsDouble;
            healingChannelIds.Clear();
            foreach (int actorId in healingChannels.Keys) healingChannelIds.Add(actorId);

            foreach (int actorId in healingChannelIds)
            {
                if (!healingChannels.TryGetValue(actorId, out var channel)) continue;
                if (!IsHealingChannelValid(channel))
                {
                    EndHealingChannel(actorId, cancelled: true);
                    continue;
                }

                channel.Elapsed = Mathf.Min(channel.Duration, (float)(now - channel.StartedAt));
                if (now >= channel.SyncAt)
                {
                    if (NetworkPlayer.TryGet(actorId, out var actor))
                        actor.SendHealingProgress(true, channel.IsBandage, channel.Elapsed / channel.Duration);
                    if (channel.PatientId != actorId && NetworkPlayer.TryGet(channel.PatientId, out var patient))
                        patient.SendHealingProgress(true, false, channel.Elapsed / channel.Duration);
                    channel.SyncAt = now + HealingProgressSyncSeconds;
                }

                if (channel.Elapsed >= channel.Duration) CompleteHealingChannel(channel);
            }
        }

        private bool IsHealingChannelValid(HealingChannel channel)
        {
            if (valves == null || result.Value != RoundResult.InProgress ||
                !TryGetAliveRunner(channel.ActorId, out var actor) || !TryGetAliveRunner(channel.PatientId, out var patient) ||
                !HasMissingLives(channel.PatientId)) return false;

            if (channel.IsBandage)
                return channel.ActorId == channel.PatientId &&
                    bandageCounts.TryGetValue(channel.ActorId, out int count) && count > 0;

            return channel.ActorId != channel.PatientId && patient.IsCrouching &&
                actor.IsTreatmentLocked && patient.IsTreatmentLocked &&
                (actor.transform.position - channel.ActorStart).sqrMagnitude <= 0.16f &&
                (patient.transform.position - channel.PatientStart).sqrMagnitude <= 0.16f &&
                (patient.transform.position - actor.transform.position).sqrMagnitude <= interactRange * interactRange &&
                HasClearTreatmentPath(actor, patient);
        }

        // Treatment works from any direction, but world geometry must not separate the players.
        // Ignore only the two participants' colliders so their own character meshes do not block the ray.
        private bool HasClearTreatmentPath(NetworkPlayer healer, NetworkPlayer patient)
        {
            const float chestHeight = 0.8f;
            Vector3 start = healer.transform.position + Vector3.up * chestHeight;
            Vector3 end = patient.transform.position + Vector3.up * chestHeight;
            Vector3 offset = end - start;
            float distance = offset.magnitude;
            if (distance <= 0.001f) return true;

            int hitCount = Physics.RaycastNonAlloc(start, offset / distance, treatmentPathHits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // If the buffer fills, fail closed rather than accidentally allowing treatment through geometry.
            if (hitCount >= treatmentPathHits.Length) return false;

            for (int i = 0; i < hitCount; i++)
            {
                Transform hitTransform = treatmentPathHits[i].transform;
                if (IsPlayerCollider(hitTransform, healer) || IsPlayerCollider(hitTransform, patient)) continue;
                return false;
            }

            return true;
        }

        private static bool IsPlayerCollider(Transform hitTransform, NetworkPlayer player)
        {
            return hitTransform == player.transform || hitTransform.IsChildOf(player.transform);
        }

        private void CompleteHealingChannel(HealingChannel channel)
        {
            if (!healingChannels.ContainsKey(channel.ActorId) ||
                !NetworkPlayer.TryGet(channel.ActorId, out var actor) ||
                !NetworkPlayer.TryGet(channel.PatientId, out var patient))
            {
                EndHealingChannel(channel.ActorId, cancelled: true);
                return;
            }

            if (channel.IsBandage)
            {
                int count = bandageCounts[channel.ActorId] - 1;
                bandageCounts[channel.ActorId] = count;
                actor.SendBandageCount(count);
                int halves = runnerHalves[channel.ActorId];
                SetLives(actor, Mathf.Min(FullHalves, halves + 1));
            }
            else SetLives(patient, FullHalves);

            EndHealingChannel(channel.ActorId, cancelled: false);
        }

        private void EndHealingChannel(int actorId, bool cancelled)
        {
            if (!healingChannels.TryGetValue(actorId, out var channel)) return;
            healingChannels.Remove(actorId);
            if (NetworkPlayer.TryGet(channel.ActorId, out var actor))
            {
                actor.SendHealingProgress(false, channel.IsBandage, 0f);
                if (!channel.IsBandage) actor.SetTreatmentLocked(false);
            }
            if (!channel.IsBandage && NetworkPlayer.TryGet(channel.PatientId, out var patient))
            {
                patient.SendHealingProgress(false, false, 0f);
                patient.SetTreatmentLocked(false);
                patient.SetCrouching(channel.PatientWasCrouching);
            }
            if (cancelled) StopTreatmentObservers(channel.ActorId, channel.PatientId);
        }

        private void CancelHealingFor(int playerId)
        {
            healingChannelIds.Clear();
            foreach (var pair in healingChannels)
                if (pair.Value.ActorId == playerId || pair.Value.PatientId == playerId)
                    healingChannelIds.Add(pair.Key);
            foreach (int actorId in healingChannelIds) EndHealingChannel(actorId, cancelled: true);
        }

        private void CancelAllHealingActions()
        {
            healingChannelIds.Clear();
            foreach (int actorId in healingChannels.Keys) healingChannelIds.Add(actorId);
            foreach (int actorId in healingChannelIds) EndHealingChannel(actorId, cancelled: true);
        }

        private const int MaxBandages = 3;

        [ObserversRpc]
        private void TreatmentObservers(int healerId, int patientId, float duration)
        {
            if (NetworkPlayer.TryGet(healerId, out var healer) && healer.TryGetComponent(out CharacterMotion healerMotion))
                healerMotion.PlayTreatment(duration);
            if (patientId != healerId && NetworkPlayer.TryGet(patientId, out var patient) &&
                patient.TryGetComponent(out CharacterMotion patientMotion))
                patientMotion.PlayTreatment(duration);
        }

        [ObserversRpc]
        private void StopTreatmentObservers(int healerId, int patientId)
        {
            if (NetworkPlayer.TryGet(healerId, out var healer) && healer.TryGetComponent(out CharacterMotion healerMotion))
                healerMotion.StopTreatment();
            if (patientId != healerId && NetworkPlayer.TryGet(patientId, out var patient) &&
                patient.TryGetComponent(out CharacterMotion patientMotion))
                patientMotion.StopTreatment();
        }

        // The Seeker's client judged the swing with TagRadius (targetInReach); the server re-picks the
        // target from its own state, allowing up to tagServerMaxDistance for latency (기획서 3.1-2).
        [Server]
        public SwingResult TrySwing(int seekerId, Transform seeker, bool targetInReach)
        {
            if (valves == null || roster.GetRole(seekerId) != PlayerRole.Seeker) return SwingResult.Rejected;
            if (nextTagTimes.TryGetValue(seekerId, out float next) && Time.time < next) return SwingResult.Rejected;

            NetworkPlayer target = null;
            var result = targetInReach && TryFindTagTarget(seeker, tagServerMaxDistance, out target)
                ? Hit(target) : SwingResult.Missed;
            nextTagTimes[seekerId] = Time.time + (result == SwingResult.Missed ? tagMissCooldown : tagHitCooldown);
            if (result != SwingResult.Missed) Debug.Log($"[MARCO] P{seekerId} hit P{target.PlayerId}: {result}");
            return result;
        }

        // Full lives → wounded, told to the target alone (other Runners learn it from the moan later).
        // Below full → Echo, which every client sees through the status SyncVar.
        private SwingResult Hit(NetworkPlayer target)
        {
            CancelHealingFor(target.PlayerId);
            int halves = runnerHalves.TryGetValue(target.PlayerId, out int stored) ? stored : FullHalves;
            if (halves >= FullHalves)
            {
                SetLives(target, WoundedHalves);
                return SwingResult.Wounded;
            }
            if (!ApplyTag(target)) return SwingResult.Missed;
            // Keep the terminal value so ResetLives can restore the owner's private HUD next round.
            SetLives(target, 0);
            return SwingResult.Echoed;
        }

        private void SetLives(NetworkPlayer player, int halves)
        {
            runnerHalves[player.PlayerId] = halves;
            player.SendHealth(halves);
            player.SetNeedsTreatment(halves > 0 && halves < FullHalves);
        }

        // A new round starts everyone at full lives again; only players who changed need telling.
        private void ResetLives()
        {
            foreach (int playerId in runnerHalves.Keys)
                if (NetworkPlayer.TryGet(playerId, out var player))
                {
                    player.SendHealth(FullHalves);
                    player.SetNeedsTreatment(false);
                }
            runnerHalves.Clear();
        }

        private bool ApplyTag(NetworkPlayer target)
        {
            if (round != null && !round.NotifyRunnerTagged(target.PlayerId)) return false;

            ClearClickerHolding(target);
            RemoveFromWork(target.PlayerId);
            target.SetStatus(PlayerStatus.Tagged);
            return true;
        }

        // Nearest alive Runner within range (3D distance) and the horizontal ±tagHalfAngle cone, with
        // no wall in between. The Seeker's client calls it with TagRadius, the server with its limit.
        public bool TryFindTagTarget(Transform seeker, float range, out NetworkPlayer target)
        {
            target = null;
            float best = range * range;
            var forward = Vector3.ProjectOnPlane(seeker.forward, Vector3.up);
            foreach (var player in NetworkPlayer.All)
            {
                if (player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive) continue;
                var offset = player.transform.position - seeker.position;
                if (offset.sqrMagnitude > best || Vector3.Angle(forward, Vector3.ProjectOnPlane(offset, Vector3.up)) > tagHalfAngle) continue;
                // A wall (or another player) between them blocks the swing; the line starts inside
                // the Seeker's own CharacterController, which a linecast does not report.
                if (Physics.Linecast(seeker.position, player.transform.position, out var blocker, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !blocker.transform.IsChildOf(player.transform)) continue;
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
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            CancelHealingFor(conn.ClientId);
            if (valves == null) return;
            nextTagTimes.Remove(conn.ClientId);
            runnerHalves.Remove(conn.ClientId);
            bandageCounts.Remove(conn.ClientId);
            clickerHoldings.Remove(conn.ClientId);
            clickerStunUntil.Remove(conn.ClientId);
            RemoveFromWork(conn.ClientId);
            // A spectator never entered RoundState's count; reporting them would count as a tag.
            bool spectating = NetworkPlayer.TryGet(conn.ClientId, out var player) && player.Status == PlayerStatus.Spectating;
            if (round != null && !spectating) round.NotifyPlayerLeft(conn.ClientId);
        }

        // One world pickup at each marked map-design location. Its wrap-and-cross model is generated
        // locally; only this synchronized availability list and the server-owned inventory matter.
        private void ResetBandagePickups(bool available)
        {
            ResolveBandagePositions();
            bandagePickupStates.Clear();
            for (int i = 0; i < bandagePositions.Count; i++) bandagePickupStates.Add(available);
        }

        private void ResolveBandagePositions()
        {
            for (int i = 0; i < BandageSpawnPoints.Length; i++)
            {
                if (resolvedBandageSpawns.Contains(i)) continue;
                bandagePositions.Add(BandageSpawnPoints[i] + Vector3.up * 0.12f);
                resolvedBandageSpawns.Add(i);
            }
        }

        private void ResetClickerPickups(bool available)
        {
            clickerPickupStates.Clear();
            clickerRespawnTimes.Clear();
            for (int i = 0; i < ClickerSpawnPoints.Length; i++) clickerPickupStates.Add(available);
        }

        private void TickClickerRespawns()
        {
            for (int i = 0; i < ClickerSpawnPoints.Length; i++)
            {
                if (!clickerRespawnTimes.TryGetValue(i, out float respawnAt) || Time.time < respawnAt) continue;
                if (i < clickerPickupStates.Count) clickerPickupStates[i] = true;
                clickerRespawnTimes.Remove(i);
            }
        }

        private void EnsureBandageViews()
        {
            ResolveBandagePositions();
            if (bandagePositions.Count == 0) return;
            if (bandageWrapMaterial == null || bandageMarkMaterial == null) CreateBandageMaterials();
            while (bandageViews.Count < bandagePositions.Count)
            {
                int index = bandageViews.Count;
                var root = new GameObject($"붕대 {index + 1}");
                root.transform.position = bandagePositions[index];
                CreateBandageCube(root.transform, "붕대 포장", new Vector3(0.34f, 0.12f, 0.25f),
                    new Vector3(0, 0.11f, 0), bandageWrapMaterial);
                CreateBandageCube(root.transform, "십자 가로", new Vector3(0.18f, 0.018f, 0.055f),
                    new Vector3(0, 0.18f, 0), bandageMarkMaterial);
                CreateBandageCube(root.transform, "십자 세로", new Vector3(0.055f, 0.018f, 0.18f),
                    new Vector3(0, 0.18f, 0), bandageMarkMaterial);
                bandageViews.Add(root.transform);
            }
        }

        private void EnsureClickerViews()
        {
            if (ClickerSpawnPoints.Length == 0) return;
            if (clickerMaterial == null)
            {
                Shader shader = Shader.Find("PoolV12/BlockoutSurface") ?? Shader.Find("MARCO/WorldSpaceTexturedSurface");
                if (shader == null) return;
                clickerMaterial = CreateBandageMaterial(shader, new Color(0.82f, 0.9f, 1f));
            }

            while (clickerViews.Count < ClickerSpawnPoints.Length)
            {
                int index = clickerViews.Count;
                var root = new GameObject($"찰칵이 {index + 1}");
                root.transform.position = ClickerSpawnPoints[index];
                CreateBandageCube(root.transform, "본체", new Vector3(0.12f, 0.16f, 0.08f), Vector3.zero, clickerMaterial);
                CreateBandageCube(root.transform, "렌즈", new Vector3(0.08f, 0.035f, 0.085f), new Vector3(0, 0.055f, -0.045f), clickerMaterial);
                clickerViews.Add(root.transform);
                root.SetActive(index < clickerPickupStates.Count && clickerPickupStates[index]);
            }
        }

        private void CreateBandageMaterials()
        {
            // Bandages are created at runtime, so they cannot rely on a scene-assigned
            // material. The unlit shader bypasses MARCO-'s world-space microphone falloff
            // and leaves the pickups visible even when the microphone is silent.
            var shader = Shader.Find("PoolV12/BlockoutSurface") ?? Shader.Find("MARCO/WorldSpaceTexturedSurface");
            if (shader == null)
            {
                Debug.LogError("No MARCO- world-space shader is available for bandage pickups.", this);
                return;
            }
            bandageWrapMaterial = CreateBandageMaterial(shader, new Color(0.94f, 0.89f, 0.72f));
            bandageMarkMaterial = CreateBandageMaterial(shader, new Color(0.75f, 0.08f, 0.06f));
        }

        private static Material CreateBandageMaterial(Shader shader, Color color)
        {
            var material = new Material(shader) { name = "붕대 아이템" };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            return material;
        }

        private static void CreateBandageCube(Transform parent, string childName, Vector3 scale, Vector3 position, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = childName;
            cube.transform.SetParent(parent, false);
            cube.transform.localScale = scale;
            cube.transform.localPosition = position;
            var collider = cube.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void ClearBandageViews()
        {
            foreach (var view in bandageViews) if (view != null) Destroy(view.gameObject);
            bandageViews.Clear();
            if (bandageWrapMaterial != null) Destroy(bandageWrapMaterial);
            if (bandageMarkMaterial != null) Destroy(bandageMarkMaterial);
            bandageWrapMaterial = bandageMarkMaterial = null;
        }

        private void ClearClickerViews()
        {
            foreach (Transform view in clickerViews) if (view != null) Destroy(view.gameObject);
            clickerViews.Clear();
            if (clickerMaterial != null) Destroy(clickerMaterial);
            clickerMaterial = null;
        }

        [Server]
        private void ClearClickerHolding(NetworkPlayer player)
        {
            if (player == null) return;
            clickerHoldings[player.PlayerId] = false;
            player.SendClickerCount(0);
        }

        private void OnDestroy()
        {
            ClearBandageViews();
            ClearClickerViews();
            if (Instance == this) Instance = null;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Dev cheats for RoundCheats (Host only). They skip range, cooldown and waiting, but every
        // outcome still goes through the Core objects, so the result is whatever Core decides.
        private const int CheatWorkerId = -1;
        // Long enough for any valve state (Closed / Rotating / Reflowing) to finish turning open.
        private const float CheatValveSeconds = 1000f;

        private bool CheatReady
        {
            get
            {
                if (valves != null && result.Value == RoundResult.InProgress) return true;
                Debug.Log("[MARCO][Cheat] 진행 중인 라운드가 없음");
                return false;
            }
        }

        // Master key: a cheat worker turns each unopened active valve; Valve opens it and
        // ValveInteractionService re-evaluates the gate on each status change.
        [Server]
        internal void CheatOpenActiveValves()
        {
            if (!CheatReady) return;
            foreach (var valve in board.Valves.Values)
            {
                if (valve.Status == ValveStatus.Open) continue;
                valve.BeginWork(CheatWorkerId);
                valve.Tick(CheatValveSeconds);
                valve.EndWork(CheatWorkerId);
            }
            Debug.Log($"[MARCO][Cheat] 밸브 개방 {valves.OpenValveCount}개, 게이트 {(gate.IsOpen ? "열림" : "닫힘")}");
        }

        [Server]
        internal void CheatTagRunner()
        {
            if (!CheatReady) return;
            if (!TryGetFirstAliveRunner(out var target)) return;
            Debug.Log(ApplyTag(target) ? $"[MARCO][Cheat] P{target.PlayerId} 태그" : "[MARCO][Cheat] 태그 거부됨");
        }

        [Server]
        internal void CheatEscapeRunner()
        {
            if (!CheatReady) return;
            if (!TryGetFirstAliveRunner(out var player)) return;
            Debug.Log(ApplyEscape(player) ? $"[MARCO][Cheat] P{player.PlayerId} 탈출" : "[MARCO][Cheat] 탈출 거부됨 (게이트 닫힘 → F1 먼저)");
        }

        // One server step with a large dt: valves, round time and the last-survivor timer all advance.
        [Server]
        internal void CheatFastForward(float seconds)
        {
            if (!CheatReady) return;
            Step(seconds);
            Debug.Log($"[MARCO][Cheat] {seconds}초 빨리감기");
        }

        // Core enters the phase by itself once exactly one Runner is undecided, so tag down to one.
        [Server]
        internal void CheatEnterLastSurvivorPhase()
        {
            if (!CheatReady) return;
            if (round == null)
            {
                Debug.Log("[MARCO][Cheat] 밸브 전용 모드(도망자 1명)에는 최후 생존자 페이즈가 없음");
                return;
            }
            if (round.LastSurvivorPhase.HasEntered)
            {
                Debug.Log("[MARCO][Cheat] 이미 최후 생존자 페이즈");
                return;
            }

            int alive = 0;
            foreach (var player in NetworkPlayer.All)
                if (player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive) alive++;
            while (alive > 1 && TryGetFirstAliveRunner(out var target) && ApplyTag(target)) alive--;
            Debug.Log("[MARCO][Cheat] 도망자 1명만 남김 → 다음 틱에 최후 생존자 페이즈");
        }

        // Toggle the host between Runner and Seeker. Swap with an alive player of the opposite
        // role when one exists so multiplayer test rounds keep the same number of each role.
        [Server]
        internal void CheatToggleHostRole()
        {
            if (!CheatReady) return;

            NetworkPlayer host = null;
            foreach (var player in NetworkPlayer.All)
            {
                if (!player.IsRoomHost) continue;
                host = player;
                break;
            }

            if (host == null || host.Status != PlayerStatus.Alive ||
                (host.Role != PlayerRole.Runner && host.Role != PlayerRole.Seeker))
            {
                Debug.Log("[MARCO][Cheat] 전환할 수 있는 생존 중인 호스트 플레이어가 없음");
                return;
            }

            PlayerRole oldRole = host.Role;
            PlayerRole newRole = oldRole == PlayerRole.Seeker ? PlayerRole.Runner : PlayerRole.Seeker;
            NetworkPlayer partner = null;
            foreach (var player in NetworkPlayer.All)
            {
                if (player == host || player.Status != PlayerStatus.Alive || player.Role != newRole) continue;
                partner = player;
                break;
            }

            CancelHealingFor(host.PlayerId);
            host.SetCrouching(false);
            host.SetTreatmentLocked(false);
            host.SetRole(newRole);

            if (partner != null)
            {
                CancelHealingFor(partner.PlayerId);
                partner.SetCrouching(false);
                partner.SetTreatmentLocked(false);
                partner.SetRole(oldRole);
            }

            Debug.Log($"[MARCO][Cheat] Host P{host.PlayerId}: {oldRole} → {newRole}" +
                      (partner != null ? $" · P{partner.PlayerId}: {newRole} → {oldRole}" : " · 역할 교환 대상 없음"));
        }

        private static bool TryGetFirstAliveRunner(out NetworkPlayer runner)
        {
            foreach (var player in NetworkPlayer.All)
            {
                if (player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive) continue;
                runner = player;
                return true;
            }
            runner = null;
            Debug.Log("[MARCO][Cheat] 살아있는 도망자가 없음");
            return false;
        }
#endif

        [Server]
        internal void MarkGateOpened() => gateOpen.Value = true;

        // No valve id in the payload: the cue must not tell anyone where the valve is (기획서 6.1, 10-26).
        [ObserversRpc]
        internal void ValveOpenedObservers()
        {
            if (valveOpenedClip == null)
            {
                Debug.Log("[MARCO] 밸브 개방 완료음 (에셋 없음)");
                return;
            }
            if (valveOpenedSource == null)
            {
                valveOpenedSource = gameObject.AddComponent<AudioSource>();
                valveOpenedSource.playOnAwake = false;
                valveOpenedSource.spatialBlend = 0f; // 2D: no direction, no distance
            }
            valveOpenedSource.PlayOneShot(valveOpenedClip);
        }

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
