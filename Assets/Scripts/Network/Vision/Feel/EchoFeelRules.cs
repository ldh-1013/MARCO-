using UnityEngine;

namespace Marco.Network.Vision
{
    // VB B-3 연출.md 7.1 · 7.2 — the eliminated Echo's return from the tag blackout, and the knock / lure tremor. Pure; the
    // callers read only local state (TagBlackoutLocal.Elapsed, the knock HUD's nudge). The escape fade (연출 5) is
    // EscapeFade and stays as it is (1.0 s cyan, 0.5 s reveal, smootherstep, no blackout).
    // [GAP 연출 고유 시작값 — 연출.md 16.2] every number below.
    public static class EchoFeelRules
    {
        // ── 7.1 전환 직후: the sonar view opens over 0.5 s, ease-out (most of it at once, then it settles) instead of the
        // linear clearing; the cover stays black (shade 0) as before.
        public const float RevealSeconds = .5f;

        // Cover opacity `t` seconds after the 3.0 s blackout ended (1 at 0, 0 at RevealSeconds).
        public static float RevealCover(float t)
        {
            if (t <= 0f) return 1f;
            if (t >= RevealSeconds) return 0f;
            return 1f - FeelRules.EaseOut(t / RevealSeconds);
        }

        // "시야가 넓어지는" — FOV −2° while the screen is covered, back to 0 over the same 0.5 s (ease-out). Never on the
        // escapees' transition (no blackout there).
        public const float WidenFov = -2f;
        public static float RevealFov(float sinceBlackoutEnd)
        {
            if (sinceBlackoutEnd >= RevealSeconds) return 0f;
            if (sinceBlackoutEnd <= 0f) return WidenFov * FeelRules.FeelIntensity;
            return WidenFov * (1f - FeelRules.EaseOut(sinceBlackoutEnd / RevealSeconds)) * FeelRules.FeelIntensity;
        }

        // ── 7.2 노크 · 유인 성공: 0.1 s (spec), ≤ 0.12° and ≤ 2 mm, low-frequency noise under a fast-in, critically damped
        // envelope (no jump at the start or the end). The lowest priority in the stack (×0.4 under any other one-shot).
        public const float KnockSeconds = .1f, KnockRiseSeconds = .015f, KnockDegrees = .12f, KnockMeters = .002f, KnockHz = 25f;

        public static float KnockEnvelope(float t) => FeelRules.Impulse(t, KnockRiseSeconds, KnockSeconds);

        public static Vector3 KnockRotation(float t)
        {
            float e = KnockEnvelope(t);
            if (e <= 0f) return Vector3.zero;
            var n = new Vector2(BreathFeelRules.Noise(t * KnockHz, 5f), BreathFeelRules.Noise(t * KnockHz, 6f));
            if (n.sqrMagnitude > 1f) n.Normalize();
            return new Vector3(n.x, n.y, 0f) * (KnockDegrees * e * FeelRules.FeelIntensity);
        }

        public static Vector3 KnockPosition(float t)
        {
            float e = KnockEnvelope(t);
            if (e <= 0f) return Vector3.zero;
            var n = new Vector2(BreathFeelRules.Noise(t * KnockHz, 7f), BreathFeelRules.Noise(t * KnockHz, 8f));
            if (n.sqrMagnitude > 1f) n.Normalize();
            return new Vector3(n.x, n.y, 0f) * (KnockMeters * e * FeelRules.FeelIntensity);
        }
    }
}
