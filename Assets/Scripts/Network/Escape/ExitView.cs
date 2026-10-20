using System.Collections.Generic;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Escape
{
    public enum ExitId : byte { MainGate, StaffDoor }

    // Placeholder exit (tall cube). Red while the gate is closed, green once it opens.
    // Also the position source for the server's escape range check.
    public sealed class ExitView : MonoBehaviour
    {
        private static readonly Dictionary<ExitId, ExitView> views = new();
        private static readonly Color closedColor = new(0.5f, 0.1f, 0.1f);

        [SerializeField] private ExitId id;
        [SerializeField] private Renderer body;

        public ExitId Id => id;

        public static bool AnyInRange(Vector3 position, float range)
        {
            foreach (var view in views.Values)
                if (view.IsInRange(position, range)) return true;
            return false;
        }

        // Horizontal distance only, like ValveView.
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
            body.material.color = round != null && round.GateOpen ? Color.green : closedColor;
        }
    }
}
