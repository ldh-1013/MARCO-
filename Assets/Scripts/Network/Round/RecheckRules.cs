using UnityEngine;

namespace Marco.Network.Round
{
    // 부록 A [사운드 차폐] 차폐 재판정 주기 · 재판정 상향 규칙 [11-16]: a listener's state for one pulse.
    public enum HearState : byte { None = 0, Muffled = 1, Clear = 2 }

    // 부록 A [사운드 차폐] 재판정 상향 규칙 [11-16]: re-judge only upward (못 들음 → 먹먹하게 들음 → 선명히 들음); an upgrade
    // sends the remaining duration and the elapsed time, and a clear ring is drawn from the elapsed phase.
    public static class RecheckRules
    {
        public static HearState State(bool heard, bool muffled) =>
            !heard ? HearState.None : muffled ? HearState.Muffled : HearState.Clear;

        public static bool Upgrade(HearState old, HearState now) => now > old;

        // total = this listener's perceived duration in the new state (PulseSpec.ListenerSeconds, occlusion scale included).
        public static float Remaining(float total, float elapsed) => total - elapsed;

        public static float Phase(float total, float elapsed) => total > 0f ? Mathf.Clamp01(elapsed / total) : 1f;

        public static bool Send(HearState old, HearState now, float total, float elapsed) =>
            Upgrade(old, now) && Remaining(total, elapsed) > 0f;

        // Who is followed after the emission: not the source itself, not an Echo body (it ignores occlusion — clear from the
        // start), not a looping sound (a rotating valve is re-emitted and re-judged every 0.25 s by its own tick), not a
        // self-only sound, and not someone who already hears it clearly.
        public static bool Tracks(bool own, bool echoBody, bool looping, bool selfOnly, HearState initial) =>
            !own && !echoBody && !looping && !selfOnly && initial != HearState.Clear;
    }
}
