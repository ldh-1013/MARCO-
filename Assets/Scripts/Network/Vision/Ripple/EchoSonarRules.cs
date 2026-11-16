using System;
using System.Collections.Generic;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Vision
{
    // 기획서 3.2-1 정보량 표(379-380행) · 부록 A [메아리] [11-16]: on an Echo body's screen a sonar ring is shown for its own
    // duration (×1.0), fading from MaxAlpha down to the residual start alpha R instead of to 0; then it stays as a residual
    // trace (its last shape and radius, not expanding) that fades from R to 0 over EchoSonarResidualSeconds. A ring and its
    // residual are one slot of the "최근 8개" cap. Pure — WalkingFootRings draws with these numbers.
    public static class EchoSonarRules
    {
        // [GAP] 기획서에 없음 — 잔상(16.4) 15%를 차용, 11-16 (RippleVisualSettings.EchoResidualAlpha).
        public static float ResidualStartAlpha => RippleVisualSettings.EchoResidualAlpha;
        public static float MaxAlpha => RippleVisualSettings.MaxAlpha;
        public static float ResidualSeconds => PulseSpec.EchoSonarResidualSeconds;

        // An expanding sound ring's alpha at t = 0 … 1 through its own duration (fade-in not included). Survivor / Seeker
        // screens: MaxAlpha × (1 − t)^FadePower, unchanged (reaches 0). Echo sonar: R + (MaxAlpha − R) × (1 − t)^FadePower.
        public static float RingAlpha(float t, bool sonar)
        {
            t = Mathf.Clamp01(t);
            float tail = Mathf.Pow(1f - t, RippleVisualSettings.FadePower);
            if (!sonar) return RippleVisualSettings.MaxAlpha * tail;
            float r = ResidualStartAlpha;
            return r + (MaxAlpha - r) * tail;
        }

        // R × (1 − s / EchoSonarResidualSeconds), s = seconds since the ring ended; 0 from the end on.
        public static float ResidualAlpha(float s)
        {
            if (s <= 0f) return ResidualStartAlpha;
            return ResidualStartAlpha * Math.Max(0f, 1f - s / ResidualSeconds);
        }

        public static bool ResidualDone(float s) => s >= ResidualSeconds;

        // The ring to remove so a new sound ring fits the cap: the oldest sound slot (a ring or its residual) when the
        // sound slots already fill it; -1 when there is room. isSound is in creation order (oldest first).
        public static int SlotToEvict(IReadOnlyList<bool> isSound, int cap)
        {
            int count = 0, oldest = -1;
            for (int i = 0; i < isSound.Count; i++)
                if (isSound[i]) { count++; if (oldest < 0) oldest = i; }
            return count < cap ? -1 : oldest;
        }
    }
}
