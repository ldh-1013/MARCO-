using UnityEngine;

namespace Marco.Network.Vision
{
    // V36 파트 A 연출.md 8.1 억제 실패 — "화면 가장자리 백색 섬광 1회", the Runner's own screen only. The signal is
    // PlayerMovement.LocalScreamFlash (a read-only event raised where the server's own scream message reaches this client,
    // self && scream); the drawing is the A-0 edge mask (EdgeStain). Pure curve here.
    // [GAP 연출 고유 시작값 — 연출.md 16.2]: opacity 0.30, 40 ms in · 160 ms out (0.2 s in total), depth 9 % / 3.5 %.
    // The colour is the 4.2 tag flash's white (TagBlackoutLocal: shade 1) — no new colour.
    public static class ScreamFlashRules
    {
        public const float MaxAlpha = .30f, InSeconds = .04f, OutSeconds = .16f;
        public const float TotalSeconds = InSeconds + OutSeconds;
        public const float CornerDepth = .09f, EdgeDepth = .035f;
        public static readonly Color Colour = Color.white;

        // Fast ease-out in, ease-out fall (slow tail) — one rise, one fall, no pulse.
        public static float Alpha(float t)
        {
            if (t < 0f || t >= TotalSeconds) return 0f;
            float a = t < InSeconds ? FeelRules.EaseOut(t / InSeconds) : 1f - FeelRules.EaseOut((t - InSeconds) / OutSeconds);
            return MaxAlpha * a * FeelRules.FeelIntensity;
        }
    }
}
