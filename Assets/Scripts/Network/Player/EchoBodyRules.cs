using Marco.Network.Character;
using UnityEngine;

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

        // The blackout's own timeline (3.1-1 · 연출.md 4.2 태그된 도망자 화면 · 7.1), seconds since the tag on this screen:
        //   0.00 white flash · 0.15 blackout starts · 0.50 fully black · 3.00 sonar view and controls back
        //   3.00 – 3.50 the black clears (7.1 "소나 시야 페이드 인"; 0.5 s is 7.1's step to the compass and self glow)
        // White and black only, no new colour.
        public const float TagFlashSeconds = .15f;
        public const float TagFullBlackSeconds = .5f;
        public const float TagBlackoutSeconds = 3f;
        public const float TagRevealSeconds = .5f;
        // The input lock never outlives 3.0 s + this on the real clock, whatever happens to the tag time (fail-open).
        public const float TagInputGraceSeconds = .1f;

        // Look and flight input are ignored inside the 3.0 s (the underwater rise keeps running). NaN refuses the lock.
        public static bool BlocksTagInput(float elapsed, float realElapsed) =>
            elapsed >= 0f && elapsed < TagBlackoutSeconds &&
            realElapsed >= 0f && realElapsed < TagBlackoutSeconds + TagInputGraceSeconds;

        // Cover opacity at `elapsed`; shade 1 = white, 0 = black. Outside the timeline (or NaN) nothing is drawn.
        public static float TagBlackoutCover(float elapsed, out float shade)
        {
            shade = 0f;
            if (!(elapsed >= 0f) || elapsed >= TagBlackoutSeconds + TagRevealSeconds) return 0f;
            if (elapsed < TagFlashSeconds) { shade = 1f; return 1f; }
            if (elapsed < TagFullBlackSeconds)
            {
                shade = 1f - (elapsed - TagFlashSeconds) / (TagFullBlackSeconds - TagFlashSeconds);
                return 1f;
            }
            if (elapsed < TagBlackoutSeconds) return 1f;
            return 1f - (elapsed - TagBlackoutSeconds) / TagRevealSeconds;
        }

        // The 8-way compass shows from 0.5 s after the view returns (연출.md 7.1); no blackout = always.
        public static bool TagCompassShown(float elapsed) =>
            !(elapsed >= 0f && elapsed < TagBlackoutSeconds + TagRevealSeconds);
        public static bool HasEscapeFade(PlayerStatus from, PlayerStatus to) =>
            from == PlayerStatus.Alive && to == PlayerStatus.Escaped;

        // Where an Echo body may fly (3.2 · 맵설계.md 2.3 ⑫ · 부록 A [메아리]): inside the building, under the ceiling.
        // World = map coordinates (map x → x, map y → z, no root offset in IndoorPool_v1_2; 1층 바닥 Y 0, 수면 Y 0).
        // The outer wall's 0.3 stands outside the (0, 0)–(64, 62) outline (맵설계.md 0 · 1), so the outline is the
        // building and a box clamp is exact. Up: the hall ceiling 9.0 everywhere (no per-zone ceiling, ⑫ "홀 9.0 포함").
        // Down: the deepest pool floor, 수면 0 − 수심 3.5 (해석 — the spec gives no lower number).
        public const float FlightMinX = 0f, FlightMaxX = 64f, FlightMinZ = 0f, FlightMaxZ = 62f;
        public const float FlightMinY = -3.5f, FlightMaxY = 9f;

        // Inside points stay, outside points stop at the edge; a NaN position (or any failure) is returned as it was.
        public static Vector3 ClampFlight(Vector3 position)
        {
            try
            {
                if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)) return position;
                return new Vector3(Mathf.Clamp(position.x, FlightMinX, FlightMaxX),
                    Mathf.Clamp(position.y, FlightMinY, FlightMaxY), Mathf.Clamp(position.z, FlightMinZ, FlightMaxZ));
            }
            catch (System.Exception) { return position; }
        }

        // Valves, drain hatch, items, tag, treatment: alive players only (3.3-3 상호작용 — none for any Echo body).
        public static bool CanInteract(PlayerStatus status) => status == PlayerStatus.Alive;
    }
}
