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

        // S5: when the gate latches open, both exits sound once, Shout grade (§10.5).
        internal void EmitGatePulses()
        {
            if (!RippleServerFeatures.GatePulse) return;
            RippleSafe("GatePulse", () =>
            {
                foreach (var exit in ExitView.All)
                    if (exit != null)
                        EmitSound("GatePulse", exit.transform.position, PulseSpec.Shout, 0f, PulseSpec.ShoutSeconds, playAudio: false, forcePulse: true);
            });
        }

        // S6: entering the last-survivor phase sounds once at the active drain hatch, Shout grade (§6.5-1).
        internal void EmitSurvivorPulse(DrainHatchId id)
        {
            if (!RippleServerFeatures.SurvivorPulse) return;
            RippleSafe("SurvivorPulse", () =>
            {
                if (DrainHatchView.TryGet(id, out var hatch) && hatch != null)
                    EmitSound("SurvivorPulse", hatch.transform.position, PulseSpec.Shout, 0f, PulseSpec.ShoutSeconds, playAudio: false, forcePulse: true);
            });
        }

        // S7: while the active drain hatch is being worked it pulses like an underwater valve, 12m every
        // 2.5s (§6.5-3 · §6.1); the water surface halves it for listeners above (SoundWorld).
        private float nextDrainPulse;
        private void TickDrainWorkPulse()
        {
            if (!RippleServerFeatures.DrainWorkPulse) return;
            RippleSafe("DrainWorkTick", () =>
            {
                var phase = round?.LastSurvivorPhase;
                if (phase == null || !(phase.ActiveDrainHatchId is DrainHatchId id) || phase.GetDrainHatch(id).WorkerCount == 0)
                {
                    nextDrainPulse = 0;
                    return;
                }
                if (Time.time < nextDrainPulse) return;
                nextDrainPulse = Time.time + PulseSpec.DrainWorkPeriod;
                EmitDrainWorkPulse(id);
            });
        }

        private void EmitDrainWorkPulse(DrainHatchId id)
        {
            if (!RippleServerFeatures.DrainWorkPulse) return;
            RippleSafe("DrainWorkPulse", () =>
            {
                if (DrainHatchView.TryGet(id, out var hatch) && hatch != null)
                    EmitSound("DrainWork", hatch.transform.position, PulseSpec.DrainWork, 0f, PulseSpec.DrainWorkPeriod,
                        underwater: true, playAudio: false, forcePulse: true);
            });
        }

        // S8: the 0:30 exit pulses go through the normal judged path — distance, walls, Seeker × 1.2 — like
        // every other pulse (§6.2-1). The temporary all-client RPC stays in place for the switch.
        private void EmitExitPulsesJudged()
        {
            RippleSafe("ExitPulse", () =>
            {
                foreach (var exit in ExitView.All)
                    if (exit != null)
                        EmitSound("ExitPulse", exit.transform.position, PulseSpec.Shout, 0f, PulseSpec.ShoutSeconds, playAudio: false, forcePulse: true);
            });
        }

        // ITEMS
    }
}
