using System.Collections.Generic;
using System.IO;
using Marco.Network.Escape;
using Marco.Network.Map;
using Marco.Network.Valves;
using Marco.Core.WinCondition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Menu helper for the network round on the real pool map (IndoorPool_v1_2). The scene holds only
    // network objects plus the GamePoints marker prefab; MapSceneLoader adds the map at runtime, so
    // the map scene itself is never copied or edited. It is the first scene in EditorBuildSettings (game entry with LobbyMenu).
    public static class NetworkMapSceneTools
    {
        public const string ScenePath = "Assets/Scenes/Network/NetworkPool_v1_2.unity";
        public const string GamePointsPrefabPath = "Assets/Prefabs/Network/PoolV12_GamePoints.prefab";
        private const ulong RoundSceneId = 0x4D4152434F000002; // "MARCO" + 2; differs from NetworkTest
        // The map's own visuals, used as-is (never edited); the map scene's copies are hidden at runtime.
        private const string ValveVisualPath = "Assets/Object/파이프.fbx";
        private const string DrainVisualPath = "Assets/Maps/PoolV12/Generated/Prefabs/DrainCover_Reusable.prefab";
        // The map scene's Valve_* wheels are turned 90° about Y; the markers copy that so ours face the same way.
        private const float ValveMarkerYaw = 90f;

        // Pipe model parts: the wheel ring (lies in its local XY plane), its cross spokes and hub spin;
        // the stem under the wheel stays out of the collider so the wheel side stays open.
        private const string WheelPart = "토러스.001";
        private static readonly string[] WheelSpinParts = { WheelPart, "실린더.011", "실린더.012", "구체" };
        private const string WheelStemPart = "실린더.010";
        private const float LampAboveWheel = 0.3f;

        // Pipe yaw (relative to the valve marker) and scale copied from the map scene's own pipes,
        // which sit 0.4–7 m off the valve points; ours put the wheel center on the marker instead.
        private static readonly (float yaw, float scale)[] ValvePipes =
        {
            (-90, 0.21299766f),  // A: 파이프 (4)
            (0, 0.21299766f),    // B: 파이프 (2)
            (0, 0.2721795f),     // C: 파이프
            (-90, 0.21299766f),  // D: 파이프 (3)
            (0, 0.21299766f),    // E: 파이프 (1)
        };

        // The map scene's own pipe roots, replaced by the valve pipes above.
        private static readonly string[] MapPipeRoots = { "파이프", "파이프 (1)", "파이프 (2)", "파이프 (3)", "파이프 (4)" };

        private static readonly string[] RunnerSpawnPoints = { "SpawnPoint_1", "SpawnPoint_2", "SpawnPoint_3" };

        // Initial marker positions. Exit_* / SpawnPoint_* copy the map scene's GamePoints;
        // Valve_* / Drain_* come from Layout_v1_2.json "points" (x, height, z).
        private static readonly (string name, Vector3 position)[] Points =
        {
            ("Exit_1", new(55, 0, 61)),
            ("Exit_2", new(23, 0, 1)),
            ("SpawnPoint_1", new(27.85f, 0, 5.75f)),
            ("SpawnPoint_2", new(29.95f, 0, 5.75f)),
            ("SpawnPoint_3", new(32.05f, 0, 5.75f)),
            ("SpawnPoint_4", new(55, 3.5f, 18)),
            ("Valve_A", new(58.5f, 1, 36)),
            ("Valve_B", new(38.5f, -3, 52)),
            ("Valve_C", new(6, 4.5f, 16.5f)),
            ("Valve_D", new(50.5f, 1, 20)),
            ("Valve_E", new(4.5f, -0.7f, 51)),
            ("Drain_1", new(36, -3.5f, 47.5f)),
            ("Drain_2", new(7, -3.5f, 47)),
        };

        // TEMPORARY until swimming (see PoolLadders): bottom inside the pool, top on the deck.
        // Deck spots avoid the Layout_v1_2.json obstacles (e.g. lifeguard chair h06).
        private static readonly (string name, Vector3 bottom, Vector3 top)[] Ladders =
        {
            ("PoolLadder_1", new(39.3f, -3.5f, 50), new(41.2f, 0, 50)),  // competition pool, deep end east wall
            ("PoolLadder_2", new(15.7f, -1.8f, 51), new(13.5f, 0, 51)),  // competition pool, shallow end west wall
            ("PoolLadder_3", new(3.7f, -1.2f, 49), new(1.8f, 0, 49)),    // teaching pool, west wall
            ("PoolLadder_4", new(7, -3.5f, 47), new(12.5f, 0, 44)),      // teaching pool sump
        };

        [MenuItem("MARCO/Network/Create Pool Map Scene")]
        public static void CreatePoolMapScene()
        {
            if (File.Exists(ScenePath) && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("NetworkPool", $"{ScenePath} 를 다시 만들까요?\n(위치 표시 프리팹은 그대로 둡니다)", "다시 만들기", "취소"))
                return;

            var playerPrefab = NetworkTestTools.LoadOrCreatePlayerPrefab();
            // Never overwrite an existing prefab: its markers may have been moved by hand.
            var pointsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GamePointsPrefabPath);
            if (pointsPrefab == null) pointsPrefab = CreateGamePointsPrefab();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Overview until the local player spawns; light and ambient come with the map scene.
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camera.tag = "MainCamera";
            camera.transform.SetPositionAndRotation(new Vector3(32, 70, 31), Quaternion.Euler(90, 0, 0));
            var loader = new GameObject("MapSceneLoader").AddComponent<MapSceneLoader>();
            PrefabUtility.InstantiatePrefab(pointsPrefab);

            var spawner = NetworkTestTools.CreateNetworkManager(playerPrefab);
            spawner.Spawns = new Transform[RunnerSpawnPoints.Length];
            for (int i = 0; i < RunnerSpawnPoints.Length; i++)
            {
                var spawn = new GameObject($"Spawn {RunnerSpawnPoints[i]}");
                Anchor(spawn, RunnerSpawnPoints[i], Vector3.up * GamePoints.PlayerStandHeight);
                spawner.Spawns[i] = spawn.transform;
            }

            var hidden = new List<string>();
            for (var id = ValveId.A; id <= ValveId.E; id++)
            {
                Anchor(CreateMapValve(id), $"Valve_{id}", Vector3.zero);
                hidden.Add($"Valve_{id}");
            }
            var exitSize = new Vector3(3, 3, 0.5f);  // both exits sit in walls running along X
            Anchor(NetworkTestTools.CreateExit(ExitId.StaffDoor, Vector3.zero, exitSize), "Exit_1", new Vector3(0, 1.5f, 0));
            Anchor(NetworkTestTools.CreateExit(ExitId.MainGate, Vector3.zero, exitSize), "Exit_2", new Vector3(0, 1.5f, 0));
            Anchor(CreateMapDrainHatch(DrainHatchId.First), "Drain_1", Vector3.zero);
            Anchor(CreateMapDrainHatch(DrainHatchId.Second), "Drain_2", Vector3.zero);
            hidden.Add("Drain_1");
            hidden.Add("Drain_2");
            SetStrings(loader, "hiddenMapPoints", hidden);
            SetStrings(loader, "hiddenMapRoots", MapPipeRoots);

            NetworkTestTools.SaveWithRound(scene, ScenePath, RoundSceneId);
            Debug.Log($"[MARCO] Network pool scene saved: {ScenePath}");
        }

        // The pipe's wheel spins with progress; status color goes to a separate lamp above it,
        // because the pipe's materials are shared and ValveView must not tint them.
        // The root sits on the wheel center, so the marker and the server range check stay on the wheel.
        private static GameObject CreateMapValve(ValveId id)
        {
            var root = new GameObject($"Valve {id}");
            var pipe = InstantiateVisual(ValveVisualPath, root.transform).transform;
            var (yaw, scale) = ValvePipes[(int)id];
            pipe.localRotation = Quaternion.Euler(0, yaw, 0);
            pipe.localScale = Vector3.one * scale;
            var wheel = FindPart(pipe, WheelPart);
            pipe.localPosition = -wheel.position;  // root is still at the origin, unrotated

            // An added child keeps the pipe a prefab instance; the wheel parts can't be reparented under it.
            var pivot = new GameObject("WheelPivot").transform;
            pivot.SetParent(pipe, false);
            pivot.SetPositionAndRotation(wheel.position, wheel.rotation);
            AddPipeCollider(pipe);
            var lamp = CreateLamp(root.transform, new Vector3(0, LampAboveWheel, 0), 0.15f);

            var view = root.AddComponent<ValveView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("id").enumValueIndex = (int)id;
            serialized.FindProperty("pivot").objectReferenceValue = pivot;
            serialized.FindProperty("spinAxis").vector3Value = Vector3.forward;  // the wheel lies in its local XY plane
            var parts = serialized.FindProperty("spinParts");
            parts.arraySize = WheelSpinParts.Length;
            for (int i = 0; i < WheelSpinParts.Length; i++)
                parts.GetArrayElementAtIndex(i).objectReferenceValue = FindPart(pipe, WheelSpinParts[i]);
            serialized.FindProperty("body").objectReferenceValue = lamp;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        private static Transform FindPart(Transform pipe, string partName)
        {
            foreach (var part in pipe.GetComponentsInChildren<Transform>(true))
                if (part.name == partName) return part;
            throw new System.InvalidOperationException($"{ValveVisualPath} 에 '{partName}' 가 없습니다");
        }

        // One box around the pipe body, leaving out the wheel parts and the stem under them, so the box
        // tops out below the wheel and players can walk right up to it (well inside the valve range).
        private static void AddPipeCollider(Transform pipe)
        {
            var bounds = new Bounds();
            bool any = false;
            foreach (var filter in pipe.GetComponentsInChildren<MeshFilter>(true))
            {
                var part = filter.transform.name;
                if (part == WheelStemPart || System.Array.IndexOf(WheelSpinParts, part) >= 0) continue;
                var mesh = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = mesh.center + Vector3.Scale(mesh.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var point = pipe.InverseTransformPoint(filter.transform.TransformPoint(local));
                    if (any) bounds.Encapsulate(point);
                    else bounds = new Bounds(point, Vector3.zero);
                    any = true;
                }
            }
            var box = pipe.gameObject.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
        }

        private static GameObject CreateMapDrainHatch(DrainHatchId id)
        {
            var root = new GameObject($"DrainHatch {id}");
            InstantiateVisual(DrainVisualPath, root.transform);
            var lamp = CreateLamp(root.transform, new Vector3(0, 0.4f, 0), 0.2f);

            var view = root.AddComponent<DrainHatchView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("id").enumValueIndex = (int)id;
            serialized.FindProperty("body").objectReferenceValue = lamp;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return root;
        }

        // Stays a prefab instance, so later map prefab changes reach this scene too.
        private static GameObject InstantiateVisual(string path, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        }

        // Small sphere without a collider, so it never blocks players.
        private static Renderer CreateLamp(Transform parent, Vector3 localPosition, float size)
        {
            var lamp = NetworkTestTools.CreatePrimitive(PrimitiveType.Sphere, "StatusLamp");
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = localPosition;
            lamp.transform.localScale = Vector3.one * size;
            return lamp.GetComponent<Renderer>();
        }

        private static void SetStrings(MapSceneLoader loader, string propertyName, IReadOnlyList<string> names)
        {
            var serialized = new SerializedObject(loader);
            var property = serialized.FindProperty(propertyName);
            property.arraySize = names.Count;
            for (int i = 0; i < names.Count; i++) property.GetArrayElementAtIndex(i).stringValue = names[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Anchor(GameObject target, string pointName, Vector3 offset)
        {
            var anchor = target.AddComponent<GamePointAnchor>();
            var serialized = new SerializedObject(anchor);
            serialized.FindProperty("pointName").stringValue = pointName;
            serialized.FindProperty("offset").vector3Value = offset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateGamePointsPrefab()
        {
            var root = new GameObject("PoolV12_GamePoints", typeof(GamePoints), typeof(PoolLadders));
            foreach (var (name, position) in Points)
            {
                var marker = CreateMarker(name, root.transform, position);
                if (name.StartsWith("Valve_")) marker.rotation = Quaternion.Euler(0, ValveMarkerYaw, 0);
            }
            foreach (var (name, bottom, top) in Ladders)
                CreateMarker("Top", CreateMarker(name, root.transform, bottom), top);

            Directory.CreateDirectory(Path.GetDirectoryName(GamePointsPrefabPath));
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, GamePointsPrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"[MARCO] GamePoints prefab saved: {GamePointsPrefabPath}");
            return prefab;
        }

        private static Transform CreateMarker(string name, Transform parent, Vector3 position)
        {
            var marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
            marker.position = position;
            // Name label in the Scene view, so the empty markers can be found and dragged.
            EditorGUIUtility.SetIconForObject(marker.gameObject, (Texture2D)EditorGUIUtility.IconContent("sv_label_1").image);
            return marker;
        }
    }
}
