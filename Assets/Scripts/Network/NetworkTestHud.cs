using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Round.ResultPodium;
using UnityEngine;

namespace Marco.Network
{
    // Temporary IMGUI HUD for the test scenes once the round has started: round timer and gate
    // (top center), valves (left), players (right), result (center). Before that, LobbyMenu covers
    // the screen. The host gets restart here in ValvesOnly mode; a decided round goes to the result
    // screen's rematch vote. RoundCheats (Tab, dev builds only) keeps its own restart with the cheats.
    [RequireComponent(typeof(LanConnector))]
    public sealed partial class NetworkTestHud : MonoBehaviour
    {
        private const float SideWidth = 420, RowHeight = 44;

        private static readonly Color SeekerColor = new(0.95f, 0.3f, 0.3f);
        private static readonly Color RunnerColor = new(0.35f, 0.6f, 1f);
        private static readonly Color MutedColor = new(0.6f, 0.6f, 0.6f);
        private static readonly Color OpenColor = new(0.3f, 0.85f, 0.4f);
        private static readonly Color RotatingColor = new(1f, 0.85f, 0.2f);

        private LanConnector connector;
        private ResultPodiumView podium;

        private void Awake()
        {
            connector = GetComponent<LanConnector>();
            podium = GetComponent<ResultPodiumView>();
            if (podium == null) podium = gameObject.AddComponent<ResultPodiumView>();
        }

        private void OnGUI()
        {
            if (connector.Status != LanConnectionStatus.Connected) return;
            var round = NetworkRound.Instance;
            if (round == null || round.Mode == RoundMode.NotStarted) return;

            TestHudSkin.Begin();
            DrawRunnerInjury(round);
            DrawSeekerReticle(round);
            var local = NetworkPlayer.Local;
            if (local != null && local.Role == PlayerRole.Runner && local.Status == PlayerStatus.Alive &&
                !local.HasHealingAction && round.Result == RoundResult.InProgress)
                RuinedUi.Reticle(new Vector2(TestHudSkin.Width * .5f, TestHudSkin.Height * .5f), Color.white, 24);
            DrawPlayers();
            DrawRoundStatus(round);
            DrawValves(round);
            DrawRunnerHealth(round);
            DrawRunnerInventory();
            DrawShoutGauge(round);
            DrawHealingProgress();
            DrawBreath();
            DrawValveDiveHint();
            DrawRoundStart(round);
            DrawResult(round);
        }

        // Center: own role only for the 5 s role reveal, then the Seeker's hold countdown (기획서 부록 A [라운드 시작]).
        private static void DrawRoundStart(NetworkRound round)
        {
            var player = NetworkPlayer.Local;
            if (player == null || round.Result != RoundResult.InProgress) return;
            int seconds = Mathf.Max(1, Mathf.CeilToInt(round.StartPhaseRemainingSeconds));
            bool seeker = player.Role == PlayerRole.Seeker;

            string title, detail;
            if (round.StartPhase == RoundStartPhase.RoleReveal)
            {
                title = $"당신은 {(seeker ? "술래" : "도망자")}";
                detail = $"{seconds}초 뒤 시작";
            }
            else if (round.StartPhase == RoundStartPhase.SeekerHold && seeker)
            {
                title = "격리 중";
                detail = $"출발까지 {seconds}초";
            }
            else return;

            const float width = 640, height = 190;
            var rect = new Rect((TestHudSkin.Width - width) / 2, TestHudSkin.Height * 0.4f - height / 2, width, height);
            TestHudSkin.Panel(rect);
            var titleStyle = new GUIStyle(TestHudSkin.Text(72, FontStyle.Bold, TextAnchor.MiddleCenter));
            titleStyle.normal.textColor = seeker ? SeekerColor : RunnerColor;
            GUI.Label(new Rect(rect.x, rect.y + 16, rect.width, 96), title, titleStyle);
            GUI.Label(new Rect(rect.x, rect.y + 116, rect.width, 52), detail, TestHudSkin.Text(36, FontStyle.Bold, TextAnchor.MiddleCenter));
        }

