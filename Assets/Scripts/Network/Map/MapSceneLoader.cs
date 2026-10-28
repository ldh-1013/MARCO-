using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Network.Map
{
    // Loads the map scene (meshes + colliders, no network objects) additively on every machine, so
    // the map stays a single untouched source. Its preview camera and fly viewer are turned off, and
    // map GamePoints that this scene draws itself (valves, drains) are hidden so they don't overlap.
    public sealed class MapSceneLoader : MonoBehaviour
    {
        private const string MapGamePointsRoot = "GamePoints";
        private const string DesignOnlyCandidateMarker = "CandidateMarker_NoSpawnLogic";

        [SerializeField] private string mapScene = "IndoorPool_v1_2";
        // Names under the map's GamePoints to hide at runtime, e.g. Valve_A, Drain_1.
        [SerializeField] private string[] hiddenMapPoints = Array.Empty<string>();
        // Map root objects to hide at runtime, e.g. the map's pipes that this scene places at the valves.
        [SerializeField] private string[] hiddenMapRoots = Array.Empty<string>();

        private void Awake()
        {
            // Already open next to this scene in the editor (multi-scene editing): don't load a second copy.
            var scene = SceneManager.GetSceneByName(mapScene);
            if (scene.isLoaded)
            {
                OnMapLoaded(scene);
                return;
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.LoadScene(mapScene, LoadSceneMode.Additive);
        }

        private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != mapScene) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            OnMapLoaded(scene);
        }

        private void OnMapLoaded(Scene scene)
        {
            // Ambient lighting comes from the active scene; the map has no lights of its own.
            SceneManager.SetActiveScene(scene);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var camera in root.GetComponentsInChildren<Camera>(true)) camera.gameObject.SetActive(false);
                // PoolV12Viewer lives in Assembly-CSharp, which this assembly cannot reference.
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                    if (behaviour.GetType().Name == "PoolV12Viewer") behaviour.enabled = false;
                // Spawn-candidate cubes are editor/map-design markers only; actual pickups are
                // spawned by NetworkRound. Keep them in the source map, but never show them in-game.
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    if (transform.name == DesignOnlyCandidateMarker) transform.gameObject.SetActive(false);
                if (root.name == MapGamePointsRoot) HidePoints(root.transform);
                if (Array.IndexOf(hiddenMapRoots, root.name) >= 0) root.SetActive(false);
            }
        }

        private void HidePoints(Transform points)
        {
            foreach (var point in points.GetComponentsInChildren<Transform>(true))
                if (point != points && Array.IndexOf(hiddenMapPoints, point.name) >= 0) point.gameObject.SetActive(false);
        }
    }
}
