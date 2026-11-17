using UnityEngine;

namespace Marco.Network.Vision
{
    // VB B-4 연출.md 8.1 · 8.2 — the Runner's own screen around the shout and the breath gauge. Pure functions; the overlays
    // read only what the owner already has (PlayerMovement.BreathSeconds / IsDiving / SwimPenaltyRemaining). Edge effects
    // reuse the A-0 mask (EdgeStain); the centre 70 % is never touched.
    // [GAP 연출 고유 시작값 — 연출.md 16.2] every number below.
    public static class BreathFeelRules
    {
        // ── 8.1 억제 성공: a faint black contraction. The suppression window itself is not readable on the client (only the
        // spent breath is), so it is a 0.5 s pulse: 0.2 s ease-out in, 0.3 s out (fast in, slow out; the directive's
        // "0.3 s in" does not fit a 0.5 s pulse together with the 0.3 s fade).
        public const float SuppressMaxAlpha = .10f, SuppressCornerDepth = .09f, SuppressEdgeDepth = .035f;
        public const float SuppressInSeconds = .2f, SuppressOutSeconds = .3f;
        public const float SuppressSeconds = SuppressInSeconds + SuppressOutSeconds;
        // The suppression spends 4.5 s of breath at once (부록 A [숨 게이지]); out of the water nothing else lowers the gauge
        // (diving drains 1 / s), and the 0.1 s sync can add at most 0.4 of recovery in between.
        public const float SuppressDropSeconds = 4f;

        public static bool Suppressed(float previousBreath, float breath, bool diving) =>
            !diving && previousBreath - breath >= SuppressDropSeconds;

        public static float SuppressAlpha(float t)
        {
            if (t < 0f || t >= SuppressSeconds) return 0f;
            float a = t < SuppressInSeconds ? FeelRules.EaseOut(t / SuppressInSeconds)
                : 1f - FeelRules.EaseInOut((t - SuppressInSeconds) / SuppressOutSeconds);
            return SuppressMaxAlpha * a * FeelRules.FeelIntensity;
        }

        // ── 8.2 질식: the red edge closes in fast, then hands over to whatever the health edge shows now. The suffocation does
        // not lower the health (기획서 5.9-1: forced surfacing, −20 % for 3 s, a shout-grade pulse) — healthy, it simply fades.
        // Depth 9 % → 22 % (corner) / 3.5 % → 9 % (edge middle) in 0.25 s ease-out, opacity up to 0.55, then 0.25 s back to
        // the health edge (the directive's 0.4 s hand-over would end at 0.65 s, past its own "0.5 s in total").
        public const float SuffocationAlpha = .55f, SuffocationCloseSeconds = .25f, SuffocationHandSeconds = .25f;
        public const float SuffocationSeconds = SuffocationCloseSeconds + SuffocationHandSeconds;
        public static readonly Vector2 SuffocationStartDepth = new(.09f, .035f), SuffocationPeakDepth = new(.22f, .09f);
        // The forced-surfacing slow (3 s) starts with the suffocation: its first sync jumps from 0 to ~3 s.
        public const float SuffocationPenaltyEdge = 2f;

        public static bool Suffocated(float previousPenalty, float penalty) =>
            previousPenalty <= .05f && penalty >= SuffocationPenaltyEdge;

        // 0 → 1 while closing, 1 → 0 while handing over; 0 outside. One rise and one fall: a single peak.
        public static float SuffocationWeight(float t)
        {
            if (t < 0f || t >= SuffocationSeconds) return 0f;
            return t < SuffocationCloseSeconds ? FeelRules.EaseOut(t / SuffocationCloseSeconds)
                : 1f - FeelRules.EaseInOut((t - SuffocationCloseSeconds) / SuffocationHandSeconds);
        }

        // The single stain to draw while it runs: from the health edge's own value (no health swell meanwhile — merged)
        // to the suffocation peak and back.
        public static float MixAlpha(float healthAlpha, float t) => Mathf.Lerp(healthAlpha, SuffocationAlpha * FeelRules.FeelIntensity, SuffocationWeight(t));
        // Closing starts from the 9 % / 3.5 % ring (or the health edge where that is already deeper); the hand-over ends on
        // the health edge's own depth (healthy: the 9 % / 3.5 % ring, faded out).
        public static Vector2 MixDepth(Vector2 healthDepth, bool healthShown, float t)
        {
            Vector2 start = healthShown ? Vector2.Max(healthDepth, SuffocationStartDepth) : SuffocationStartDepth;
            Vector2 end = healthShown ? healthDepth : SuffocationStartDepth;
            if (t < SuffocationCloseSeconds)
                return Vector2.Lerp(start, SuffocationPeakDepth, FeelRules.EaseOut(Mathf.Max(0f, t) / SuffocationCloseSeconds));
            return Vector2.Lerp(end, SuffocationPeakDepth, SuffocationWeight(t));
        }

        // ── 8.2 부상 후: a slow breathing sway while the forced-surfacing slow lasts (SwimPenaltyRemaining > 0). Low-frequency
        // value noise (no sine), ≤ 0.15° · ≤ 3 mm, faded in and out over 0.3 s.
        public const float SwayRotationDegrees = .15f, SwayPositionMeters = .003f, SwayHz = .33f, SwayFadeSeconds = .3f;

        public static float SwayFade(float weight, bool on, float deltaTime) =>
            Mathf.MoveTowards(weight, on ? 1f : 0f, Mathf.Max(0f, deltaTime) / SwayFadeSeconds);

        // Smooth 1-D value noise in [−1, 1] (hash lattice + smoothstep), period-free.
        public static float Noise(float x, float seed)
        {
            float i = Mathf.Floor(x), f = x - i;
            float a = Hash(i + seed * 17.13f), b = Hash(i + 1f + seed * 17.13f);
            float s = f * f * (3f - 2f * f);
            return Mathf.Lerp(a, b, s) * 2f - 1f;
        }

        private static float Hash(float n) { float s = Mathf.Sin(n * 12.9898f) * 43758.5453f; return s - Mathf.Floor(s); }

        public static Vector3 SwayRotation(float time, float weight)
        {
            float w = FeelRules.EaseInOut(weight) * FeelRules.FeelIntensity;
            return new Vector3(Noise(time * SwayHz, 1f), Noise(time * SwayHz, 2f) * .6f, 0f) * (SwayRotationDegrees * w);
        }

        public static Vector3 SwayPosition(float time, float weight)
        {
            float w = FeelRules.EaseInOut(weight) * FeelRules.FeelIntensity;
            return new Vector3(0f, Noise(time * SwayHz, 3f), 0f) * (SwayPositionMeters * w);
        }
    }
}
