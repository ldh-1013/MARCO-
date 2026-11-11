using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEngine;

namespace Marco.Network
{
    // 기획서 8.3 · 연출.md 7.2: the knocking Echo's own "낚았다!" — HUD text for 1.5 s in the HUD's own white
    // (no new colour, no new sound) plus a 0.1 s screen nudge. It sits where the Echo-body banner goes;
    // an eliminated Echo never has that banner, so the two never overlap.
    public sealed partial class NetworkTestHud
    {
        public const float LureNoticeSeconds = 1.5f;
        private const float LureNoticeFadeSeconds = .3f;
        private float nudgedFor = float.NegativeInfinity;

        private void DrawLureNotice(NetworkRound round, float top)
        {
            var player = NetworkPlayer.Local;
            if (player == null || round.IsResultScreenShown) return;
            float age = Time.unscaledTime - round.LastLureNoticeTime;
            if (age < 0f || age >= LureNoticeSeconds) return;
            if (!Mathf.Approximately(nudgedFor, round.LastLureNoticeTime))
            {
                nudgedFor = round.LastLureNoticeTime;
                CameraNudge.Play(player);
            }
            float alpha = Mathf.Clamp01((LureNoticeSeconds - age) / LureNoticeFadeSeconds);
            DrawLureNoticePanel(EchoBodyBannerRect(TestHudSkin.Width, top), alpha);
        }

        // Also used by the render check's composite.
        public const string LureNoticeText = "낚았다!";

        private static void DrawLureNoticePanel(Rect rect, float alpha)
        {
            var saved = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            TestHudSkin.Panel(rect);
            GUI.Label(rect, LureNoticeText, TestHudSkin.Text(44, FontStyle.Bold, TextAnchor.MiddleCenter));
            GUI.color = saved;
        }
    }
}
