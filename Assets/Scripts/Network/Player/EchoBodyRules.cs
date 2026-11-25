using Marco.Network.Character;

namespace Marco.Network.Player
{
    // Who wears the Echo body and what it changes (기획서 3.2 · 3.3-3 · 13.3, 11-10). Pure rules so EditMode
    // tests can pin them; NetworkPlayer applies them to the synced status.
    //
    // "Echo body" is only the body: ghost flight, no collision, hidden from survivors, seen by other
    // Echo bodies. Rules about who an Echo *is* (win/loss, counting, tag target, knock, awards) keep
    // using NetworkPlayer.IsEcho (= Tagged) or the status itself.
    public static class EchoBodyRules
    {
        // Tagged = eliminated Echo. Escaped = escapee. Spectating = joined mid-round (13.3).
        public static bool HasEchoBody(PlayerStatus status) =>
            status == PlayerStatus.Tagged || status == PlayerStatus.Escaped || status == PlayerStatus.Spectating;

        // Survivors (alive Runners and the Seeker) never render an Echo body; Echo bodies see each other.
        // viewer = null: no local player (dedicated server, or before spawn) — Echo bodies stay hidden.
        public static bool IsBodyVisible(PlayerStatus subject, PlayerStatus? viewer) =>
            !HasEchoBody(subject) || (viewer is PlayerStatus v && HasEchoBody(v));

        // An Echo body passes through walls and players (the CharacterController is off).
        public static bool HasCollision(PlayerStatus status) => !HasEchoBody(status);

        // Rim colour: eliminated = pale purple, escapee and mid-round joiner = cyan (16.1 · 3.3-3).
        public static GhostLook LookOf(PlayerStatus status) => status switch
        {
            PlayerStatus.Tagged => GhostLook.Echo,
            PlayerStatus.Escaped or PlayerStatus.Spectating => GhostLook.Escapee,
            _ => GhostLook.None,
        };

        // Knock (3.2, G) belongs to the eliminated Echo only. Escapees and mid-round joiners never knock.
        public static bool CanKnock(PlayerStatus status) => status == PlayerStatus.Tagged;

        // The 3.0 s blackout and 20 s knock lock follow a tag only (3.2 · 연출.md 7.1). An escapee gets the
        // cyan fade instead (연출.md 5); a mid-round joiner gets neither.
        public static bool HasTagBlackout(PlayerStatus status) => status == PlayerStatus.Tagged;
        public static bool HasEscapeFade(PlayerStatus from, PlayerStatus to) =>
            from == PlayerStatus.Alive && to == PlayerStatus.Escaped;

        // Valves, drain hatch, items, tag, treatment: alive players only (3.3-3 상호작용 — none for any Echo body).
        public static bool CanInteract(PlayerStatus status) => status == PlayerStatus.Alive;
    }
}
