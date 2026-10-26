using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network
{
    // Temporary IMGUI HUD for the test scenes once the round has started: round timer and gate
    // (top center), valves (left), players (right), result (center). Before that, LobbyMenu covers
    // the screen. The host also gets restart here; RoundCheats (Tab, dev builds only) repeats it with the cheats.
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

        private LanConnector connector;

        private void Awake() => connector = GetComponent<LanConnector>();

        private void OnGUI()
        {
            if (connector.Status != LanConnectionStatus.Connected) return;
            var round = NetworkRound.Instance;
            if (round == null || round.Mode == RoundMode.NotStarted) return;

            TestHudSkin.Begin();
            DrawPlayers();
            DrawRoundStatus(round);
            DrawValves(round);
            DrawRunnerHealth(round);
            DrawRunnerInventory();
            DrawHealingProgress();
            DrawResult(round);
        }

        // Top center: time, gate, last-survivor phase, and the host's start/restart button.
        private void DrawRoundStatus(NetworkRound round)
        {
            const float width = 640, buttonHeight = 52;
            bool lastSurvivor = round.InLastSurvivorPhase;
            // ValvesOnly never decides a result, so the host can restart it at any time.
            bool hostButton = connector.IsHost && (round.Result != RoundResult.InProgress || round.Mode == RoundMode.ValvesOnly);
            float height = (lastSurvivor ? 220 : 130) + (hostButton ? buttonHeight + 12 : 0);
            var rect = new Rect((TestHudSkin.Width - width) / 2, TestHudSkin.Margin, width, height);
            TestHudSkin.Panel(rect);
            var inner = Inset(rect);
            var center = TestHudSkin.Text(40, FontStyle.Bold, TextAnchor.MiddleCenter);

            if (hostButton) DrawHostButton(round, new Rect(inner.x, inner.yMax - buttonHeight, inner.width, buttonHeight));

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
            if (GUI.Button(rect, "↻  다시 시작", style)) round.RestartRound();
        }

        // Left: valve A~E progress bars, then the controls reminder.
        private static void DrawValves(NetworkRound round)
        {
            const int count = ValveId.E - ValveId.A + 1;
            var rect = new Rect(TestHudSkin.Margin, TestHudSkin.Margin, SideWidth, 60 + count * RowHeight + 220);
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
            GUI.Label(new Rect(inner.x, y + 56, inner.width, 26), "좌클릭: 앞쪽 1.2m 도망자 태그 (술래)", help);
            GUI.Label(new Rect(inner.x, y + 82, inner.width, 26), "Esc: 커서 풀기/잠그기", help);
            GUI.Label(new Rect(inner.x, y + 108, inner.width, 26), "Space: 점프 · Left Shift: 질주 (도망자)", help);
            GUI.Label(new Rect(inner.x, y + 134, inner.width, 26), "C: 웅크리기 · 부상자 근처 E 홀드: 치료 (9초)", help);
            GUI.Label(new Rect(inner.x, y + 160, inner.width, 26), "E: 붕대·찰칵이 줍기", help);
            GUI.Label(new Rect(inner.x, y + 186, inner.width, 26), "1/2: 아이템 선택 · G: 사용 (붕대는 홀드)", help);
        }

        private static void DrawHealingProgress()
        {
            var player = NetworkPlayer.Local;
            if (player == null) return;

            if (!player.HasHealingAction)
            {
                if (player.TryGetComponent(out PlayerInteractor interactor) && interactor.CanTreatNearbyRunner)
                {
                    var prompt = new Rect(TestHudSkin.Width * 0.5f - 190, TestHudSkin.Height * 0.5f + 68, 380, 34);
                    GUI.Label(prompt, "E 홀드로 치료 시작 · 9초", TestHudSkin.Text(22, FontStyle.Bold, TextAnchor.MiddleCenter));
                }
                return;
            }

            const int segments = 80;
            const float radius = 48f, dotSize = 7f;
            var center = new Vector2(TestHudSkin.Width * 0.5f, TestHudSkin.Height * 0.5f);
            int litSegments = Mathf.RoundToInt(player.HealingActionProgress * segments);
            Color active = player.HealingActionIsBandage
                ? new Color(1f, 0.75f, 0.2f, 0.96f)
                : new Color(0.35f, 0.8f, 1f, 0.96f);
            Color inactive = new Color(0f, 0f, 0f, 0.58f);

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments * Mathf.PI * 2f) - Mathf.PI * 0.5f;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                TestHudSkin.Fill(new Rect(point.x - dotSize * 0.5f, point.y - dotSize * 0.5f, dotSize, dotSize),
                    i < litSegments ? active : inactive);
            }

            string label = player.HealingActionIsBandage ? "붕대 감는 중" : "동료 치료 중";
            GUI.Label(new Rect(center.x - 78, center.y - 22, 156, 24), label,
                TestHudSkin.Text(17, FontStyle.Bold, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(center.x - 50, center.y + 1, 100, 28),
                $"{player.HealingActionProgress:P0}",
                TestHudSkin.Text(22, FontStyle.Bold, TextAnchor.MiddleCenter));
        }

        private static void DrawRunnerHealth(NetworkRound round)
        {
            var player = NetworkPlayer.Local;
            if (player == null || player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive) return;

            int totalHalves = Mathf.Max(1, round.FullHalves);
            int currentHalves = player.HealthHalves < 0 ? totalHalves : Mathf.Clamp(player.HealthHalves, 0, totalHalves);
            const float width = 330, height = 78;
            var panel = new Rect(TestHudSkin.Margin, TestHudSkin.Height - TestHudSkin.Margin - height, width, height);
            TestHudSkin.Panel(panel);

            GUI.Label(new Rect(panel.x + 16, panel.y + 8, 140, 28), "생존자 체력",
                TestHudSkin.Text(21, FontStyle.Bold));
            GUI.Label(new Rect(panel.xMax - 150, panel.y + 8, 134, 28),
                $"{currentHalves / 2f:F1} / {totalHalves / 2f:F1}",
                TestHudSkin.Text(20, FontStyle.Bold, TextAnchor.MiddleRight));

            float barX = panel.x + 16, barY = panel.y + 44, barWidth = panel.width - 32;
            const float barHeight = 18, gap = 5;
            float segmentWidth = (barWidth - gap * (totalHalves - 1)) / totalHalves;
            float healthRatio = currentHalves / (float)totalHalves;
            Color fill = healthRatio > 0.5f
                ? new Color(0.35f, 0.82f, 0.56f, 0.94f)
                : healthRatio > 0.25f
                    ? new Color(1f, 0.67f, 0.22f, 0.94f)
                    : new Color(0.96f, 0.29f, 0.27f, 0.96f);

            for (int i = 0; i < totalHalves; i++)
            {
                var segment = new Rect(barX + i * (segmentWidth + gap), barY, segmentWidth, barHeight);
                TestHudSkin.Fill(segment, new Color(1f, 1f, 1f, 0.18f));
                float amount = Mathf.Clamp01(currentHalves - i);
                if (amount > 0f)
                    TestHudSkin.Fill(new Rect(segment.x, segment.y, segment.width * amount, segment.height), fill);
            }

        }

        private static void DrawRunnerInventory()
        {
            var player = NetworkPlayer.Local;
            if (player == null || player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive) return;

            const float width = 204, height = 144;
            var panel = new Rect(TestHudSkin.Width - TestHudSkin.Margin - width,
                TestHudSkin.Height - TestHudSkin.Margin - height, width, height);
            TestHudSkin.Panel(panel);
            GUI.Label(new Rect(panel.x + 8, panel.y + 3, panel.width - 16, 22),
                "아이템 · 1/2 선택 · G 사용", TestHudSkin.Text(18, FontStyle.Bold));

            int selectedSlot = player.TryGetComponent(out PlayerInteractor interactor)
                ? interactor.SelectedItemSlot
                : 1;
            var bandageLabel = new GUIStyle(TestHudSkin.Text(17, FontStyle.Bold, TextAnchor.MiddleCenter));
            bandageLabel.normal.textColor = selectedSlot == 1 ? new Color(1f, 0.83f, 0.35f) : Color.white;
            var clickerLabel = new GUIStyle(TestHudSkin.Text(17, FontStyle.Bold, TextAnchor.MiddleCenter));
            clickerLabel.normal.textColor = selectedSlot == 2 ? new Color(0.45f, 0.85f, 1f) : Color.white;

            GUI.Label(new Rect(panel.x + 8, panel.y + 26, 88, 20), "1 · 붕대", bandageLabel);
            GUI.Label(new Rect(panel.x + 108, panel.y + 26, 88, 20), "2 · 찰칵", clickerLabel);
            DrawBandageSlot(new Rect(panel.x + 20, panel.y + 47, 64, 64), player.BandageCount, selectedSlot == 1);
            DrawClickerSlot(new Rect(panel.x + 120, panel.y + 47, 64, 64), player.ClickerCount, selectedSlot == 2);
            GUI.Label(new Rect(panel.x + 8, panel.y + 115, panel.width - 16, 20),
                "찰칵은 즉시 · 붕대는 G 홀드", TestHudSkin.Text(15, FontStyle.Normal, TextAnchor.MiddleCenter));
        }

        private static void DrawBandageSlot(Rect rect, int count, bool selected)
        {
            TestHudSkin.Fill(rect, new Color(0f, 0f, 0f, 0.42f));
            Color border = selected ? new Color(1f, 0.78f, 0.2f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
            TestHudSkin.Fill(new Rect(rect.x, rect.y, rect.width, 2), border);
            TestHudSkin.Fill(new Rect(rect.x, rect.yMax - 2, rect.width, 2), border);
            TestHudSkin.Fill(new Rect(rect.x, rect.y, 2, rect.height), border);
            TestHudSkin.Fill(new Rect(rect.xMax - 2, rect.y, 2, rect.height), border);

            Color bandage = count > 0 ? new Color(0.91f, 0.84f, 0.67f, 1f) : new Color(0.62f, 0.59f, 0.52f, 0.55f);
            var icon = new Rect(rect.x + 16, rect.y + 11, 32, 17);
            TestHudSkin.Fill(new Rect(icon.x - 2, icon.y - 2, icon.width + 4, icon.height + 4), new Color(0f, 0f, 0f, 0.45f));
            TestHudSkin.Fill(icon, bandage);
            Color hole = new(0.48f, 0.43f, 0.34f, count > 0 ? 0.8f : 0.4f);
            for (int i = 0; i < 3; i++)
                TestHudSkin.Fill(new Rect(icon.x + 7 + i * 9, icon.y + 6, 3, 3), hole);

            GUI.Label(new Rect(rect.x, rect.y + 31, rect.width, 27), count.ToString(),
                TestHudSkin.Text(20, FontStyle.Bold, TextAnchor.MiddleCenter));
        }

        private static void DrawClickerSlot(Rect rect, int count, bool selected)
        {
            TestHudSkin.Fill(rect, new Color(0f, 0f, 0f, 0.42f));
            Color border = selected ? new Color(0.35f, 0.82f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.35f);
            TestHudSkin.Fill(new Rect(rect.x, rect.y, rect.width, 2), border);
            TestHudSkin.Fill(new Rect(rect.x, rect.yMax - 2, rect.width, 2), border);
            TestHudSkin.Fill(new Rect(rect.x, rect.y, 2, rect.height), border);
            TestHudSkin.Fill(new Rect(rect.xMax - 2, rect.y, 2, rect.height), border);

            Color body = count > 0 ? new Color(0.78f, 0.9f, 1f, 1f) : new Color(0.62f, 0.66f, 0.7f, 0.5f);
            TestHudSkin.Fill(new Rect(rect.x + 18, rect.y + 14, 28, 25), body);
            TestHudSkin.Fill(new Rect(rect.x + 22, rect.y + 9, 20, 6), body);
            TestHudSkin.Fill(new Rect(rect.x + 26, rect.y + 20, 12, 12), new Color(0.18f, 0.3f, 0.42f, count > 0 ? 1f : 0.45f));
            GUI.Label(new Rect(rect.x, rect.y + 38, rect.width, 20), count.ToString(),
                TestHudSkin.Text(18, FontStyle.Bold, TextAnchor.MiddleCenter));
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
                var style = new GUIStyle(TestHudSkin.Text(18, player.IsOwner ? FontStyle.Bold : FontStyle.Normal));
                style.normal.textColor = player.Status switch
                {
                    PlayerStatus.Alive => Color.white,
                    PlayerStatus.Escaped => OpenColor,
                    _ => MutedColor,
                };
                GUI.Label(new Rect(inner.x + 30, y, inner.width - 30, 34),
                    $"P{player.PlayerId}  {RoleLabel(player.Role)} · {StatusLabel(player.Status)}{(player.IsCrouching ? " · 웅크림" : "")}{(player.IsOwner ? "  (나)" + OwnerDetail(player) : "")}", style);
                y += RowHeight;
            }

            if (GUI.Button(new Rect(inner.x, y + 6, inner.width, 48), "연결 끊기", TestHudSkin.Button(22))) connector.Leave();
        }

        // Debug text only the owner sees: the Seeker's swing cooldown.
        private static string OwnerDetail(NetworkPlayer player)
        {
            if (player.Role == PlayerRole.Seeker && player.TryGetComponent(out PlayerInteractor interactor) &&
                interactor.SwingCooldownRemaining > 0)
                return $" · 쿨다운 {interactor.SwingCooldownRemaining:F1}s";
            return "";
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
            PlayerStatus.Tagged => "메아리",
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
