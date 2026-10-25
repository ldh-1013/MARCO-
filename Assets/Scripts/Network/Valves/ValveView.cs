using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Valves
{
    // Valve visual (NetworkTest: cube + bar; pool map: the pipe model's wheel + a status lamp). Reads
    // NetworkRound's synced snapshot: wheel angle follows progress, body color follows status.
    // Also the position source for server range checks.
    public sealed class ValveView : MonoBehaviour
    {
        private const float HandleTurnsAtFullProgress = 2f;
        private static readonly Dictionary<ValveId, ValveView> views = new();

        [SerializeField] private ValveId id;
        // The parts spin together about the pivot, keeping their own rest rotation: the pipe model's
        // wheel, spokes and hub are siblings with an import rotation, not children of one handle.
        [SerializeField] private Transform pivot;
        // Pivot-local spin axis: up for the NetworkTest bar, forward for the pipe's wheel.
        [SerializeField] private Vector3 spinAxis = Vector3.up;
        [SerializeField] private Transform[] spinParts;
        [SerializeField] private Renderer body;

        // Each part's pose relative to the pivot (unscaled), taken before any spin.
        private Vector3[] restOffsets;
        private Quaternion[] restRotations;

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

        private void Awake()
        {
            var toPivot = Quaternion.Inverse(pivot.rotation);
            restOffsets = new Vector3[spinParts.Length];
            restRotations = new Quaternion[spinParts.Length];
            for (int i = 0; i < spinParts.Length; i++)
            {
                restOffsets[i] = toPivot * (spinParts[i].position - pivot.position);
                restRotations[i] = toPivot * spinParts[i].rotation;
            }
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

            var spin = pivot.rotation * Quaternion.AngleAxis(state.Progress * 360f * HandleTurnsAtFullProgress, spinAxis);
            for (int i = 0; i < spinParts.Length; i++)
                spinParts[i].SetPositionAndRotation(pivot.position + spin * restOffsets[i], spin * restRotations[i]);
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
