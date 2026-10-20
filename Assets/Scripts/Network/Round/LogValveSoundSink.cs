using Marco.Core.WinCondition;
using UnityEngine;

namespace Marco.Network.Round
{
    // Placeholder IValveSoundSink until the sound/ripple part (남경) provides the real one.
    public sealed class LogValveSoundSink : IValveSoundSink
    {
        public void NotifyValveSound(ValveId valveId, ValveSoundEvent soundEvent) =>
            Debug.Log($"[MARCO] Valve sound {valveId}: {soundEvent}");
    }
}
