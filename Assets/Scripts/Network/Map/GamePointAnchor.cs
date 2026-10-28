using UnityEngine;

namespace Marco.Network.Map
{
    // Snaps this object onto the GamePoints marker with the same name at startup, so valves,
    // exits, drain hatches and spawns follow the markers instead of hard-coded positions.
    // In the editor (not playing) it also follows the marker live, so the Scene view shows the
    // models where the game will put them and dragging a marker drags its model along.
    [ExecuteAlways]
    public sealed class GamePointAnchor : MonoBehaviour
    {
        [SerializeField] private string pointName;
        // World-space offset from the marker, e.g. raising a 1 m valve cube so it rests on the floor.
        [SerializeField] private Vector3 offset;

        // Start, not Awake: by now every GamePoints in the scene has registered itself.
        private void Start()
        {
            if (!Application.isPlaying) return;
            if (GamePoints.TryGet(pointName, out var point))
                transform.SetPositionAndRotation(point.position + offset, point.rotation);
            else
                Debug.LogWarning($"[MARCO][Map] GamePoint '{pointName}' not found for '{name}'");
        }

#if UNITY_EDITOR
        // Edit mode only. Writes only when the pose differs, so an unchanged scene is not marked dirty.
        private void Update()
        {
            if (Application.isPlaying || !GamePoints.TryGetInEditor(pointName, out var point)) return;
            var position = point.position + offset;
            if (transform.position == position && transform.rotation == point.rotation) return;
            transform.SetPositionAndRotation(position, point.rotation);
        }
#endif
    }
}
