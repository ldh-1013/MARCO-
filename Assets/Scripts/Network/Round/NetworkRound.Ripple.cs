using System;
using Marco.Core.WinCondition;
using Marco.Network.Escape;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Round
{
    // Server-side ripple additions (2026-11-07 기획서 대조). Plain methods only — no remote calls and no synced state —
    // each behind its RippleServerFeatures switch and wrapped so that nothing escapes into the round tick
    // or a Core event: a failure is logged once per kind and the pulse is skipped.
    public sealed partial class NetworkRound
    {
        private static readonly System.Collections.Generic.HashSet<string> rippleFailures = new();

        private void RippleSafe(string where, Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                if (rippleFailures.Add(where + "|" + exception.GetType().Name)) Debug.LogException(exception, this);
            }
        }
        // S2: valve A rotates inside machine ambience (× 0.5 → 6m); the others and every reflow stay 12m.
        private float ValveSoundRadius(Marco.Core.WinCondition.ValveId id) =>
            RippleServerFeatures.ValveAMachineRadius && id == Marco.Core.WinCondition.ValveId.A ? PulseSpec.ValveAMachine : PulseSpec.Valve;

        // S4: the tag that turns a Runner into an Echo sounds once at the tag point, Shout grade (§3.1-1).
        // Called only on the Echoed path of Hit — a first hit (Wounded) and cheat tags make no pulse.
        private void EmitTagPulse(NetworkPlayer target)
        {
            if (!RippleServerFeatures.TagPulse || target == null) return;
            Vector3 point = target.transform.position;
            RippleSafe("TagPulse", () => EmitSound("TagPulse", point, PulseSpec.Shout, 0f, PulseSpec.ShoutSeconds,
                playAudio: false, forcePulse: true));
        }

        // ITEMS
    }
}
