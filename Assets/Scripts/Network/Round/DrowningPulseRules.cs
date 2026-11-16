using Marco.Network.Player;

namespace Marco.Network.Round
{
    // 부록 A [숨 게이지] 질식 페널티 "고함급(22m) 파문 1회" · §5.7 (1216-1221행) — who gets the drowning ring, and its radius
    // and duration for that listener. Pure; NetworkRound.EmitDrowningSound feeds it SoundWorld's result per listener.
    //   Echo body (탈락 메아리 · 탈출자): always, ignoring distance and occlusion (5.7 1218행 "거리 · 차폐 무시"),
    //     radius = the base 22 m, duration = PulseSpec.ListenerSeconds (the current Echo rule — 3.2-1 fix pending, 개정이력 11-16).
    //   Spectating (난입자 · 관전자): never (V20 지시 — 관전자 제외).
    //   Alive: when heard and — with RippleServerFeatures.DrowningOcclusion — not blocked; radius = the perceived radius
    //     (Seeker ×1.2 included), duration = ListenerSeconds(…, seeker, ring, occlusion factor).
    public static class DrowningPulseRules
    {
        public readonly struct Pulse
        {
            public readonly bool Send;
            public readonly float Radius, Seconds;
            public Pulse(bool send, float radius, float seconds) { Send = send; Radius = radius; Seconds = seconds; }
        }

        private static readonly Pulse None = new(false, 0f, 0f);

        public static Pulse For(PlayerStatus status, bool isSeeker, bool hasEchoBody, bool heard, bool blocked,
            float perceivedRadius, float durationScale) =>
            For(status, isSeeker, hasEchoBody, heard, blocked, perceivedRadius, durationScale, RippleServerFeatures.DrowningOcclusion);

        public static Pulse For(PlayerStatus status, bool isSeeker, bool hasEchoBody, bool heard, bool blocked,
            float perceivedRadius, float durationScale, bool occlusionHidesRing)
        {
            if (status == PlayerStatus.Spectating) return None;
            if (hasEchoBody)
                return new Pulse(true, PulseSpec.Drowning, PulseSpec.ListenerSeconds(PulseSpec.DrowningSeconds, isSeeker, true, true, 1f));
            if (status != PlayerStatus.Alive || !heard || (occlusionHidesRing && blocked)) return None;
            return new Pulse(true, perceivedRadius, PulseSpec.ListenerSeconds(PulseSpec.DrowningSeconds, isSeeker, false, true, durationScale));
        }
    }
}
