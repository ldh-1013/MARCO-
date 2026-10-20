using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Valves
{
    // Valve visual (NetworkTest: cube + bar; pool map: the map's wheel + a status lamp). Reads
    // NetworkRound's synced snapshot: handle angle follows progress, body color follows status.
    // Also the position source for server range checks.
    public sealed class ValveView : MonoBehaviour
    {
        private const float HandleTurnsAtFullProgress = 2f;
        private static readonly Dictionary<ValveId, ValveView> views = new();

        [SerializeField] private ValveId id;
        [SerializeField] private Transform handle;
        // Handle-local spin axis: up for the NetworkTest bar, forward for the map's wheel.
        [SerializeField] private Vector3 handleAxis = Vector3.up;
        [SerializeField] private Renderer body;

        public ValveId Id => id;
        public static IEnumerable<ValveView> All => views.Values;

        public static bool TryGet(ValveId valveId, out ValveView view) => views.TryGetValue(valveId, out view);

        // Horizontal distance only: capsule centers sit higher than the valve cube.
        public bool IsInRange(Vector3 position, float range)
        {
            var offset = position - transform.position;
            offset.y = 0;
            return offset.sqrMagnitude <= range * range;
        }

        private void OnEnable() => views[id] = this;

        private void OnDisable()
        {
            if (views.TryGetValue(id, out var view) && view == this) views.Remove(id);
        }

        private void Update()
        {
            var round = NetworkRound.Instance;
            ValveSnapshot state = default;
            bool known = round != null && round.TryGetValveState(id, out state);

            handle.localRotation = Quaternion.AngleAxis(state.Progress * 360f * HandleTurnsAtFullProgress, handleAxis);
            body.material.color = !known || !state.Active ? Color.gray : state.Status switch
            {
                ValveStatus.Rotating => Color.yellow,
                ValveStatus.Open => Color.green,
                ValveStatus.Reflowing => new Color(1f, 0.5f, 0f),
                _ => Color.white,
            };
        }
    }
}
