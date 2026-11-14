namespace Marco.Network.Round
{
    // One switch per server-side ripple change (2026-11-07 기획서 대조). Setting a switch to false restores
    // exactly the behaviour before that change — no revert needed. Server-only constants; each item adds
    // its own switch in its own commit.
    public static class RippleServerFeatures
    {
        public const bool VoiceActivityGate = false;     // D1  band-pass / noise floor / 0.15s gate on voice send + own ring - OFF until the voice regression
        public const bool VoiceRepulseOnChange = true;   // S9  voice re-pulse on a held grade change, >= 0.15s apart (§5.2 ⑦ ⑨)
        public const bool ExitPulseJudged = true;        // S8  0:30 exit pulses through the judged EmitSound path (§6.2-1)
        public const bool DrainWorkPulse = true;         // S7  12m pulse every 2.5s while the active hatch is worked (§6.5-3)
        public const bool DrainWorkAudible = true;       // S7  the drain work pulse also sounds, like the underwater valve (§6.5-3 no silent escape)
        public const bool SurvivorPulse = true;          // S6  Shout pulse at the active drain hatch on last-survivor entry (§6.5-1)
        public const bool GatePulse = true;              // S5  Shout pulse at both exits when the gate opens (§10.5)
        public const bool TagPulse = true;               // S4  Shout pulse when a tag turns a Runner into an Echo (§3.1-1)
        public const bool DrowningOcclusion = true;      // S3  no drowning ring through walls (§5.4)
        public const bool ValveAMachineRadius = true;    // S2  valve A rotation 6m in machine ambience (§6.1-0)
        public const bool BreathPauseWhenLocked = false; // S11 off (11-14): §3.6 · 3.3-2 · 부록 A — treatment, bandage (and the 2 s sound-can placement) do not pause the breath timer
        public const bool BreathAudible = true;          // S1  breath heard by others, 3m → +2m/15s → 9m (부록 A [정지 감지])
    }
}
