namespace Marco.Network.Round
{
    // One switch per server-side ripple change (2026-11-07 기획서 대조). Setting a switch to false restores
    // exactly the behaviour before that change — no revert needed. Server-only constants; each item adds
    // its own switch in its own commit.
    public static class RippleServerFeatures
    {
        public const bool DrainWorkPulse = true;         // S7  12m pulse every 2.5s while the active hatch is worked (§6.5-3)
        public const bool SurvivorPulse = true;          // S6  Shout pulse at the active drain hatch on last-survivor entry (§6.5-1)
        public const bool GatePulse = true;              // S5  Shout pulse at both exits when the gate opens (§10.5)
        public const bool TagPulse = true;               // S4  Shout pulse when a tag turns a Runner into an Echo (§3.1-1)
        public const bool DrowningOcclusion = true;      // S3  no drowning ring through walls (§5.4)
        public const bool ValveAMachineRadius = true;    // S2  valve A rotation 6m in machine ambience (§6.1-0)
        public const bool BreathPauseWhenLocked = true;  // S11 breath timer pauses while the server holds the player in place (treatment lock)
        public const bool BreathAudible = true;          // S1  breath heard by others, 3m → +2m/15s → 9m (부록 A [정지 감지])
    }
}
