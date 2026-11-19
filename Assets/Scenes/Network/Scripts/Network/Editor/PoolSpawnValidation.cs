using System;
using System.Collections.Generic;
using System.Linq;
using Marco.Network.Map;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Network.Editor
{
    // Read-only geometry check against the actual map colliders and player capsule.
    public static class PoolSpawnValidation
    {
        [MenuItem("MARCO/Network/Validate Pool Spawns %#F9")]
        public static void Validate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before validating spawns.");
            const string mapPath = "Assets/Scenes/Maps/IndoorPool_v1_2.unity";
            var previous = SceneManager.GetActiveScene();
            var map = SceneManager.GetSceneByPath(mapPath);
            bool opened = !map.isLoaded;
            GameObject points = null;
            var failures = new List<string>();
            try
            {
                if (opened) map = EditorSceneManager.OpenScene(mapPath, OpenSceneMode.Additive);
                points = PrefabUtility.LoadPrefabContents(NetworkMapSceneTools.GamePointsPrefabPath);
                var capsule = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Network/NetworkPlayer.prefab")
                    .GetComponent<CharacterController>();
                Physics.SyncTransforms();
                int[] runners = { 1, 2, 3, 5, 6, 7, 8, 9 };
                var positions = new List<Vector3>();
                for (int i = 0; i <= runners.Length; i++)
                {
                    bool hunter = i == runners.Length;
                    string name = "SpawnPoint_" + (hunter ? 4 : runners[i]);
                    var marker = points.transform.Find(name);
                    if (marker == null) { failures.Add(name + ": missing marker"); continue; }
                    Vector3 expected = hunter ? new Vector3(55, 3.5f, 18)
                        : new Vector3(27.85f + (i % 4) * 2.1f, 0, 5.75f + (i / 4) * 1.5f);
                    if (Vector3.Distance(marker.position, expected) > .01f) failures.Add(name + ": wrong design position");
                    Vector3 root = marker.position + Vector3.up * GamePoints.PlayerStandHeight;
                    Vector3 center = root + capsule.center;
                    float half = capsule.height * .5f - capsule.radius;
                    var obstacles = Physics.OverlapCapsule(center + Vector3.up * half, center - Vector3.up * half,
                        capsule.radius, ~0, QueryTriggerInteraction.Ignore).Where(c => c.gameObject.scene == map).ToArray();
                    if (obstacles.Length > 0) failures.Add(name + ": capsule overlaps " + string.Join(", ", obstacles.Select(c => c.name)));
                    var floor = Physics.RaycastAll(marker.position + Vector3.up * .5f, Vector3.down, 1f,
                        ~0, QueryTriggerInteraction.Ignore).Where(h => h.collider.gameObject.scene == map)
                        .OrderBy(h => h.distance).ToArray();
                    if (floor.Length == 0 || floor[0].normal.y < .7f || Mathf.Abs(floor[0].point.y - marker.position.y) > .05f)
                        failures.Add(name + ": no level floor at marker height");
                    foreach (var other in positions)
                        if (Vector3.Distance(root, other) < capsule.radius * 2f + .1f) failures.Add(name + ": overlapping spawn slots");
                    positions.Add(root);
                    if (!hunter && Vector2.Distance(new Vector2(root.x, root.z), new Vector2(23, 1)) <= 2f + capsule.radius)
                        failures.Add(name + ": inside main exit radius");
                    Debug.Log($"[PoolSpawnCheck] {name}: root={root}, floor={(floor.Length > 0 ? floor[0].point.y.ToString("F3") : "missing")}, obstacles={obstacles.Length}");
                }
            }
            finally
            {
                if (points != null) PrefabUtility.UnloadPrefabContents(points);
                if (opened && map.isLoaded) EditorSceneManager.CloseScene(map, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            if (failures.Count > 0) throw new InvalidOperationException("[PoolSpawnCheck] FAILED\n" + string.Join("\n", failures));
            Debug.Log("[PoolSpawnCheck] PASS: eight lobby slots and one upstairs hunter slot have clear capsules, supporting floors and correct design coordinates.");
        }
    }
}