        // Top center: time, gate, last-survivor phase, and the host's start/restart button.
        private void DrawRoundStatus(NetworkRound round)
        {
            const float width = 640, buttonHeight = 52;
            bool lastSurvivor = round.InLastSurvivorPhase;
            // ValvesOnly never decides a result, so the host can restart it at any time. A decided Full
            // round restarts through the result screen's rematch vote instead (기획서 12.5).
            bool hostButton = connector.IsHost && round.Mode == RoundMode.ValvesOnly;
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

        // Left: open valve count only (기획서 6.1-0 · 12.4 — which valves are active is never shown), then the controls reminder.
        private static void DrawValves(NetworkRound round)
        {
            var rect = new Rect(TestHudSkin.Margin, TestHudSkin.Margin, SideWidth, 60 + RowHeight + 220);
            TestHudSkin.Panel(rect);
            var inner = Inset(rect);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 40), "밸브", TestHudSkin.Text(30, FontStyle.Bold));

            float y = inner.y + 50;
            int open = round.OpenValveCount, required = round.RequiredOpenValveCount;
            var countStyle = new GUIStyle(TestHudSkin.Text(26, FontStyle.Bold));
            countStyle.normal.textColor = open >= required ? OpenColor : Color.white;
            GUI.Label(new Rect(inner.x, y, inner.width, 32), $"열린 수  {open} / {required}", countStyle);
            y += RowHeight;

            var help = TestHudSkin.Text(18);
            GUI.Label(new Rect(inner.x, y + 4, inner.width, 26), "E 홀드: 밸브·배수구 작업 (도망자)", help);
            GUI.Label(new Rect(inner.x, y + 30, inner.width, 26), "E: 게이트 열린 뒤 탈출구에서 탈출", help);
            GUI.Label(new Rect(inner.x, y + 56, inner.width, 26), "좌클릭: 앞쪽 1.2m 도망자 태그 (술래)", help);
            GUI.Label(new Rect(inner.x, y + 82, inner.width, 26), "Esc: 커서 풀기/잠그기", help);
            GUI.Label(new Rect(inner.x, y + 108, inner.width, 26), "Space: 점프 · Left Shift: 질주 (도망자)", help);
            GUI.Label(new Rect(inner.x, y + 134, inner.width, 26), "C: 웅크리기 · 부상자 근처 E 홀드: 치료 (9초)", help);
            GUI.Label(new Rect(inner.x, y + 160, inner.width, 26), "E: 아이템 줍기 · 4 소리통: G 녹음/설치/재생", help);
            GUI.Label(new Rect(inner.x, y + 186, inner.width, 26), "1/2/3/4: 선택 · 투척 G 홀드 조준/떼면 투척", help);
        }

        // Underwater valve (B · E) pressed without diving: shown for any B · E, active or not.
        private static void DrawValveDiveHint()
        {
            var player = NetworkPlayer.Local;
            // Kept up even once diving: a press made right after going under can still reach the server
            // before our position does, and the player should then know to press again.
            if (player == null || !player.TryGetComponent(out PlayerInteractor interactor) || !interactor.ShowsValveDiveHint) return;
            GUI.Label(new Rect(TestHudSkin.Width * .5f - 300, TestHudSkin.Height * .5f + 60, 600, 36),
                "Ctrl로 잠수해서 돌리세요", TestHudSkin.Text(24, FontStyle.Bold, TextAnchor.MiddleCenter));
        }

