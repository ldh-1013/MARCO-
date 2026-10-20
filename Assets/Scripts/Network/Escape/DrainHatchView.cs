using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Escape
{
    // Placeholder drain hatch (flat cube). Gray until the last survivor phase activates it, then
    // fades from cyan to green with progress. Also the position source for server range checks.
    public sealed class DrainHatchView : MonoBehaviour
    {
        private static readonly Dictionary<DrainHatchId, DrainHatchView> views = new();
        private static readonly Color activeColor = new(0.2f, 0.8f, 0.9f);

        [SerializeField] private DrainHatchId id;
        [SerializeField] private Renderer body;

        public DrainHatchId Id => id;
        public static IEnumerable<DrainHatchView> All => views.Values;

        public static bool TryGet(DrainHatchId hatchId, out DrainHatchView view) => views.TryGetValue(hatchId, out view);

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
            bool active = round != null && round.TryGetActiveDrainHatch(out var activeId) && activeId == id;
            body.material.color = active ? Color.Lerp(activeColor, Color.green, round.DrainHatchProgress) : Color.gray;
        }
    }
}
