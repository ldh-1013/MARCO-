using UnityEngine;

namespace Marco.Network.Map
{
    // Snaps this object onto the GamePoints marker with the same name at startup, so valves,
    // exits, drain hatches and spawns follow the markers instead of hard-coded positions.
    public sealed class GamePointAnchor : MonoBehaviour
    {
        [SerializeField] private string pointName;
        // World-space offset from the marker, e.g. raising a 1 m valve cube so it rests on the floor.
        [SerializeField] private Vector3 offset;

        // Start, not Awake: by now every GamePoints in the scene has registered itself.
        private void Start()
        {
            if (GamePoints.TryGet(pointName, out var point))
                transform.SetPositionAndRotation(point.position + offset, point.rotation);
            else
                Debug.LogWarning($"[MARCO][Map] GamePoint '{pointName}' not found for '{name}'");
        }
    }
}
