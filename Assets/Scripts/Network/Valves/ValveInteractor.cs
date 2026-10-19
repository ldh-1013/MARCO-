using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Valves
{
    // On the player prefab. The owner holds E near a valve → ServerRpc; the server re-checks range,
    // and ValveInteractionService checks the role through IPlayerRoster. Clients decide nothing.
    public sealed class ValveInteractor : NetworkBehaviour
    {
        private ValveId? workingValve;

        public ValveId? WorkingValve => workingValve;

        private void Update()
        {
            if (!IsOwner) return;
            var round = NetworkRound.Instance;
            if (round == null) return;

            bool held = Input.GetKey(KeyCode.E) && Cursor.lockState == CursorLockMode.Locked;
            if (workingValve is ValveId current)
            {
                // Releasing E or walking away both end the work.
                if (!held || !ValveView.TryGet(current, out var view) || !view.IsInRange(transform.position, round.InteractRange))
                {
                    EndWorkServer(current);
                    workingValve = null;
                }
            }
            else if (Input.GetKeyDown(KeyCode.E) && held && TryFindNearest(round.InteractRange, out var nearest))
            {
                // Only on a fresh press: an opened valve drops its workers, and INTERFACES.md §4
                // expects a new TryBeginWork (release and press again) to rework it.
                BeginWorkServer(nearest);
                workingValve = nearest;
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            workingValve = null;
        }

        private bool TryFindNearest(float range, out ValveId nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            foreach (var view in ValveView.All)
            {
                float distance = (view.transform.position - transform.position).sqrMagnitude;
                if (distance >= best || !view.IsInRange(transform.position, range)) continue;
                best = distance;
                nearest = view.Id;
            }
            return best < float.MaxValue;
        }

        [ServerRpc]
        private void BeginWorkServer(ValveId valveId) =>
            NetworkRound.Instance?.TryBeginWork(OwnerId, valveId, transform.position);

        [ServerRpc]
        private void EndWorkServer(ValveId valveId) => NetworkRound.Instance?.EndWork(OwnerId, valveId);
    }
}
