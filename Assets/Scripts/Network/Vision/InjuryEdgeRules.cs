using UnityEngine;

namespace Marco.Network.Vision
{
    // V28 A-0 체력 가장자리 표시 (기획서 §3.3-1 상태 2.0 / 1.5 / 1.0, 색 §16.2). Pure curves and mask shape.
    // [GAP 연출 고유 시작값 — 연출.md 16.1]: depth corner 15% / edge middle 6% of the short side, opacity 1.0 → 0.40,
    // 1.5 → 0.30 (11-17 후속 2), colour correction per state, state change 0.3 s ease-in-out, first-hit swell 0.2 s,
    // pattern drift ≤ 0.02 UV/s, no pulsing.
    public static class InjuryEdgeRules
    {
        public const float CornerDepth = .15f, EdgeDepth = .06f;
        public const float WoundedAlpha = .40f, RecoveringAlpha = .30f;
        // Colour correction toward a low-saturation seep (alpha blended, never additive): desaturate = how far the palette
        // colour moves toward its own luminance, brightness = multiplier. 1.0 상처 (술래 레드) is darker and duller than
        // 1.5 회복 중 (앰버), which must still read as amber (11-17 후속 2).
        public const float WoundedDesaturate = .35f, WoundedBrightness = .60f;
        public const float RecoveringDesaturate = .10f, RecoveringBrightness = .85f;
        public const float ChangeSeconds = .3f, SwellSeconds = .2f, SwellInSeconds = .04f, SwellGain = .6f;
        public const float DriftPerSecond = .01f;   // UV/s, ≤ 0.02
        public const float CentreKeepOut = .70f;    // the middle 70 % (each axis) is never touched

        // 2 halves = 1.0 상처, 3 = 1.5 회복 중, otherwise nothing.
        public static float TargetAlpha(int halves) => halves == 2 ? WoundedAlpha : halves == 3 ? RecoveringAlpha : 0f;

        // (desaturate, brightness) for a state; 2 halves = 1.0, 3 = 1.5. Other states draw nothing (identity).
        public static Vector2 Correction(int halves) =>
            halves == 2 ? new Vector2(WoundedDesaturate, WoundedBrightness)
            : halves == 3 ? new Vector2(RecoveringDesaturate, RecoveringBrightness) : new Vector2(0f, 1f);

        // The stain colour for a state from its §16.2 palette colour (술래 레드 or 앰버, colour-blind variant included).
        public static Color StainColor(Color palette, int halves)
        {
            var correction = Correction(halves);
            float luminance = palette.r * .299f + palette.g * .587f + palette.b * .114f;
            var c = Color.Lerp(palette, new Color(luminance, luminance, luminance), correction.x) * correction.y;
            c.a = 1f;
            return c;
        }

        // Ease-in-out from `from` to `to` over ChangeSeconds since the change; frame-rate independent (time based).
        public static float Transition(float from, float to, float sinceChange)
        {
            float t = Mathf.Clamp01(sinceChange / ChangeSeconds);
            return Mathf.Lerp(from, to, t * t * (3f - 2f * t));
        }

        // What is drawn: a change caused by a hit goes to the new state at once and swells on top of it (the flash settles
        // into the state value — 연출 15.3); any other change (recovery, leaving the round) eases over ChangeSeconds.
        public static float Displayed(float from, float to, float sinceChange, bool hitChange, float sinceHit) =>
            Mathf.Clamp01((hitChange ? to : Transition(from, to, sinceChange)) * (1f + Swell(sinceHit)));

        // First-hit swell on top of the state value: in over 40 ms, out (ease-out) by 0.2 s. 0 outside [0, 0.2].
        public static float Swell(float sinceHit)
        {
            if (sinceHit < 0f || sinceHit >= SwellSeconds) return 0f;
            if (sinceHit < SwellInSeconds) return SwellGain * (sinceHit / SwellInSeconds);
            float t = (sinceHit - SwellInSeconds) / (SwellSeconds - SwellInSeconds);
            return SwellGain * (1f - t) * (1f - t);
        }

        // Mask 0..1 at screen point (u, v in 0..1) for a screen of the given aspect (width / height).
        // Depth grows toward the corners, the boundary is broken by 2-octave low-frequency noise (seeded), and it fades
        // inward with smoothstep. Zero inside the centre 70 %.
        public static float Mask(float u, float v, float aspect, float seed, float drift)
        {
            if (Mathf.Abs(u - .5f) < CentreKeepOut * .5f && Mathf.Abs(v - .5f) < CentreKeepOut * .5f) return 0f;
            float shortSide = Mathf.Min(aspect, 1f);
            // edge distances in short-side units
            float dx = Mathf.Min(u, 1f - u) * aspect / shortSide, dy = Mathf.Min(v, 1f - v) / shortSide;
            float cx = Mathf.Clamp01(1f - Mathf.Min(u, 1f - u) * 2f), cy = Mathf.Clamp01(1f - Mathf.Min(v, 1f - v) * 2f);
            float corner = Mathf.SmoothStep(0f, 1f, cx * cy);
            float n = .65f * Mathf.PerlinNoise(seed + u * 3.1f + drift, seed * .37f + v * 3.1f) +
                      .35f * Mathf.PerlinNoise(seed * 1.7f + u * 7.3f, seed + v * 7.3f - drift);
            float depth = Mathf.Lerp(EdgeDepth, CornerDepth, corner) * Mathf.Lerp(.55f, 1f, n);
            float horizontal = 1f - Mathf.SmoothStep(0f, 1f, dx / depth);
            float vertical = 1f - Mathf.SmoothStep(0f, 1f, dy / depth);
            float m = Mathf.Max(horizontal, vertical);
            return m * m;   // darker toward the very edge
        }
    }
}
