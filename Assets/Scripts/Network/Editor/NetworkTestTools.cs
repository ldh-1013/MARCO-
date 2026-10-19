using System.IO;
using FishNet.Component.Spawning;
using FishNet.Component.Transforming;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using Marco.Network.Player;
using Marco.Network.Roles;
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
        private const string PlayerPath = "Builds/NetworkTest/MarcoNetworkTest.exe";
        private static readonly Vector3[] SpawnPositions =
            { new(-2, 1, -2), new(2, 1, -2), new(-2, 1, 2), new(2, 1, 2) };

        [MenuItem("MARCO/Network/Create Test Scene")]
        public static void CreateTestScene()
        {
            if (File.Exists(ScenePath) && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("NetworkTest", $"{ScenePath} 를 다시 만들까요?", "다시 만들기", "취소"))
                return;

            var playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath);
            if (playerPrefab == null) playerPrefab = CreatePlayerPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = new Vector3(5, 1, 5);

            var manager = new GameObject("NetworkManager");
            // FishNet only auto-fills this on later inspector validation; builds log an error when it stays null.
            manager.AddComponent<NetworkManager>().SpawnablePrefabs = FindDefaultPrefabObjects();
            manager.AddComponent<LanConnector>();
            manager.AddComponent<NetworkTestHud>();

            var spawner = manager.AddComponent<PlayerSpawner>();
            spawner.SetPlayerPrefab(playerPrefab);
            spawner.Spawns = new Transform[SpawnPositions.Length];
            for (int i = 0; i < SpawnPositions.Length; i++)
            {
                var point = new GameObject($"SpawnPoint{i + 1}").transform;
                point.position = SpawnPositions[i];
                spawner.Spawns[i] = point;
            }
            manager.AddComponent<RoleAssigner>();
            manager.AddComponent<NetworkPlayerRoster>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[MARCO] Network test scene saved: {ScenePath}");
        }

        // Capsule body with a client-authoritative NetworkTransform; the camera child stays off
        // until NetworkPlayer turns it on for the owner.
        [MenuItem("MARCO/Network/Create Player Prefab")]
        public static NetworkObject CreatePlayerPrefab()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = "NetworkPlayer";
            Object.DestroyImmediate(root.GetComponent<CapsuleCollider>());
            root.AddComponent<CharacterController>();
            root.AddComponent<NetworkObject>();
            root.AddComponent<NetworkTransform>();
            var movement = root.AddComponent<PlayerMovement>();
            var player = root.AddComponent<NetworkPlayer>();

            var pivot = new GameObject("CameraPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0, 0.6f, 0);
            var camera = new GameObject("PlayerCamera", typeof(Camera), typeof(AudioListener));
            camera.transform.SetParent(pivot, false);
            camera.SetActive(false);

            SetReference(movement, "cameraPivot", pivot);
            SetReference(player, "ownerCamera", camera);

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

        private static void SetReference(Object target, string field, Object value)
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
