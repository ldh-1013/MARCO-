namespace Marco.Network.Round
{
    // §3.2 노크 · 부록 A [메아리] — pure rules of the eliminated Echo's knock (no scene, no network). The server
    // (NetworkRound.Knock) decides with these; the knocking Echo's own HUD (EchoKnockLocal) only displays them.
    //   9 m · 1.2 s, the Talk grade's pulse (§5.1 same-looking group: knock · Talk · Moan) — every per-listener rule
    //   (Seeker ×1.2 / ×1.5, walls, floors, water) is EmitSound's, unchanged
    //   cooldown 25 s · 5 per round (no recharge) · locked for 20 s after the tag · sounds 1.5 s after the request,
    //   at the Echo's position at the request (it does not follow the Echo, 10-05)
    public static class EchoKnockRules
    {
        public const float LockSeconds = 20f;
        public const float CooldownSeconds = 25f;
        public const float DelaySeconds = 1.5f;
        public const int Limit = 5;
        public const float Radius = 9f;
        public const float Seconds = 1.2f;
        // No knock clip exists (Resources/MarcoSound): the knock goes out as a ring-only pulse (PulseSpec.NoAudio on
        // the wire), like the other forced pulses. This name never reaches a client.
        public const string Clip = "Knock";

        // Server checks 3–6 for one request at `now`; checks 1–2 (round running, requester an eliminated Echo) are
        // the caller's. Any non-finite input refuses (fail-closed).
        public static bool Accept(float now, float taggedAt, float lastAccepted, int used, bool pending)
        {
            if (float.IsNaN(now) || float.IsInfinity(now) || float.IsNaN(taggedAt) || float.IsInfinity(taggedAt)) return false;
            if (pending || used < 0 || used >= Limit) return false;
            if (now < taggedAt + LockSeconds) return false;
            return float.IsNegativeInfinity(lastAccepted) || now >= lastAccepted + CooldownSeconds;
        }
    }

    // The knocking Echo's own HUD state (client, display only). The server decides; nothing here is sent. The lock
    // starts when this client sees its player become an eliminated Echo (at most one round trip after the server);
    // a request is counted when it is sent, so a request the server drops still shows as used (no reply exists, 14.2 P2).
    public static class EchoKnockLocal
    {
        static bool wasEcho;
        public static float TaggedAt { get; private set; } = float.NegativeInfinity;
        public static float LastRequest { get; private set; } = float.NegativeInfinity;
        public static int Used { get; private set; }
        // When the own 0.1 s nudge plays (the knock's moment, 1.5 s after the request; §3.2 own screen shows no ring).
        public static float NudgeAt { get; private set; } = float.NegativeInfinity;

        public static int Remaining => Used >= EchoKnockRules.Limit ? 0 : EchoKnockRules.Limit - Used;

        // Every frame on the owner: a new eliminated-Echo spell starts the lock and a fresh count; leaving it clears.
        public static void Observe(bool isEcho, float now)
        {
            if (isEcho && !wasEcho)
            {
                TaggedAt = now;
                LastRequest = float.NegativeInfinity;
                NudgeAt = float.NegativeInfinity;
                Used = 0;
            }
            else if (!isEcho && wasEcho)
            {
                TaggedAt = LastRequest = NudgeAt = float.NegativeInfinity;
                Used = 0;
            }
            wasEcho = isEcho;
        }

        public static float LockRemaining(float now) => wasEcho ? System.Math.Max(0f, TaggedAt + EchoKnockRules.LockSeconds - now) : 0f;
        public static float CooldownRemaining(float now) =>
            float.IsNegativeInfinity(LastRequest) ? 0f : System.Math.Max(0f, LastRequest + EchoKnockRules.CooldownSeconds - now);

        // True when a request may be sent now; then it is counted (the server re-checks everything).
        public static bool TryRequest(float now)
        {
            if (!wasEcho || !EchoKnockRules.Accept(now, TaggedAt, LastRequest, Used, false)) return false;
            Used++;
            LastRequest = now;
            NudgeAt = now + EchoKnockRules.DelaySeconds;
            return true;
        }
    }
}
