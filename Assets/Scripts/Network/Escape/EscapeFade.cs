using UnityEngine;

namespace Marco.Network.Escape
{
    // Escape presentation timeline (연출.md 5 · 기획서 3.3-3 · 부록 A [탈출자]), pure so tests can pin it.
    //   0.0 – 1.0 s  screen fades to cyan #7FE7E0 (smootherstep, fully covered at 1.0 s)
    //   1.0 s        the body becomes the Echo body under full cover (no blackout)
    //   1.0 – 1.5 s  cyan clears and the Echo view opens; 0.5 s is 연출.md 7.1's step to the self glow
    public static class EscapeFade
    {
        public const float FadeOutSeconds = 1.0f;
        public const float RevealSeconds = 0.5f;
        public const float TotalSeconds = FadeOutSeconds + RevealSeconds;

        // #7FE7E0 (16.1 탈출자 / 연출.md 5).
        public static readonly Color Cyan = new(0x7F / 255f, 0xE7 / 255f, 0xE0 / 255f, 1f);

        public static bool HasSwitchedBody(float elapsed) => elapsed >= FadeOutSeconds;
        public static bool IsFinished(float elapsed) => elapsed >= TotalSeconds;

        // Overlay opacity at a time since the escape was judged.
        public static float Alpha(float elapsed)
        {
            if (elapsed <= 0f) return 0f;
            if (elapsed < FadeOutSeconds) return Smootherstep(elapsed / FadeOutSeconds);
            return Reveal(1f, elapsed - FadeOutSeconds);
        }

        // Clearing from any opacity (also used when the round ends mid-fade and the result screen takes over).
        public static float Reveal(float from, float elapsed)
        {
            if (elapsed <= 0f) return Mathf.Clamp01(from);
            if (elapsed >= RevealSeconds) return 0f;
            return Mathf.Clamp01(from) * (1f - Smootherstep(elapsed / RevealSeconds));
        }

        private static float Smootherstep(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }
    }
}
