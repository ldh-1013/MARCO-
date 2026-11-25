using System.IO;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Managing.Transporting;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting.UTP;
using Marco.Network.Character;
using Marco.Network.Escape;
using Marco.Network.Lobby;
using Marco.Network.Player;
using Marco.Network.Roles;
using Marco.Network.Round;
using Marco.Network.Valves;
using Marco.Network.Vision;
using Marco.Network.Voice;
using Marco.Core.WinCondition;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Menu helpers for the LAN test: (re)create the player prefab and test scene, and build a
    // standalone exe containing only that scene, so one PC can run "exe Host + editor Join".
    // The scene is not added to EditorBuildSettings, leaving the team's scene list untouched.
    public static class NetworkTestTools
    {
        public const string ScenePath = "Assets/Scenes/Network/NetworkTest.unity";
        public const string PlayerPrefabPath = "Assets/Prefabs/Network/NetworkPlayer.prefab";
        public const string PlaceholderMaterialPath = "Assets/Materials/Network/NetworkPlaceholder.mat";
        private const string SeekerCharacterPath = "Assets/Prefabs/Network/Characters/Demon.asset";
        private const string RunnerCharacterPath = "Assets/Prefabs/Network/Characters/UnityChan.asset";
        private const string FootDotShaderPath = "Assets/Prototype/MicrophoneFootDot.shader";
        private const string FootRingShaderPath = "Assets/Prototype/WalkingFootRing.shader";
        private const string PlaceholderShaderPath = "Assets/Materials/Network/NetworkPlaceholder.shader";
        private const string PlayerPath = "Builds/NetworkTest/MarcoNetworkTest.exe";
        private const ulong RoundSceneId = 0x4D4152434F000001; // "MARCO" + 1; any fixed non-zero value
        // Game design: six players total, so each transport accepts up to five remote peers.
        private const int MaxPlayers = 6;

        private static readonly Vector3[] SpawnPositions =
            { new(-2, 1, -2), new(2, 1, -2), new(-2, 1, 2), new(2, 1, 2) };

        [MenuItem("MARCO/Network/Create Test Scene")]
        public static void CreateTestScene()
        {
            if (File.Exists(ScenePath) && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("NetworkTest", $"{ScenePath} 를 다시 만들까요?", "다시 만들기", "취소"))
                return;

            var playerPrefab = LoadOrCreatePlayerPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var floor = CreatePrimitive(PrimitiveType.Plane, "Floor");
            floor.transform.localScale = new Vector3(5, 1, 5);

            var spawner = CreateNetworkManager(playerPrefab);
            spawner.Spawns = new Transform[SpawnPositions.Length];
            for (int i = 0; i < SpawnPositions.Length; i++)
            {
                var point = new GameObject($"SpawnPoint{i + 1}").transform;
                point.position = SpawnPositions[i];
                spawner.Spawns[i] = point;
            }

            for (var id = ValveId.A; id <= ValveId.E; id++) CreateValve(id, new Vector3(-8 + 4 * (int)id, 0.5f, 8));
            CreateExit(ExitId.MainGate, new Vector3(0, 1.5f, -14), new Vector3(3, 3, 0.5f));
            CreateExit(ExitId.StaffDoor, new Vector3(14, 1.5f, 0), new Vector3(0.5f, 3, 2));
            CreateDrainHatch(DrainHatchId.First, new Vector3(-14, 0.05f, -8));
            CreateDrainHatch(DrainHatchId.Second, new Vector3(-14, 0.05f, 8));
            SaveWithRound(scene, ScenePath, RoundSceneId);
            Debug.Log($"[MARCO] Network test scene saved: {ScenePath}");
        }

        internal static NetworkObject LoadOrCreatePlayerPrefab()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath);
            if (playerPrefab == null) return CreatePlayerPrefab();
            EnsurePlayerPrefabComponents();
            return playerPrefab;
        }

        // Lobby (LAN + Unity Relay) + HUD + spawning + roles; callers fill spawner.Spawns.
        internal static PlayerSpawner CreateNetworkManager(NetworkObject playerPrefab)
        {
            var manager = new GameObject("NetworkManager");
            // FishNet only auto-fills this on later inspector validation; builds log an error when it stays null.
            manager.AddComponent<NetworkManager>().SpawnablePrefabs = FindDefaultPrefabObjects();
            AddSession(manager);
            manager.AddComponent<NetworkTestHud>();

            var spawner = manager.AddComponent<PlayerSpawner>();
            spawner.SetPlayerPrefab(playerPrefab);
            manager.AddComponent<RoleAssigner>();
            manager.AddComponent<NetworkPlayerRoster>();
            return spawner;
        }

        [MenuItem("MARCO/Network/Migrate Scenes To Unity Relay")]
        public static void MigrateScenesToUnityRelay()
        {
            foreach (string path in new[] { ScenePath, NetworkMapSceneTools.ScenePath })
            {
                if (!File.Exists(path)) continue;
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var manager = Object.FindAnyObjectByType<NetworkManager>();
                if (manager == null) continue;
                ConfigureRelaySession(manager.gameObject);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log($"[MARCO] Scene switched to Unity Relay: {path}");
            }
        }

        // LAN (Tugboat) and Unity Relay (FishyUnityTransport) share a Multipass. GlobalServerActions is
        // off so LanConnector starts only the selected transport's server.
        private static void AddSession(GameObject manager)
        {
            var tugboat = manager.AddComponent<Tugboat>();
            var relay = manager.AddComponent<FishyUnityTransport>();
            var multipass = manager.AddComponent<Multipass>();
            multipass.GlobalServerActions = false;
            var serialized = new SerializedObject(multipass);
            var transports = serialized.FindProperty("_transports");
            transports.arraySize = 2;
            transports.GetArrayElementAtIndex(0).objectReferenceValue = tugboat;
            transports.GetArrayElementAtIndex(1).objectReferenceValue = relay;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SetInt(tugboat, "_maximumClients", MaxPlayers - 1);
            relay.SetMaximumClients(MaxPlayers - 1);
            var transportManager = manager.GetComponent<TransportManager>();
            if (transportManager == null) transportManager = manager.AddComponent<TransportManager>();
            transportManager.Transport = multipass;
            EditorUtility.SetDirty(transportManager);

            if (manager.GetComponent<LanConnector>() == null) manager.AddComponent<LanConnector>();
            if (manager.GetComponent<UnityRelayLobby>() == null) manager.AddComponent<UnityRelayLobby>();
            if (manager.GetComponent<LobbyMenu>() == null) manager.AddComponent<LobbyMenu>();
        }

        private static void ConfigureRelaySession(GameObject manager)
        {
            var tugboat = manager.GetComponent<Tugboat>();
            if (tugboat == null) tugboat = manager.AddComponent<Tugboat>();
            var relay = manager.GetComponent<FishyUnityTransport>();
            if (relay == null) relay = manager.AddComponent<FishyUnityTransport>();
            relay.SetMaximumClients(MaxPlayers - 1);
            SetInt(tugboat, "_maximumClients", MaxPlayers - 1);

            var multipass = manager.GetComponent<Multipass>();
            if (multipass == null) multipass = manager.AddComponent<Multipass>();
            multipass.GlobalServerActions = false;
            var serialized = new SerializedObject(multipass);
            var transports = serialized.FindProperty("_transports");
            transports.arraySize = 2;
            transports.GetArrayElementAtIndex(0).objectReferenceValue = tugboat;
            transports.GetArrayElementAtIndex(1).objectReferenceValue = relay;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (var transport in manager.GetComponents<Transport>())
                if (transport != tugboat && transport != relay && transport != multipass)
                    Object.DestroyImmediate(transport);

            var transportManager = manager.GetComponent<TransportManager>();
            if (transportManager == null) transportManager = manager.AddComponent<TransportManager>();
            transportManager.Transport = multipass;
            if (manager.GetComponent<LanConnector>() == null) manager.AddComponent<LanConnector>();

            // Remove the menu before the old lobby shim so Unity no longer sees its serialized
            // RequireComponent dependency while we replace the component type.
            var legacyMenu = manager.GetComponent<LobbyMenu>();
            if (legacyMenu != null) Object.DestroyImmediate(legacyMenu);

            var legacyLobby = manager.GetComponent<SteamLobby>();
            if (legacyLobby != null) Object.DestroyImmediate(legacyLobby);
            if (manager.GetComponent<UnityRelayLobby>() == null) manager.AddComponent<UnityRelayLobby>();
            if (manager.GetComponent<LobbyMenu>() == null) manager.AddComponent<LobbyMenu>();
            EditorUtility.SetDirty(transportManager);
        }

        private static void SetInt(Object target, string field, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Adds the Round scene object and saves the scene at path.
        internal static void SaveWithRound(UnityEngine.SceneManagement.Scene scene, string path, ulong roundSceneId)
        {
            var round = new GameObject("Round");
            var roundObject = round.AddComponent<NetworkObject>();
            round.AddComponent<NetworkRound>();

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            EditorSceneManager.SaveScene(scene, path);
            // FishNet derives scene ids from the scene path, which a brand-new scene lacks until saved;
            // set one explicitly so every client resolves the same scene object.
            roundObject.SetSceneId(roundSceneId);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        // Cube body + flat handle bar on top.
        internal static GameObject CreateValve(ValveId id, Vector3 position)
        {
            var body = CreatePrimitive(PrimitiveType.Cube, $"Valve {id}");
            body.transform.position = position;

            var handle = CreatePrimitive(PrimitiveType.Cube, "Handle");
            Object.DestroyImmediate(handle.GetComponent<BoxCollider>());
            handle.transform.SetParent(body.transform, false);
            handle.transform.localPosition = new Vector3(0, 0.6f, 0);
            handle.transform.localScale = new Vector3(0.9f, 0.1f, 0.15f);

            var view = body.AddComponent<ValveView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("id").enumValueIndex = (int)id;
            serialized.FindProperty("pivot").objectReferenceValue = body.transform;
            var parts = serialized.FindProperty("spinParts");
            parts.arraySize = 1;
            parts.GetArrayElementAtIndex(0).objectReferenceValue = handle.transform;
            serialized.FindProperty("body").objectReferenceValue = body.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return body;
        }

        // Tall cube without a collider so players can walk into it to escape.
        internal static GameObject CreateExit(ExitId id, Vector3 position, Vector3 size)
        {
            var body = CreatePrimitive(PrimitiveType.Cube, $"Exit {id}");
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());
            body.transform.position = position;
            body.transform.localScale = size;

            var view = body.AddComponent<ExitView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("id").enumValueIndex = (int)id;
            serialized.FindProperty("body").objectReferenceValue = body.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return body;
        }

        // Flat square on the floor.
        internal static GameObject CreateDrainHatch(DrainHatchId id, Vector3 position)
        {
            var body = CreatePrimitive(PrimitiveType.Cube, $"DrainHatch {id}");
            Object.DestroyImmediate(body.GetComponent<BoxCollider>());
            body.transform.position = position;
            body.transform.localScale = new Vector3(1.5f, 0.1f, 1.5f);

            var view = body.AddComponent<DrainHatchView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("id").enumValueIndex = (int)id;
            serialized.FindProperty("body").objectReferenceValue = body.GetComponent<Renderer>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return body;
        }

        [MenuItem("MARCO/Network/Update Player Prefab")]
        public static void UpdatePlayerPrefab() => LoadOrCreatePlayerPrefab();

        // Adds components / settings introduced after the prefab was first made, keeping existing file ids.
        private static void EnsurePlayerPrefabComponents()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            bool changed = false;
            if (root.GetComponent<PlayerInteractor>() == null)
            {
                root.AddComponent<PlayerInteractor>();
                changed = true;
            }
            if (root.GetComponent<CharacterBody>() == null)
            {
                AddCharacter(root);
                changed = true;
            }
            if (root.GetComponent<PlayerVoice>() == null)
            {
                root.AddComponent<PlayerVoice>();
                changed = true;
            }
            var playerCamera = root.GetComponentInChildren<Camera>(true).gameObject;
            var vision = playerCamera.GetComponent<MicrophoneVisionController>();
            if (vision == null)
            {
                AddVision(playerCamera);
                changed = true;
            }
            var material = LoadOrCreatePlaceholderMaterial();
            var body = root.GetComponentInChildren<Renderer>();
            if (body.sharedMaterial != material)
            {
                body.sharedMaterial = material;
                changed = true;
            }
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // CreatePrimitive gives the built-in Default-Material (Standard), which the project's URP
        // 2D Renderer never draws; every placeholder uses NetworkPlaceholder instead.
        internal static GameObject CreatePrimitive(PrimitiveType type, string name)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.GetComponent<Renderer>().sharedMaterial = LoadOrCreatePlaceholderMaterial();
            return primitive;
        }

        internal static Material LoadOrCreatePlaceholderMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(PlaceholderMaterialPath);
            if (material != null) return material;
            material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(PlaceholderShaderPath));
            AssetDatabase.CreateAsset(material, PlaceholderMaterialPath);
            return material;
        }

        // Capsule body with a client-authoritative NetworkTransform; the camera child stays off
        // until NetworkPlayer turns it on for the owner.
        // Sized for the map's narrowest 1 m doors (radius 0.35 / height 1.8, the map builder's
        // clearance check). Feet stay 1 m below the pivot, so GamePoints.PlayerStandHeight holds.
        [MenuItem("MARCO/Network/Create Player Prefab")]
        public static NetworkObject CreatePlayerPrefab()
        {
            var root = new GameObject("NetworkPlayer");
            var controller = root.AddComponent<CharacterController>();
            controller.radius = 0.35f;
            controller.height = 1.8f;
            controller.skinWidth = 0.05f;
            controller.center = new Vector3(0, -0.1f, 0);

            // The unit capsule mesh is radius 0.5 / height 2, scaled to match the controller.
            var body = CreatePrimitive(PrimitiveType.Capsule, "Body");
            Object.DestroyImmediate(body.GetComponent<CapsuleCollider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = controller.center;
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);

            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkTransform>();
            var movement = root.AddComponent<PlayerMovement>();
            var player = root.AddComponent<NetworkPlayer>();
            root.AddComponent<PlayerInteractor>();

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0, 0.6f, 0);
            var camera = new GameObject("PlayerCamera", typeof(Camera), typeof(AudioListener));
            camera.transform.SetParent(pivot, false);
            camera.SetActive(false);

            SetReference(movement, "cameraPivot", pivot);
            SetReference(player, "ownerCamera", camera);
            AddCharacter(root);
            AddVision(camera);
            root.AddComponent<PlayerVoice>();

            Directory.CreateDirectory(Path.GetDirectoryName(PlayerPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath).GetComponent<NetworkObject>();
            Object.DestroyImmediate(root);

            var defaults = FindDefaultPrefabObjects();
            if (defaults != null)
            {
                defaults.AddObject(prefab, checkForDuplicates: true, initializeAdded: false);
                EditorUtility.SetDirty(defaults);
                AssetDatabase.SaveAssets();
            }
            Debug.Log($"[MARCO] Network player prefab saved: {PlayerPrefabPath}");
            return prefab;
        }

        // Role models (Seeker = Demon, Runner = UnityChan); first-person view stays off until NetworkPlayer enables it for the owner.
        private static void AddCharacter(GameObject root)
        {
            var character = root.AddComponent<CharacterBody>();
            SetReference(character, "seekerAsset", AssetDatabase.LoadAssetAtPath<CharacterAsset>(SeekerCharacterPath));
            SetReference(character, "runnerAsset", AssetDatabase.LoadAssetAtPath<CharacterAsset>(RunnerCharacterPath));
            root.AddComponent<CharacterMotion>();
            root.AddComponent<FirstPersonBody>().enabled = false;
        }

        // Microphone vision, foot dots, walking rings and the F8 cheat live on the camera, so they only
        // run for the owner. The shaders still sit in Assets/Prototype (moved in the clean-up step).
        private static void AddVision(GameObject camera)
        {
            var vision = camera.AddComponent<MicrophoneVisionController>();
            SetReference(vision, "microphoneFootDotShader", AssetDatabase.LoadAssetAtPath<Shader>(FootDotShaderPath));
            camera.AddComponent<MicrophoneFootDots>();
            var rings = camera.AddComponent<WalkingFootRings>();
            SetReference(rings, "ringShader", AssetDatabase.LoadAssetAtPath<Shader>(FootRingShaderPath));
            camera.AddComponent<DevelopmentVisionCheat>();
        }

        internal static void SetReference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static DefaultPrefabObjects FindDefaultPrefabObjects()
        {
            foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(DefaultPrefabObjects)}"))
                return AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(AssetDatabase.GUIDToAssetPath(guid));
            Debug.LogWarning("[MARCO] DefaultPrefabObjects not found; select the NetworkManager once so FishNet creates it.");
            return null;
        }

        [MenuItem("MARCO/Network/Build Test Player (Windows)")]
        public static void BuildTestPlayer()
        {
            if (!File.Exists(ScenePath)) CreateTestScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = PlayerPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development,
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.Exception($"Network test build failed: {report.summary.result}");
            Debug.Log($"[MARCO] Network test player built: {Path.GetFullPath(PlayerPath)}");
        }
    }
}
