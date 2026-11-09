using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Round.ResultPodium
{
    // 표시 전용. Result screen (기획서 12.5): the podium feed full screen, with the existing rematch vote
    // drawn over its bottom edge. Builds the stage when the round is decided and tears it down when the
    // screen closes (rematch or lobby return). Reads only synced values: result, role, status, nickname.
    // The vote itself stays in NetworkRound.Rematch; this only calls RequestRematch.
    [DisallowMultipleComponent]
    public sealed class ResultPodiumView : MonoBehaviour
    {
        private static readonly Color EscapedColor = new(.35f, .95f, 1f);
        private static readonly Color AliveColor = new(.8f, .9f, 1f);
        private static readonly Color EchoColor = new(.78f, .58f, 1f);
        private static readonly Color SeekerLabelColor = new(1f, .42f, .4f);

        private ResultPodiumStage stage;
        private ResultPodiumAssets assets;
        private bool shown;

        private void Update()
        {
            var round = NetworkRound.Instance;
            bool want = round != null && round.IsResultScreenShown;
            if (want && !shown) Open(round.Result);
            else if (!want && shown) Close();
            shown = want;
            if (stage == null) return;
            // GameEnd can land a tick after the result: add the cards when it does.
            if (!stage.HasAwards && round != null && round.RoundAwards != null)
                stage.SetAwards(BuildAwards(round.RoundAwards, NetworkPlayer.All));
            stage.Resize(FeedWidth, FeedHeight);
            stage.Tick(Time.unscaledDeltaTime);
            stage.Camera.enabled = true;
        }

        private static int FeedWidth => Mathf.Min(Screen.width, 1920);
        private static int FeedHeight => Mathf.RoundToInt(FeedWidth * Screen.height / (float)Mathf.Max(1, Screen.width));

        private void Open(RoundResult result)
        {
            Close();
            if (assets == null) assets = ResultPodiumAssets.Load();
            if (assets == null) { Debug.LogWarning("[MARCO][Result] Resources/" + ResultPodiumAssets.ResourcePath + " missing; plain result screen."); return; }
            var awards = NetworkRound.Instance != null && NetworkRound.Instance.RoundAwards != null
                ? BuildAwards(NetworkRound.Instance.RoundAwards, NetworkPlayer.All) : null;
            stage = new ResultPodiumStage(result, BuildEntries(result, NetworkPlayer.All, NetworkPlayer.Local), assets, FeedWidth, FeedHeight, awards);
            if (!stage.IsValid) Close();
        }

        private void Close()
        {
            stage?.Dispose();
            stage = null;
        }

        private void OnDisable() { Close(); shown = false; }

        // Who stands on the podium and how: Seeker red, escaped/alive Runners cyan side, Echoes (tagged
        // Runners) on the Runners' side as ghosts. Spectators joined mid-round and are left out.
        public static List<PodiumEntry> BuildEntries(RoundResult result, IEnumerable<NetworkPlayer> players, NetworkPlayer local)
        {
            var entries = new List<PodiumEntry>();
            foreach (var player in players)
            {
                if (player == null || player.Status == PlayerStatus.Spectating) continue;
                bool seeker = player.Role == PlayerRole.Seeker;
                bool echo = !seeker && (player.Role == PlayerRole.Echo || player.Status == PlayerStatus.Tagged);
                entries.Add(new PodiumEntry
                {
                    Name = string.IsNullOrEmpty(player.Nickname) ? $"P{player.PlayerId}" : player.Nickname,
                    Body = seeker ? PodiumBody.Seeker : echo ? PodiumBody.Echo : PodiumBody.Runner,
                    Won = seeker ? result == RoundResult.SeekersWin : result == RoundResult.RunnersWin,
                    Outcome = seeker ? "술래" : echo ? "메아리" : player.Status == PlayerStatus.Escaped ? "탈출" : "생존",
                    OutcomeColor = seeker ? SeekerLabelColor : echo ? EchoColor : player.Status == PlayerStatus.Escaped ? EscapedColor : AliveColor,
                    IsLocal = player == local,
                    PlayerId = player.PlayerId,
                });
            }
            return entries;
        }

        // GameEnd awards (14.3) → cards. Winner names come from the synced nicknames, like the podium labels.
        public static List<PodiumAward> BuildAwards(IReadOnlyList<AwardWire> awards, IEnumerable<NetworkPlayer> players)
        {
            var names = new Dictionary<int, string>();
            foreach (var player in players)
                if (player != null) names[player.PlayerId] = string.IsNullOrEmpty(player.Nickname) ? $"P{player.PlayerId}" : player.Nickname;
            var cards = new List<PodiumAward>();
            foreach (var award in awards)
            {
                var ids = award.Winners ?? new int[0];
                var winnerNames = new string[ids.Length];
                for (int i = 0; i < ids.Length; i++) winnerNames[i] = names.TryGetValue(ids[i], out var name) ? name : $"P{ids[i]}";
                cards.Add(new PodiumAward { Kind = award.Kind, WinnerIds = ids, WinnerNames = winnerNames, Value = award.Value });
            }
            return cards;
        }

        // From NetworkTestHud.OnGUI (TestHudSkin coordinates).
        public void Draw(NetworkRound round)
        {
            var full = new Rect(0, 0, TestHudSkin.Width, TestHudSkin.Height);
            float reveal = 1f;
            if (stage != null && stage.Feed != null)
            {
                GUI.DrawTexture(full, stage.Feed, ScaleMode.ScaleAndCrop, false);
                reveal = Mathf.Clamp01((stage.Time - stage.VoteRevealTime) / .35f);
            }
            else
            {
                // No podium (assets missing): dark screen with the banner text only.
                TestHudSkin.Fill(full, new Color(.012f, .014f, .03f, .96f));
                var banner = new GUIStyle(TestHudSkin.Text(96, FontStyle.Bold, TextAnchor.MiddleCenter));
                banner.normal.textColor = TeamColor(round.Result);
                GUI.Label(new Rect(0, full.height * .2f, full.width, 140), ResultPodiumStage.ResultTitle(round.Result), banner);
            }
            if (reveal > 0f) DrawVote(round, full, reveal);
        }

        // Rematch: "리매치? (yes/total)  N초  [찬성]" — same values and button as before (기획서 12.5).
        private static void DrawVote(NetworkRound round, Rect full, float reveal)
        {
            var previousColor = GUI.color;
            GUI.color = new Color(1, 1, 1, reveal);
            const float height = 118;
            var bar = new Rect(0, full.height - height + (1 - reveal) * 40, full.width, height);
            for (int i = 0; i < 12; i++)
                TestHudSkin.Fill(new Rect(bar.x, bar.y - 36 + i * 3, bar.width, 3), new Color(0, 0, 0, .05f * i));
            TestHudSkin.Fill(bar, new Color(0, 0, 0, .72f));

            float remaining = round.RematchRemainingSeconds;
            var accent = TeamColor(round.Result);
            TestHudSkin.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(remaining / RematchVote.DefaultDurationSeconds), 4), accent);

            float centerX = full.width / 2;
            float rowY = bar.y + 26;
            GUI.Label(new Rect(centerX - 560, rowY, 300, 64), "리매치?", TestHudSkin.Text(40, FontStyle.Bold, TextAnchor.MiddleRight));
            GUI.Label(new Rect(centerX - 240, rowY, 260, 64), $"찬성 {round.RematchYesCount} / {round.RematchVoterCount}",
                TestHudSkin.Text(36, FontStyle.Bold, TextAnchor.MiddleCenter));
            int seconds = Mathf.Max(0, Mathf.CeilToInt(remaining));
            GUI.Label(new Rect(centerX + 30, rowY, 110, 64), $"{seconds}초", TestHudSkin.Text(36, FontStyle.Bold, TextAnchor.MiddleCenter));

            bool previousEnabled = GUI.enabled;
            GUI.enabled = round.CanRequestRematch && reveal >= 1f;
            var button = new GUIStyle(TestHudSkin.Button(32)) { alignment = TextAnchor.MiddleCenter };
            if (GUI.Button(new Rect(centerX + 170, rowY, 220, 64), round.HasRequestedRematch ? "찬성함" : "찬성", button))
                round.RequestRematch();
            GUI.enabled = previousEnabled;
            GUI.color = previousColor;
        }

        private static Color TeamColor(RoundResult result) => result switch
        {
            RoundResult.RunnersWin => ResultPodiumStage.RunnersColor,
            RoundResult.SeekersWin => ResultPodiumStage.SeekersColor,
            _ => ResultPodiumStage.VoidColor,
        };
    }
}
