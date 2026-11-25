using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEngine;

namespace Marco.Network
{
    // §3.2 · 12.4 · 연출.md 7.2: the eliminated Echo's own knock HUD — remaining knocks (5 per round), the cooldown
    // ring and, for 20 s after the tag, the lock countdown ring. In the Echo-body banner's place (an eliminated Echo
    // has no banner) and in the HUD's own white, no new colour. Not on an escapee's or a joiner's screen.
    // The knock makes no ring on the knocker's own screen; a 0.1 s nudge marks the moment it sounds (1.5 s after
    // the request, local). The values are display only (EchoKnockLocal); the server decides.
    public sealed partial class NetworkTestHud
    {
        private float knockNudgedFor = float.NegativeInfinity;
        private static readonly string[] KnockCountText = { "노크 0/5", "노크 1/5", "노크 2/5", "노크 3/5", "노크 4/5", "노크 5/5" };
        private static readonly string[] SecondsText = MakeSeconds(26);
        public const string KnockReadyText = "G 노크";
        public const string KnockLockedText = "잠금";
        public const string KnockCooldownText = "쿨다운";
        public const string KnockSpentText = "노크 없음";

        private static string[] MakeSeconds(int count)
        {
            var array = new string[count];
            for (int i = 0; i < count; i++) array[i] = i + "초";
            return array;
        }

        private void DrawKnockHud(NetworkRound round, float top)
        {
            var player = NetworkPlayer.Local;
            if (player == null || !player.IsEcho || round.Result != RoundResult.InProgress || round.IsResultScreenShown) return;
            float now = Time.time;
            if (now >= EchoKnockLocal.NudgeAt && !Mathf.Approximately(knockNudgedFor, EchoKnockLocal.NudgeAt))
            {
                knockNudgedFor = EchoKnockLocal.NudgeAt;
                CameraNudge.Play(player);
            }
            // The lure notice (1.5 s) uses the same place.
            if (Time.unscaledTime - round.LastLureNoticeTime < LureNoticeSeconds) return;

            var rect = EchoBodyBannerRect(TestHudSkin.Width, top);
            TestHudSkin.Panel(rect);
            int remaining = Mathf.Clamp(EchoKnockLocal.Remaining, 0, EchoKnockRules.Limit);
            float locked = EchoKnockLocal.LockRemaining(now), cooling = EchoKnockLocal.CooldownRemaining(now);
            var ring = new Rect(rect.x + 24, rect.y + 14, rect.height - 28, rect.height - 28);
            var text = new Rect(ring.xMax + 20, rect.y + 8, rect.width - ring.width - 64, 50);
            var detail = new Rect(text.x, text.yMax, text.width, rect.height - 66);
            GUI.Label(text, KnockCountText[remaining], TestHudSkin.Text(36, FontStyle.Bold));
            string state;
            float fraction;
            if (locked > 0f) { state = KnockLockedText; fraction = 1f - locked / EchoKnockRules.LockSeconds; }
            else if (remaining == 0) { state = KnockSpentText; fraction = 0f; }
            else if (cooling > 0f) { state = KnockCooldownText; fraction = 1f - cooling / EchoKnockRules.CooldownSeconds; }
            else { state = KnockReadyText; fraction = 1f; }
            RuinedUi.HealingRing(ring, Mathf.Clamp01(fraction));
            float left = locked > 0f ? locked : cooling;
            GUI.Label(detail, state, TestHudSkin.Text(22));
            if (left > 0f && remaining > 0)
                GUI.Label(detail, SecondsText[Mathf.Clamp(Mathf.CeilToInt(left), 0, SecondsText.Length - 1)],
                    TestHudSkin.Text(22, FontStyle.Normal, TextAnchor.MiddleRight));
        }
    }
}
