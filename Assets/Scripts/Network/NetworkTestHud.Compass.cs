using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEngine;

namespace Marco.Network
{
    // §3.2-2 방향 감각: the 8-way compass of an Echo body (eliminated Echo, escapee, mid-round joiner) — very faint at
    // the screen's bottom edge, only the direction the view faces. No position, no terrain, no players. Local only.
    // HUD white at the direction hint's "은은한" alpha (DirectionHintRules.MaxAlpha 0.35, 연출 튜닝값 11-08), no new
    // value; world north = +Z, the same octants as the direction hint and the server's hidden-source snap.
    public sealed partial class NetworkTestHud
    {
        public static readonly string[] CompassText = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };
        private NetworkPlayer compassOwner;
        private Transform compassView;

        private void DrawEchoCompass(NetworkRound round)
        {
            var player = NetworkPlayer.Local;
            if (player == null || !player.HasEchoBody || round.Result != RoundResult.InProgress || round.IsResultScreenShown) return;
            if (TagBlackoutLocal.HidesCompass(player)) return;   // 연출.md 7.1: 0.5 s after the tag blackout
            if (compassOwner != player || compassView == null)
            {
                compassOwner = player;
                var view = player.GetComponentInChildren<Camera>(true);
                compassView = view != null ? view.transform : player.transform;
            }
            float yaw = compassView.eulerAngles.y;
            int octant = ((int)Mathf.Round(yaw / DirectionHintRules.ArcDegrees) % 8 + 8) % 8;
            var saved = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, DirectionHintRules.MaxAlpha);
            GUI.Label(new Rect(TestHudSkin.Width * .5f - 120f, TestHudSkin.Height - 64f, 240f, 48f), CompassText[octant],
                TestHudSkin.Text(30, FontStyle.Bold, TextAnchor.MiddleCenter));
            GUI.color = saved;
        }
    }
}
