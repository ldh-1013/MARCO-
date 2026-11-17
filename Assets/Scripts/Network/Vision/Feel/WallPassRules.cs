using UnityEngine;

namespace Marco.Network.Vision
{
    // V31 연출.md 2.2 벽 통과(메아리) — 통과 순간 0.1초 화이트 노이즈 오버레이. Pure rules: one effect per entry into a
    // structure (the SoundWorld classification — walls, storey slabs, locker rows, 차폐 파트; furniture and props are not
    // structures), nothing while staying inside, again after leaving and re-entering.
    // [GAP 연출 고유 시작값 — 연출.md 16.2]: max opacity 0.25, 20 ms in, 80 ms out, edge mask 15 % / 6 % (A-0 shape).
    public static class WallPassRules
    {
        public const float MaxAlpha = .25f, InSeconds = .02f, OutSeconds = .08f;
        public const float TotalSeconds = InSeconds + OutSeconds;
        // The camera probe: a small sphere at the eye (Physics.OverlapSphereNonAlloc, triggers ignored).
        public const float ProbeRadius = .1f;

        public static bool Entered(bool wasInside, bool isInside) => isInside && !wasInside;

        // Overlay opacity t seconds after an entry: fast ease-out in, ease-out fall (no hold, no pulse).
        public static float Alpha(float t)
        {
            if (t < 0f || t >= TotalSeconds) return 0f;
            float a = t < InSeconds ? FeelRules.EaseOut(t / InSeconds) : 1f - FeelRules.EaseOut((t - InSeconds) / OutSeconds);
            return MaxAlpha * a * FeelRules.FeelIntensity;
        }
    }
}
