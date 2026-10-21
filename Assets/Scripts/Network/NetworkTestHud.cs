using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network
{
    // Temporary IMGUI HUD for the LAN test scenes. Offline: Host / IP Join panel. Connected: round
    // timer and gate (top center), valves (left), players (right), result (center).
    // The host also gets round start/restart here; RoundCheats (Tab, dev builds only) repeats them with the cheats.
    [RequireComponent(typeof(LanConnector))]
    public sealed class NetworkTestHud : MonoBehaviour
    {
        private const float SideWidth = 420, RowHeight = 44;

        private static readonly Color SeekerColor = new(0.95f, 0.3f, 0.3f);
        private static readonly Color RunnerColor = new(0.35f, 0.6f, 1f);
        private static readonly Color MutedColor = new(0.6f, 0.6f, 0.6f);
        private static readonly Color OpenColor = new(0.3f, 0.85f, 0.4f);
        private static readonly Color RotatingColor = new(1f, 0.85f, 0.2f);
        private static readonly Color ReflowColor = new(1f, 0.55f, 0.1f);

        [SerializeField] private string joinAddress = "127.0.0.1";

        private LanConnector connector;

        private void Awake() => connector = GetComponent<LanConnector>();

        private void OnGUI()
        {
            TestHudSkin.Begin();
            if (connector.Status != LanConnectionStatus.Connected)
            {
                DrawConnectPanel();
                return;
            }

            DrawPlayers();
            var round = NetworkRound.Instance;
            if (round == null) return;
            DrawRoundStatus(round);
            if (round.Mode == RoundMode.NotStarted) return;
            DrawValves(round);
            DrawResult(round);
        }

        private void DrawConnectPanel()
        {
            var rect = new Rect(TestHudSkin.Margin, TestHudSkin.Margin, SideWidth, 330);
            TestHudSkin.Panel(rect);
            GUILayout.BeginArea(Inset(rect));
            if (connector.Status == LanConnectionStatus.Connecting)
            {
                GUILayout.Label("연결 중…", TestHudSkin.Text(32, FontStyle.Bold));
                if (GUILayout.Button("취소", TestHudSkin.Button(24), GUILayout.Height(52))) connector.Leave();
            }
            else
            {
                GUILayout.Label(connector.Status == LanConnectionStatus.Failed ? "연결 실패 — IP를 확인하세요" : "오프라인",
                    TestHudSkin.Text(28, FontStyle.Bold));
                if (GUILayout.Button("Host", TestHudSkin.Button(24), GUILayout.Height(52))) connector.Host();
                GUILayout.Space(12);
                GUILayout.Label("Host IP", TestHudSkin.Text(22));
                joinAddress = GUILayout.TextField(joinAddress, new GUIStyle(GUI.skin.textField) { fontSize = 24 }, GUILayout.Height(40));
                if (GUILayout.Button("Join", TestHudSkin.Button(24), GUILayout.Height(52))) connector.Join(joinAddress);
            }
            GUILayout.EndArea();
        }

        // Top center: time, gate, last-survivor phase, and the host's start/restart button.
        private void DrawRoundStatus(NetworkRound round)
        {
            const float width = 640, buttonHeight = 52;
            bool lastSurvivor = round.Mode != RoundMode.NotStarted && round.InLastSurvivorPhase;
            // ValvesOnly never decides a result, so the host can restart it at any time.
            bool hostButton = connector.IsHost && (round.Mode == RoundMode.NotStarted ||
                                                   round.Result != RoundResult.InProgress || round.Mode == RoundMode.ValvesOnly);
            float height = (lastSurvivor ? 220 : 130) + (hostButton ? buttonHeight + 12 : 0);
            var rect = new Rect((TestHudSkin.Width - width) / 2, TestHudSkin.Margin, width, height);
            TestHudSkin.Panel(rect);
            var inner = Inset(rect);
            var center = TestHudSkin.Text(40, FontStyle.Bold, TextAnchor.MiddleCenter);

            if (hostButton) DrawHostButton(round, new Rect(inner.x, inner.yMax - buttonHeight, inner.width, buttonHeight));

            if (round.Mode == RoundMode.NotStarted)
            {
                GUI.Label(new Rect(inner.x, inner.y, inner.width, 52), "라운드 대기 중", center);
                GUI.Label(new Rect(inner.x, inner.y + 56, inner.width, 36),
                    connector.IsHost ? "인원이 모이면 라운드를 시작하세요" : "Host가 라운드를 시작하길 기다리는 중",
                    TestHudSkin.Text(24, FontStyle.Normal, TextAnchor.MiddleCenter));
                return;
            }

            GUI.Label(new Rect(inner.x, inner.y, inner.width, 52), round.Mode == RoundMode.Full
                ? $"남은 시간  {FormatTime(round.RemainingSeconds)}"
                : "밸브 전용 모드", center);
            var gateStyle = new GUIStyle(TestHudSkin.Text(28, FontStyle.Bold, TextAnchor.MiddleCenter));
            gateStyle.normal.textColor = round.GateOpen ? OpenColor : SeekerColor;
            GUI.Label(new Rect(inner.x, inner.y + 56, inner.width, 36), round.GateOpen ? "게이트 열림" : "게이트 닫힘", gateStyle);

            if (!lastSurvivor) return;
            float y = inner.y + 100;
            GUI.Label(new Rect(inner.x, y, inner.width, 36), $"최후 생존자 페이즈 · {FormatTime(round.LastSurvivorRemainingSeconds)}",
                TestHudSkin.Text(26, FontStyle.Bold, TextAnchor.MiddleCenter));
            if (round.TryGetActiveDrainHatch(out var drain))
            {
                GUI.Label(new Rect(inner.x, y + 44, 160, 28), $"배수구 {drain}", TestHudSkin.Text(22));
                TestHudSkin.Bar(new Rect(inner.x + 170, y + 48, inner.width - 260, 20), round.DrainHatchProgress, RotatingColor);
                GUI.Label(new Rect(inner.xMax - 80, y + 44, 80, 28), $"{round.DrainHatchProgress:P0}",
                    TestHudSkin.Text(22, FontStyle.Normal, TextAnchor.MiddleRight));
            }
            else GUI.Label(new Rect(inner.x, y + 44, inner.width, 28), "배수구 없음 (게이트 열림)",
                TestHudSkin.Text(22, FontStyle.Normal, TextAnchor.MiddleCenter));
        }

        private static void DrawHostButton(NetworkRound round, Rect rect)
        {
            var style = new GUIStyle(TestHudSkin.Button(24)) { alignment = TextAnchor.MiddleCenter };
            if (round.Mode != RoundMode.NotStarted)
            {
                if (GUI.Button(rect, "↻  다시 시작", style)) round.RestartRound();
                return;
            }
            if (!GUI.Button(rect, "▶  라운드 시작", style)) return;
            // [Server] on StartRound silently skips the call unless the scene object is spawned on the server.
            Debug.Log($"[MARCO][HUD] 라운드 시작 클릭 (IsSpawned {round.IsSpawned}, IsServerInitialized {round.IsServerInitialized})");
            round.StartRound();
        }

        // Left: valve A~E progress bars, then the controls reminder.
        private static void DrawValves(NetworkRound round)
        {
            const int count = ValveId.E - ValveId.A + 1;
            var rect = new Rect(TestHudSkin.Margin, TestHudSkin.Margin, SideWidth, 60 + count * RowHeight + 140);
            TestHudSkin.Panel(rect);
            var inner = Inset(rect);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 40), "밸브", TestHudSkin.Text(30, FontStyle.Bold));

            float y = inner.y + 50;
            for (var id = ValveId.A; id <= ValveId.E; id++, y += RowHeight)
            {
                round.TryGetValveState(id, out var state);
                var (label, color) = ValveLook(state);
                GUI.Label(new Rect(inner.x, y, 40, 32), id.ToString(), TestHudSkin.Text(26, FontStyle.Bold));
                TestHudSkin.Bar(new Rect(inner.x + 44, y + 6, inner.width - 180, 22), state.Active ? state.Progress : 0f, color);
                var statusStyle = new GUIStyle(TestHudSkin.Text(22, FontStyle.Bold, TextAnchor.MiddleRight));
                statusStyle.normal.textColor = color;
                GUI.Label(new Rect(inner.xMax - 128, y, 128, 32), label, statusStyle);
            }

            var help = TestHudSkin.Text(18);
            GUI.Label(new Rect(inner.x, y + 4, inner.width, 26), "E 홀드: 밸브·배수구 작업 (도망자)", help);
            GUI.Label(new Rect(inner.x, y + 30, inner.width, 26), "E: 게이트 열린 뒤 탈출구에서 탈출", help);
            GUI.Label(new Rect(inner.x, y + 56, inner.width, 26), "좌클릭: 근처 도망자 태그 (술래)", help);
            GUI.Label(new Rect(inner.x, y + 82, inner.width, 26), "Esc: 커서 풀기/잠그기", help);
        }

        // Right: every player with role and status, then the disconnect button.
        private void DrawPlayers()
        {
            var players = NetworkPlayer.All;
            var rect = new Rect(TestHudSkin.Width - SideWidth - TestHudSkin.Margin, TestHudSkin.Margin, SideWidth,
                60 + players.Count * RowHeight + 76);
            TestHudSkin.Panel(rect);
            var inner = Inset(rect);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 40), $"플레이어 {connector.PlayerCount}명 · {(connector.IsHost ? "Host" : "Client")}",
                TestHudSkin.Text(28, FontStyle.Bold));

            float y = inner.y + 50;
            foreach (var player in players)
            {
                var roleColor = player.Role switch
                {
                    PlayerRole.Seeker => SeekerColor,
                    PlayerRole.Runner => RunnerColor,
                    _ => MutedColor,
                };
                TestHudSkin.Fill(new Rect(inner.x, y + 8, 18, 18), roleColor);
                var style = new GUIStyle(TestHudSkin.Text(24, player.IsOwner ? FontStyle.Bold : FontStyle.Normal));
                style.normal.textColor = player.Status switch
                {
                    PlayerStatus.Alive => Color.white,
                    PlayerStatus.Escaped => OpenColor,
                    _ => MutedColor,
                };
                GUI.Label(new Rect(inner.x + 30, y, inner.width - 30, 34),
                    $"P{player.PlayerId}  {RoleLabel(player.Role)} · {StatusLabel(player.Status)}{(player.IsOwner ? "  (나)" : "")}", style);
                y += RowHeight;
            }

            if (GUI.Button(new Rect(inner.x, y + 6, inner.width, 48), "연결 끊기", TestHudSkin.Button(22))) connector.Leave();
        }

        // Center (slightly high, clear of the cheat panel at the bottom): the decided result, large.
        private static void DrawResult(NetworkRound round)
        {
            if (round.Result == RoundResult.InProgress) return;

            const float width = 900, height = 220;
            var rect = new Rect((TestHudSkin.Width - width) / 2, TestHudSkin.Height * 0.4f - height / 2, width, height);
            TestHudSkin.Panel(rect);
            var style = new GUIStyle(TestHudSkin.Text(110, FontStyle.Bold, TextAnchor.MiddleCenter));
            style.normal.textColor = round.Result switch
            {
                RoundResult.RunnersWin => RunnerColor,
                RoundResult.SeekersWin => SeekerColor,
                _ => MutedColor,
            };
            GUI.Label(rect, ResultLabel(round.Result), style);
        }

        private static Rect Inset(Rect rect) => new(rect.x + TestHudSkin.Padding, rect.y + TestHudSkin.Padding,
            rect.width - TestHudSkin.Padding * 2, rect.height - TestHudSkin.Padding * 2);

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
        }

        private static (string, Color) ValveLook(ValveSnapshot state)
        {
            if (!state.Active) return ("비활성", MutedColor);
            return state.Status switch
            {
                ValveStatus.Rotating => ($"회전 {state.Progress:P0}", RotatingColor),
                ValveStatus.Open => ("열림", OpenColor),
                ValveStatus.Reflowing => ($"역류 {state.Progress:P0}", ReflowColor),
                _ => ("닫힘", Color.white),
            };
        }

        private static string RoleLabel(PlayerRole role) => role switch
        {
            PlayerRole.Seeker => "술래",
            PlayerRole.Echo => "에코",
            _ => "도망자",
        };

        private static string StatusLabel(PlayerStatus status) => status switch
        {
            PlayerStatus.Escaped => "탈출",
            PlayerStatus.Tagged => "태그됨",
            PlayerStatus.Spectating => "관전 (다음 판부터)",
            _ => "생존",
        };

        private static string ResultLabel(RoundResult result) => result switch
        {
            RoundResult.RunnersWin => "도망자 승",
            RoundResult.SeekersWin => "술래 승",
            RoundResult.Void => "무효",
            _ => "진행 중",
        };
    }
}
