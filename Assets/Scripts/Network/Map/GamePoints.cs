using UnityEngine;

namespace Marco.Network.Map
{
    // Root of the map's location-marker prefab. Children carry the map's GamePoints names
    // (Valve_A, Exit_1, SpawnPoint_4 …) and objects look them up by name, so when the map changes
    // only the markers inside the prefab move. One per scene; names are searched under this root
    // only, so the map scene's own GamePoints never clash.
    public sealed class GamePoints : MonoBehaviour
    {
        // Markers sit on the floor; a CharacterController (height 1.8, center -0.1) stands 1 m above it,
        // plus a little so it never starts inside the floor collider.
        public const float PlayerStandHeight = 1.05f;

        private static GamePoints instance;

        public static bool TryGet(string pointName, out Transform point)
        {
            point = instance != null ? instance.Find(pointName) : null;
            return point != null;
        }

#if UNITY_EDITOR
        // Edit mode has no Awake, so look the root up directly. Play mode keeps using TryGet.
        public static bool TryGetInEditor(string pointName, out Transform point)
        {
            var root = instance != null ? instance : FindAnyObjectByType<GamePoints>();
            point = root != null ? root.Find(pointName) : null;
            return point != null;
        }
#endif

        private void Awake() => instance = this;

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private Transform Find(string pointName)
        {
            foreach (var child in GetComponentsInChildren<Transform>(true))
                if (child != transform && child.name == pointName) return child;
            return null;
        }
    }
}
