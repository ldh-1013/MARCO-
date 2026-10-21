using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Prototype
{
    // World objects remain serialized and editable, including both roles in the shared scene.
    [DefaultExecutionOrder(-1000)]
    public sealed class PrototypeSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private PrototypeSceneKind sceneKind = PrototypeSceneKind.Lobby;
        [SerializeField] private GameObject hunterPlayer;
        [SerializeField] private GameObject survivorPlayer;
        public PrototypePlayerRole LocalRole { get; private set; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private RoomSessionPrototype developmentRoom;
        private Transform developmentTemplates;
        private GameObject developmentHunterTemplate, developmentSurvivorTemplate;
        private readonly System.Collections.Generic.Dictionary<int, GameObject> participants = new System.Collections.Generic.Dictionary<int, GameObject>();
        private DevelopmentCheatMode? pendingDevelopmentMode;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLaunchContext() { MatchLaunchContext.Clear(); }

        private void Awake()
        {
            if (sceneKind == PrototypeSceneKind.SurvivorTest)
            {
                CreateTemporaryValve();
                return;
            }
            if (sceneKind != PrototypeSceneKind.Game) return;
            if (!MatchLaunchContext.IsActive)
            {
                // Direct Play/build entry always goes through the lobby before gameplay starts.
                if (hunterPlayer != null) hunterPlayer.SetActive(false);
                if (survivorPlayer != null) survivorPlayer.SetActive(false);
                enabled = false;
                Cursor.lockState = CursorLockMode.None;
                if (Application.CanStreamedLevelBeLoaded(MatchLaunchContext.LobbyScene))
                    SceneManager.LoadSceneAsync(MatchLaunchContext.LobbyScene, LoadSceneMode.Single);
                else Debug.LogError("Lobby scene is missing from the build scene list.", this);
                return;
            }
            LocalRole = MatchLaunchContext.LocalRole;
            if (Application.isPlaying)
            {
                SpawnParticipants();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                developmentRoom = RoomSessionPrototype.Instance;
                if (developmentRoom != null) developmentRoom.DevelopmentRolesChanged += ApplyDevelopmentRoles;
#endif
                return;
            }
            ConfigurePlayer(hunterPlayer, LocalRole == PrototypePlayerRole.Hunter);
            ConfigurePlayer(survivorPlayer, LocalRole == PrototypePlayerRole.Survivor);
        }

        private static void CreateTemporaryValve()
        {
            if (FindObjectsByType<TemporaryValvePrototype>(FindObjectsSortMode.None).Length > 0) return;

            var valve = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            valve.name = "Temporary Valve (replace with production valve)";
            valve.transform.position = new Vector3(0f, 0.55f, 0f);
            valve.transform.localScale = new Vector3(0.65f, 0.55f, 0.65f);
            valve.AddComponent<TemporaryValvePrototype>();
        }

        private void SpawnParticipants()
        {
            hunterPlayer.SetActive(false); survivorPlayer.SetActive(false);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Keep pristine INACTIVE serialized role templates; never clone an
            // already initialized asset/animation graph when changing roles.
            developmentTemplates = new GameObject("Development role templates (inactive)").transform;
            developmentTemplates.SetParent(transform); developmentTemplates.gameObject.SetActive(false);
            developmentHunterTemplate = Instantiate(hunterPlayer, developmentTemplates);
            developmentSurvivorTemplate = Instantiate(survivorPlayer, developmentTemplates);
#endif
            var players = new System.Collections.Generic.List<GameObject>();
            var holder = new GameObject("Inactive participant setup"); holder.SetActive(false);
            bool usedHunter = false, usedSurvivor = false; int survivorIndex = 0;
            foreach (var member in MatchLaunchContext.Members)
            {
                bool hunter = member.Role == PrototypePlayerRole.Hunter;
                var template = hunter ? hunterPlayer : survivorPlayer;
                bool used = hunter ? usedHunter : usedSurvivor;
                GameObject player = used ? Instantiate(template, holder.transform) : template;
                if (used) player.name = "Survivor Player " + member.Id;
                if (hunter) usedHunter = true;
                else { usedSurvivor = true; player.transform.position += Vector3.right * (survivorIndex++ * 2); }
                player.GetComponent<CharacterVisualPrototype>().SetAssetNameBeforeInitialization(member.AssetName);
                bool local = member.Id == MatchLaunchContext.LocalPlayerId;
                ConfigurePlayer(player, local);
                var replication = player.GetComponent<PlayerPoseReplicator>();
                if (replication == null) replication = player.AddComponent<PlayerPoseReplicator>();
                replication.Configure(member.Id, local);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                participants[member.Id] = player;
#endif
                players.Add(player);
            }
            // All clones are prepared before any asset initializes or imported demo script runs.
            foreach (var player in players) { player.transform.SetParent(null); player.SetActive(true); }
            if (!usedHunter) Destroy(hunterPlayer);
            if (!usedSurvivor) Destroy(survivorPlayer);
            Destroy(holder);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void ApplyDevelopmentRoles()
        {
            LocalRole = MatchLaunchContext.LocalRole;
            foreach (var member in MatchLaunchContext.Members)
            {
                if (!participants.TryGetValue(member.Id, out var previous) || previous == null) continue;
                bool hunter = member.Role == PrototypePlayerRole.Hunter;
                if ((previous.GetComponent<HunterTagPrototype>() != null) == hunter
                    && previous.GetComponent<CharacterVisualPrototype>().AssetName == member.AssetName) continue;
                bool local = member.Id == MatchLaunchContext.LocalPlayerId;
                var oldCamera = previous.GetComponentInChildren<Camera>(true);
                var replacement = Instantiate(hunter ? developmentHunterTemplate : developmentSurvivorTemplate, developmentTemplates);
                replacement.name = hunter ? "Hunter Player" : "Survivor Player" + (local ? "" : " " + member.Id);
                replacement.transform.SetPositionAndRotation(previous.transform.position, previous.transform.rotation);
                replacement.GetComponent<CharacterVisualPrototype>().SetAssetNameBeforeInitialization(member.AssetName);
                var newCamera = replacement.GetComponentInChildren<Camera>(true);
                if (oldCamera != null && newCamera != null)
                {
                    newCamera.transform.localRotation = oldCamera.transform.localRotation;
                    var oldVision = oldCamera.GetComponent<MicrophoneVisionController>();
                    var newVision = newCamera.GetComponent<MicrophoneVisionController>();
                    if (oldVision != null && newVision != null) newVision.SetDevelopmentFullVision(oldVision.DevelopmentFullVision);
                }
                ConfigurePlayer(replacement, local);
                var replication = replacement.GetComponent<PlayerPoseReplicator>();
                if (replication == null) replication = replacement.AddComponent<PlayerPoseReplicator>();
                replication.Configure(member.Id, local);
                replication.InheritDevelopmentStream(previous.GetComponent<PlayerPoseReplicator>());
                // Disable before enabling the replacement: only one local camera,
                // input owner and microphone exist, even in this transition frame.
                previous.SetActive(false); Destroy(previous);
                participants[member.Id] = replacement;
                replacement.transform.SetParent(null); replacement.SetActive(true);
            }
            if (pendingDevelopmentMode.HasValue)
            {
                var mode = pendingDevelopmentMode.Value;
                pendingDevelopmentMode = null;
                ApplyDevelopmentMode(mode);
            }
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (sceneKind != PrototypeSceneKind.Game || !MatchLaunchContext.IsActive) return;
            if (Input.GetKeyDown(KeyCode.F11)) SelectDevelopmentMode(DevelopmentCheatMode.Survivor);
            if (Input.GetKeyDown(KeyCode.F9) && LocalRole == PrototypePlayerRole.Hunter) SelectDevelopmentMode(DevelopmentCheatMode.Ghost);
            if (Input.GetKeyDown(KeyCode.F12)) SelectDevelopmentMode(DevelopmentCheatMode.Hunter);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void SelectDevelopmentMode(DevelopmentCheatMode mode)
        {
            if (sceneKind != PrototypeSceneKind.Game || !MatchLaunchContext.IsActive) return;
            int localId = MatchLaunchContext.LocalPlayerId;
            if (!participants.TryGetValue(localId, out var localPlayer) || localPlayer == null) return;
            var requestedRole = mode == DevelopmentCheatMode.Hunter ? PrototypePlayerRole.Hunter : PrototypePlayerRole.Survivor;
            if (LocalRole != requestedRole)
            {
                if (developmentRoom == null) developmentRoom = RoomSessionPrototype.Instance;
                if (developmentRoom == null) return;
                pendingDevelopmentMode = mode;
                if (!developmentRoom.RequestDevelopmentRoleSwitch()) pendingDevelopmentMode = null;
                return;
            }
            ApplyDevelopmentMode(mode);
        }

        private void ApplyDevelopmentMode(DevelopmentCheatMode mode)
        {
            if (mode == DevelopmentCheatMode.Hunter) return;
            if (!participants.TryGetValue(MatchLaunchContext.LocalPlayerId, out var localPlayer) || localPlayer == null) return;
            var ghost = localPlayer.GetComponent<SurvivorGhostPrototype>();
            if (ghost == null) return;
            ghost.SetGhostForDevelopment(mode == DevelopmentCheatMode.Ghost);
        }
#endif
        private void OnDestroy()
        {
            if (developmentRoom != null) developmentRoom.DevelopmentRolesChanged -= ApplyDevelopmentRoles;
        }
#endif

        private static void ConfigurePlayer(GameObject player, bool local)
        {
            if (player == null) { Debug.LogError("Shared scene is missing a role player."); return; }
            var movement = player.GetComponent<PrototypeFirstPersonController>();
            if (movement != null) movement.enabled = local;
            var bodyView = player.GetComponent<FirstPersonBodyView>();
            if (bodyView != null) bodyView.enabled = local;
            var interaction = player.GetComponent<SurvivorInteractionPrototype>();
            if (interaction != null) interaction.enabled = local;
            var healthHud = player.GetComponent<SurvivorHealthHud>();
            if (healthHud != null) healthHud.enabled = local;
            var hunter = player.GetComponent<HunterTagPrototype>();
            if (hunter != null) hunter.SetLocalPlayer(local);
            var ghost = player.GetComponent<SurvivorGhostPrototype>();
            if (ghost != null) ghost.SetLocalPlayer(local);
            var voiceChat = player.GetComponent<ProximityVoiceChat>();
            if (voiceChat == null) voiceChat = player.AddComponent<ProximityVoiceChat>();
            voiceChat.enabled = true;
            var visual = player.GetComponent<CharacterVisualPrototype>();
            if (visual != null) visual.SetFirstPerson(local);
            // Disable the entire other camera object: no second microphone, mask or listener.
            foreach (var camera in player.GetComponentsInChildren<Camera>(true))
                camera.gameObject.SetActive(local);
        }

        private void OnGUI()
        {
            if (sceneKind == PrototypeSceneKind.Lobby) return;
            bool hunter = sceneKind == PrototypeSceneKind.Game
                ? LocalRole == PrototypePlayerRole.Hunter : sceneKind == PrototypeSceneKind.HunterTest;
            GUI.Label(new Rect(35, 25, 800, 35), sceneKind == PrototypeSceneKind.Game
                ? "MARCO · " + (hunter ? "헌터" : "서바이버") : hunter ? "HUNTER TEST" : "SURVIVOR TEST");
            GUI.Label(new Rect(35, 55, 1050, 28), hunter ? "WASD 이동 · Shift 달리기 · 마우스 회전 · 좌클릭 태그" : "WASD 이동 · Shift 달리기 · 마우스 회전 · E 상호작용");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (sceneKind == PrototypeSceneKind.Game && MatchLaunchContext.IsActive && Cursor.lockState != CursorLockMode.Locked)
            {
                GUI.depth = -10;
                GUI.Label(new Rect(Screen.width - 390, 160, 360, 25), "[DEV] 플레이 상태 선택 · F11 서바이버 / F9 고스트 / F12 헌터");
                if (GUI.Button(new Rect(Screen.width - 390, 188, 112, 36), "서바이버")) SelectDevelopmentMode(DevelopmentCheatMode.Survivor);
                if (GUI.Button(new Rect(Screen.width - 274, 188, 112, 36), "고스트")) SelectDevelopmentMode(DevelopmentCheatMode.Ghost);
                if (GUI.Button(new Rect(Screen.width - 158, 188, 112, 36), "헌터")) SelectDevelopmentMode(DevelopmentCheatMode.Hunter);
            }
#endif
        }
    }
    public enum PrototypeSceneKind { Lobby, HunterTest, SurvivorTest, Game }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    internal enum DevelopmentCheatMode { Survivor, Ghost, Hunter }
#endif
}
