using System;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Vision
{
    // 3.1-1 · 3.2 · 연출.md 4.2 · 7.1: the eliminated Echo's own 3.0 s tag blackout. Local only, nothing is sent:
    // it starts when this client sees its own player become an eliminated Echo (EchoKnockLocal.TaggedAt, the same
    // moment the knock HUD's 20 s lock counts from). Escapees and mid-round joiners never get it (3.3-3).
    // The cover is drawn over the 3D view only (NetworkTestHud draws it first, the HUD text stays on top); the sonar
    // rings and the voice sight keep running underneath. Every failure clears it (fail-open: free input is the safe side).
    public static class TagBlackoutLocal
    {
        static float trackedTag = float.NaN;
        static float lockStartedReal = float.NegativeInfinity;

        // Seconds since this screen's tag; +inf when there is no blackout to show (not an eliminated Echo, no round
        // running, result screen, rematch). Never throws.
        public static float Elapsed(NetworkPlayer player)
        {
            try
            {
                var round = NetworkRound.Instance;
                if (player == null || round == null || round.Result != RoundResult.InProgress || round.IsResultScreenShown ||
                    !EchoBodyRules.HasTagBlackout(player.Status)) return float.PositiveInfinity;
                float at = EchoKnockLocal.TaggedAt;
                if (float.IsNaN(at) || float.IsInfinity(at)) return float.PositiveInfinity;
                if (at != trackedTag)
                {
                    trackedTag = at;
                    lockStartedReal = Time.realtimeSinceStartup;
                }
                return Time.time - at;
            }
            catch (Exception exception)
            {
                DirectionHints.ReportFault("TagBlackout", exception);
                return float.PositiveInfinity;
            }
        }

        // Look and flight input are ignored while this is true (never past 3.0 s + 0.1 s on the real clock).
        public static bool BlocksInput(NetworkPlayer player)
        {
            float elapsed = Elapsed(player);
            return !float.IsInfinity(elapsed) &&
                EchoBodyRules.BlocksTagInput(elapsed, Time.realtimeSinceStartup - lockStartedReal);
        }

        // The 8-way compass comes 0.5 s after the view returns (연출.md 7.1).
        public static bool HidesCompass(NetworkPlayer player) => !EchoBodyRules.TagCompassShown(Elapsed(player));

        // Full screen, white then black, in the HUD's coordinates. Call from an OnGUI before the HUD is drawn.
        public static void Draw(NetworkPlayer player, float width, float height)
        {
            try
            {
                if (Event.current == null || Event.current.type != EventType.Repaint) return;
                float alpha = EchoBodyRules.TagBlackoutCover(Elapsed(player), out float shade);
                if (alpha <= 0f) return;
                var saved = GUI.color;
                GUI.color = new Color(shade, shade, shade, alpha);
                GUI.DrawTexture(new Rect(0f, 0f, width, height), Texture2D.whiteTexture);
                GUI.color = saved;
            }
            catch (Exception exception)
            {
                DirectionHints.ReportFault("TagBlackoutDraw", exception);
            }
        }
    }
}
