using System.IO;
using FishNet.Managing;
using FishNet.Managing.Object;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Menu helpers for the LAN test: (re)create the test scene and build a standalone exe
    // containing only that scene, so one PC can run "exe Host + editor Join".
    // The scene is not added to EditorBuildSettings, leaving the team's scene list untouched.
    public static class NetworkTestTools
    {
        public const string ScenePath = "Assets/Scenes/Network/NetworkTest.unity";
        private const string PlayerPath = "Builds/NetworkTest/MarcoNetworkTest.exe";

        [MenuItem("MARCO/Network/Create Test Scene")]
        public static void CreateTestScene()
        {
            if (File.Exists(ScenePath) && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("NetworkTest", $"{ScenePath} 를 다시 만들까요?", "다시 만들기", "취소"))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var manager = new GameObject("NetworkManager");
            // FishNet only auto-fills this on later inspector validation; builds log an error when it stays null.
            manager.AddComponent<NetworkManager>().SpawnablePrefabs = FindDefaultPrefabObjects();
            manager.AddComponent<LanConnector>();
            manager.AddComponent<NetworkTestHud>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[MARCO] Network test scene saved: {ScenePath}");
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
