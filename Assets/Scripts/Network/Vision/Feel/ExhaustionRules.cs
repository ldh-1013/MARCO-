using UnityEngine;

namespace Marco.Network.Vision
{
    // V31 B-1 연출.md 2.2 스태미나 소진 — the local Runner's own screen only. While the sprint is exhausted
    // (PlayerMovement.IsSprintExhausted, the value the owner's own movement already uses) the sprint FOV is back at 0 and
    // a faint black contraction sits at the screen edge (the A-0 mask shape); it fades in and out over 0.3 s, no pulse.
    // A persistent state display, so the 0.5 s screen-effect rule does not apply.
    // [GAP 연출 고유 시작값 — 연출.md 16.2]: black, max opacity 0.12, depth corner 9 % / edge middle 3.5 %, 0.3 s ease-out
    // in and out, sprint FOV held down for 0.1 s after the exhaustion ends.
    public static class ExhaustionRules
    {
        public const float MaxAlpha = .12f, CornerDepth = .09f, EdgeDepth = .035f, FadeSeconds = .3f;
        // The sprint FOV may come back only this long after the exhaustion ended: a client's stamina is corrected by the
        // server every 0.1 s, so a correction landing in the very frame the exhaustion starts must not lift the FOV again.
        public const float SprintFovHoldSeconds = .1f;
        public static readonly Color Colour = Color.black;

        // The contraction's opacity `since` seconds after the state last changed, starting from `from` (the opacity shown at
        // that change, so a change mid-fade continues smoothly). Ease-out, frame-rate independent.
        public static float Alpha(float from, bool exhausted, float since) =>
            FeelRules.Follow(from, exhausted ? MaxAlpha * FeelRules.FeelIntensity : 0f, since, FadeSeconds);

        // Whether the sprint FOV may be raised now. `sinceExhausted` = seconds since the exhaustion was last seen
        // (0 while exhausted, negative / huge when never).
        public static bool SprintFovAllowed(bool sprinting, bool exhausted, float sinceExhausted) =>
            sprinting && !exhausted && !(sinceExhausted >= 0f && sinceExhausted < SprintFovHoldSeconds);
    }
}
