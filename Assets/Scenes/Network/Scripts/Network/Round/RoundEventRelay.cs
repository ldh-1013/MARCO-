using Marco.Core.WinCondition;
using UnityEngine;

namespace Marco.Network.Round
{
    // Server-side IRoundEventSink. Valve status/progress reach clients through NetworkRound's
    // per-tick snapshot, so here only the latched events (gate, result, last survivor phase,
    // active drain hatch) are written to SyncVars.
    public sealed class RoundEventRelay : IRoundEventSink
    {
        private readonly NetworkRound owner;

        public RoundEventRelay(NetworkRound owner) => this.owner = owner;

        // Open (first open or rework) → map-wide 2D cue for everyone; the valve id stays on the server.
        public void OnValveStatusChanged(ValveId valveId, ValveStatus status)
        {
            Debug.Log($"[MARCO] Valve {valveId}: {status}");
            if (status == ValveStatus.Open) owner.ValveOpenedObservers();
        }

        public void OnValveProgressChanged(ValveId valveId, float progress) { }

        public void OnGateOpened()
        {
            Debug.Log("[MARCO] Gate opened");
            owner.MarkGateOpened();
        }

        public void OnRoundEnded(RoundResult result)
        {
            Debug.Log($"[MARCO] Round ended: {result}");
            owner.SetResult(result);
        }

        public void OnLastSurvivorPhaseEntered()
        {
            Debug.Log("[MARCO] Last survivor phase entered");
            owner.MarkLastSurvivorPhase();
        }

        public void OnDrainHatchActivated(DrainHatchId drainHatchId)
        {
            Debug.Log($"[MARCO] Drain hatch activated: {drainHatchId}");
            owner.MarkDrainHatchActivated(drainHatchId);
        }
    }
}