        private static void DrawBreath()
        {
            var player = NetworkPlayer.Local;
            if (player == null || player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive ||
                !player.TryGetComponent(out PlayerMovement movement)) return;
            var bar = new Rect(TestHudSkin.Width * .5f - 150, TestHudSkin.Height - 155, 300, 14);
            if (movement.IsSwimming)
                GUI.Label(new Rect(bar.x - 130, bar.y + 22, 560, 28), "Ctrl 홀드: 잠수 · 마우스: 수중 방향 · 해제: 부상 · 가장자리 Space: 나오기", TestHudSkin.Text(16, FontStyle.Normal, TextAnchor.MiddleCenter));
            if (!movement.IsDiving) return;

            float value = movement.BreathSeconds;
            Color saved = GUI.color;
            if (value <= 1f)
            {
                GUI.color = new Color(.7f, .02f, .02f, .18f + .08f * Mathf.Sin(Time.time * 8f));
                GUI.DrawTexture(new Rect(0, 0, TestHudSkin.Width, 45), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, TestHudSkin.Height - 45, TestHudSkin.Width, 45), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, 0, 45, TestHudSkin.Height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(TestHudSkin.Width - 45, 0, 45, TestHudSkin.Height), Texture2D.whiteTexture);
            }
            GUI.color = new Color(.08f, .12f, .16f, .9f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = value <= 1f ? Color.red : value <= 3f ? Color.yellow : new Color(.3f, .8f, 1f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * value / PlayerMovement.MaxBreath, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(bar.x + bar.width / PlayerMovement.MaxBreath, bar.y - 3, 2, 20), Texture2D.whiteTexture);
            GUI.color = saved;
            string warning = value <= 1f ? " · 즉시 부상!" : value <= 3f ? " · 숨 부족" : "";
            GUI.Label(new Rect(bar.x - 90, bar.y - 30, 480, 28), $"숨 {value:0.0} / 14초{warning}", TestHudSkin.Text(20, FontStyle.Bold, TextAnchor.MiddleCenter));
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

            var center = new Vector2(TestHudSkin.Width * 0.5f, TestHudSkin.Height * 0.5f);
            RuinedUi.HealingRing(new Rect(center.x - 54, center.y - 54, 108, 108), player.HealingActionProgress);

            string label = player.HealingActionIsBandage ? "붕대 감는 중" : "동료 치료 중";
            GUI.Label(new Rect(center.x - 100, center.y + 60, 200, 24), label,
                TestHudSkin.Text(17, FontStyle.Bold, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(center.x - 42, center.y - 14, 84, 28),
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
            var panel = new Rect(TestHudSkin.Margin, TestHudSkin.Height - TestHudSkin.Margin - height - 30, width, height);
            TestHudSkin.Panel(panel);

            GUI.Label(new Rect(panel.xMax - 150, panel.y + 8, 134, 28),
                $"{currentHalves / 2f:F1} / {totalHalves / 2f:F1}",
                TestHudSkin.Text(20, FontStyle.Bold, TextAnchor.MiddleRight));

            float barX = panel.x + 16, barY = panel.y + 44, barWidth = panel.width - 32;
            RuinedUi.Health(new Rect(barX, barY, barWidth, 18), currentHalves);
            float staminaRatio = player.TryGetComponent(out PlayerMovement movement)
                ? movement.SprintStaminaRatio : 1f;
            RuinedUi.Stamina(new Rect(barX, panel.yMax + 10, barWidth, 12), staminaRatio);
        }

        private static void DrawRunnerInventory()
        {
            var player = NetworkPlayer.Local;
            if (player == null || player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive) return;

            const float width = 404, height = 182;
            var panel = new Rect(TestHudSkin.Width - TestHudSkin.Margin - width,
                TestHudSkin.Height - TestHudSkin.Margin - height, width, height);
            TestHudSkin.Panel(panel);
            GUI.Label(new Rect(panel.x + 8, panel.y + 3, panel.width - 16, 22),
                "아이템 · 1/2/3/4 슬롯", TestHudSkin.Text(18, FontStyle.Bold));

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
            var decoyLabel = new GUIStyle(TestHudSkin.Text(17, FontStyle.Bold, TextAnchor.MiddleCenter));
            decoyLabel.normal.textColor = selectedSlot == 3 ? new Color(1f, .72f, .3f) : Color.white;
            GUI.Label(new Rect(panel.x + 208, panel.y + 26, 88, 20), "3 · 오리", decoyLabel);
            var decoyRect = new Rect(panel.x + 220, panel.y + 47, 64, 64);
            RuinedUi.Item(decoyRect, 3, player.DecoyCount > 0, selectedSlot == 3);
            var canLabel = new GUIStyle(TestHudSkin.Text(17, FontStyle.Bold, TextAnchor.MiddleCenter));
            canLabel.normal.textColor = selectedSlot == 4 ? new Color(1f, .72f, .3f) : Color.white;
            GUI.Label(new Rect(panel.x + 308, panel.y + 26, 88, 20), "4 · 소리통", canLabel);
            var canRect = new Rect(panel.x + 320, panel.y + 47, 64, 64);
            RuinedUi.Item(canRect, 4, player.SoundCanStage != SoundCanStage.None, selectedSlot == 4);
            string canStatus = player.SoundCanStage switch
            {
                SoundCanStage.Empty => "녹음 전", SoundCanStage.Recording => $"{player.SoundCanRecordingProgress:P0}",
                SoundCanStage.Recorded => "설치 전", SoundCanStage.Placing => "설치 중", SoundCanStage.Placed => "리모컨", SoundCanStage.Playing => "재생 중", _ => "없음"
            };
            GUI.Label(new Rect(canRect.x - 10, canRect.yMax, canRect.width + 20, 18), canStatus, TestHudSkin.Text(12, FontStyle.Bold, TextAnchor.MiddleCenter));
            string usage = selectedSlot switch
            {
                1 => "G 홀드: 붕대 사용", 2 => "Q/우클릭: 찰칵이 사용", 3 => "G 홀드: 궤도 · 떼면 투척",
                _ => player.SoundCanStage switch
                {
                    SoundCanStage.Empty => "G: 목소리 3초 녹음",
                    SoundCanStage.Recording => $"녹음 {player.SoundCanRecordingProgress * 3f:F1}/3초 · G: 취소",
                    SoundCanStage.Recorded => "G: 앞쪽 바닥에 설치", SoundCanStage.Placed => "G: 설치한 소리통 원격 재생",
                    SoundCanStage.Placing => $"소리통 설치 {player.SoundCanRecordingProgress:P0} · G/Esc: 취소",
                    SoundCanStage.Playing => "녹음된 목소리 재생 중", _ => "E: 소리통 줍기"
                }
            };
            GUI.Label(new Rect(panel.x + 8, panel.y + 137, panel.width - 16, 20), usage,
                TestHudSkin.Text(14, FontStyle.Normal, TextAnchor.MiddleCenter));
            if (selectedSlot == 4 && interactor != null && !string.IsNullOrEmpty(interactor.SoundCanHint))
                GUI.Label(new Rect(panel.x + 8, panel.y + 158, panel.width - 16, 20), interactor.SoundCanHint,
                    TestHudSkin.Text(12, FontStyle.Normal, TextAnchor.MiddleCenter));
        }

        private static void DrawBandageSlot(Rect rect, int count, bool selected)
            => RuinedUi.Item(rect, 1, count > 0, selected);

        private static void DrawClickerSlot(Rect rect, int count, bool selected)
            => RuinedUi.Item(rect, 2, count > 0, selected);

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

        // Center: result screen (기획서 12.5) — podium (banner, everyone posing in a row, name/result/award
        // slot) with the rematch vote at the bottom; see ResultPodiumView. The sound map time-lapse and
        // clip saving are post-MVP and not drawn.
        private void DrawResult(NetworkRound round)
        {
            if (!round.IsResultScreenShown) return;
            podium.Draw(round);
        }

        private static Rect Inset(Rect rect) => new(rect.x + TestHudSkin.Padding, rect.y + TestHudSkin.Padding,
            rect.width - TestHudSkin.Padding * 2, rect.height - TestHudSkin.Padding * 2);

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{total / 60}:{total % 60:00}";
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
            RoundResult.RunnersWin => "도망자 승리",
            RoundResult.SeekersWin => "술래 승리",
            RoundResult.Void => "무효",
            _ => "진행 중",
        };
    }
}
