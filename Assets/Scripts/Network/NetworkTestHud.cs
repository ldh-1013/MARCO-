using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Round.ResultPodium;
using Marco.Network.Vision;
using UnityEngine;

namespace Marco.Network
{
    // Role-aware play HUD. The lobby owns the screen until the round begins.
    [RequireComponent(typeof(LanConnector))]
    public sealed partial class NetworkTestHud : MonoBehaviour
    {
        private static readonly Color Gold = new(.96f, .78f, .46f);

        private static readonly Color SeekerColor = new(0.95f, 0.3f, 0.3f);
        private static readonly Color RunnerColor = new(0.35f, 0.6f, 1f);
        private static readonly Color MutedColor = new(0.6f, 0.6f, 0.6f);
        private static readonly Color OpenColor = new(0.3f, 0.85f, 0.4f);
        private static readonly Color RotatingColor = new(1f, 0.85f, 0.2f);

        private LanConnector connector;
        private ResultPodiumView podium;
        private NetworkPlayer hudVoiceOwner;
        private MicrophoneVisionController hudVoice;

        private void Awake()
        {
            connector = GetComponent<LanConnector>();
            podium = GetComponent<ResultPodiumView>();
            if (podium == null) podium = gameObject.AddComponent<ResultPodiumView>();
        }

        private void OnGUI()
        {
            if (Settings.SettingsMenu.IsOpen) return;   // IMGUI draws over uGUI: stay out of the settings window
            if (connector.Status != LanConnectionStatus.Connected) return;
            var round = NetworkRound.Instance;
            if (round == null || round.Mode == RoundMode.NotStarted) return;

            TestHudSkin.Begin();
            if (round.IsResultScreenShown)
            {
                podium.Draw(round);
                return;
            }
            // The eliminated Echo's 3.0 s tag blackout covers the 3D view first; the HUD below stays on top of it.
            Marco.Network.Vision.TagBlackoutLocal.Draw(NetworkPlayer.Local, TestHudSkin.Width, TestHudSkin.Height);
            DrawEndEdgePulse(round);
            DrawSeekerReticle(round);
            var local = NetworkPlayer.Local;
            if (hudVoiceOwner != local)
            {
                hudVoiceOwner = local;
                hudVoice = local != null ? local.GetComponentInChildren<MicrophoneVisionController>(true) : null;
            }
            bool aliveRunner = local != null && local.Role == PlayerRole.Runner && local.Status == PlayerStatus.Alive;
            float paperOpacity = aliveRunner ? Mathf.SmoothStep(0f, 1f, hudVoice != null ? hudVoice.HudVisionLevel : 0f) : 1f;
            if (aliveRunner &&
                !local.HasHealingAction && round.Result == RoundResult.InProgress)
                RuinedUi.Reticle(new Vector2(TestHudSkin.Width * .5f, TestHudSkin.Height * .5f), Color.white, 24);
            DrawIdentity(local, paperOpacity);
            float statusBottom = DrawRoundStatus(round, paperOpacity);
            DrawValves(round, paperOpacity);
            DrawRunnerHealth(round, paperOpacity);
            DrawRunnerInventory(paperOpacity);

            DrawEchoBodyBanner(round, statusBottom);
            DrawKnockHud(round, statusBottom);
            DrawEchoCompass(round);
            DrawLureNotice(round, statusBottom);
            DrawValveWork(round, paperOpacity);
            DrawShoutGauge(round);
            DrawHealingProgress();
            DrawBreath();
            DrawValveDiveHint();
            DrawRoundStart(round, paperOpacity);
        }

        // Center: own role only for the 5 s role reveal, then the Seeker's hold countdown (기획서 부록 A [라운드 시작]).
        private static void DrawRoundStart(NetworkRound round, float paperOpacity)
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
            TestHudSkin.GamePanel(rect, paperOpacity);
            Lobby.RoomLettering.Draw(new Rect(rect.x, rect.y + 14, rect.width, 96), title, 62, seeker ? SeekerColor : RunnerColor, true);
            if (seconds <= 3)
            {
                string action = round.StartPhase == RoundStartPhase.RoleReveal ? "초 뒤 시작" : "초 뒤 출발";
                var style = TestHudSkin.Text(36, FontStyle.Bold);
                float textWidth = style.CalcSize(new GUIContent(action)).x;
                float x = rect.center.x - (44 + textWidth) * .5f;
                InterfaceArt.Draw(new Rect(x, rect.y + 120, 36, 44), "count" + seconds);
                GUI.Label(new Rect(x + 44, rect.y + 116, textWidth + 2, 52), action, style);
            }
            else GUI.Label(new Rect(rect.x, rect.y + 116, rect.width, 52), detail,
                TestHudSkin.Text(36, FontStyle.Bold, TextAnchor.MiddleCenter));
        }

        // Top center, under the round panel (기획서 12.4 탈출자 화면 · 연출.md 5): "탈출 성공" in the HUD's own
        // white — no knock count, cooldown ring or 20 s lock, since an escapee never knocks (3.3-3).
        // A mid-round joiner wears the same body (13.3) and is told when they take part instead.
        // The eliminated Echo's own HUD (knock) is NetworkTestHud.Knock.
        private static void DrawEchoBodyBanner(NetworkRound round, float top)
        {
            string title = EchoBodyBannerTitle(round, NetworkPlayer.Local);
            if (title == null) return;
            var rect = EchoBodyBannerRect(TestHudSkin.Width, top);
            TestHudSkin.GamePanel(rect);
            Lobby.RoomLettering.Draw(EchoBodyBannerTitleRect(rect), title, 36, centered: true);
            GUI.Label(EchoBodyBannerDetailRect(rect), EchoBodyBannerDetail, TestHudSkin.Text(20, FontStyle.Normal, TextAnchor.MiddleCenter));
        }

        // Layout and text are public so the batch-mode render check (no IMGUI there) draws the same banner.
        public const string EchoBodyBannerDetail = "Space 상승 · Left Ctrl 하강 · 벽 통과";
        public const float RoundPanelHeight = 130;

        public static string EchoBodyBannerTitle(NetworkRound round, NetworkPlayer player)
        {
            if (round == null || player == null || round.Result != RoundResult.InProgress) return null;
            if (player.IsEscapee) return "탈출 성공";
            if (player.Status == PlayerStatus.Spectating) return "다음 판부터 참가";
            return null;
        }

        // canvasWidth: the 1920-wide reference canvas (TestHudSkin.Width); top: bottom of the round panel.
        public static Rect EchoBodyBannerRect(float canvasWidth, float top)
        {
            const float width = 640, height = 100;
            return new Rect((canvasWidth - width) / 2, top + 12, width, height);
        }

        public static Rect EchoBodyBannerTitleRect(Rect banner) => new(banner.x, banner.y + 10, banner.width, 52);
        public static Rect EchoBodyBannerDetailRect(Rect banner) => new(banner.x, banner.y + 62, banner.width, 28);

        // Top center: time, gate, last-survivor phase, and the host's start/restart button.
        // Returns the panel's bottom edge so the escapee banner can sit right under it.
        private float DrawRoundStatus(NetworkRound round, float paperOpacity)
        {
            const float width = 476, buttonHeight = 52;
            bool lastSurvivor = round.InLastSurvivorPhase;
            // ValvesOnly never decides a result, so the host can restart it at any time. A decided Full
            // round restarts through the result screen's rematch vote instead (기획서 12.5).
            bool hostButton = connector.IsHost && round.Mode == RoundMode.ValvesOnly;
            float height = (lastSurvivor ? 220 : RoundPanelHeight) + (hostButton ? buttonHeight + 12 : 0);
            var rect = new Rect((TestHudSkin.Width - width) / 2, TestHudSkin.Margin, width, height);
            TestHudSkin.GamePanel(rect, paperOpacity);
            var inner = Inset(rect);

            if (hostButton) DrawHostButton(round, new Rect(inner.x, inner.yMax - buttonHeight, inner.width, buttonHeight));

            if (round.Mode == RoundMode.Full)
            {
                Lobby.RoomLettering.Draw(new Rect(inner.center.x - 156, inner.y, 138, 52), "남은 시간", 30, centered: true);
                InterfaceArt.Number(new Rect(inner.center.x, inner.y + 6, 160, 40), FormatTime(round.RemainingSeconds));
            }
            else Lobby.RoomLettering.Draw(new Rect(inner.x, inner.y, inner.width, 52), "밸브 연습", 32, centered: true);
            bool hasEscapeScore = round.EscapeRequiredCount > 0;
            float gateX = hasEscapeScore ? inner.x + 8 : inner.center.x - 96;
            DrawInkIcon(new Rect(gateX, inner.y + 53, 42, 42), round.GateOpen ? "gateOpen" : "gateClosed");
            Lobby.RoomLettering.Draw(new Rect(gateX + 49, inner.y + 55, 128, 39),
                round.GateOpen ? "게이트 열림" : "게이트 닫힘", 23, round.GateOpen ? OpenColor : Gold);
            if (hasEscapeScore) DrawEscapeScore(round, new Rect(inner.center.x - 12, inner.y + 53, inner.width * .5f + 12, 42));

            if (!lastSurvivor) return rect.yMax;
            float y = inner.y + 100;
            InterfaceArt.Draw(new Rect(inner.center.x - 160, y, 174, 36), "last");
            InterfaceArt.Draw(new Rect(inner.center.x + 16, y, 130, 36), "frame");
            InterfaceArt.Number(new Rect(inner.center.x + 24, y + 6, 112, 24), FormatTime(round.LastSurvivorRemainingSeconds));
            if (round.TryGetActiveDrainHatch(out var drain))
            {
                var runner = NetworkPlayer.Local;
                if (runner != null && runner.Role == PlayerRole.Runner && runner.Status == PlayerStatus.Alive &&
                    Escape.DrainHatchView.TryGet(drain, out var hatch))
                {
                    var offset = hatch.transform.position - runner.transform.position;
                    if (offset.x * offset.x + offset.z * offset.z > .01f)
                    {
                        float bearing = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg - runner.transform.eulerAngles.y;
                        var arrow = new Rect(inner.center.x - 197, y + 4, 27, 27);
                        var matrix = GUI.matrix;
                        GUIUtility.RotateAroundPivot(bearing, arrow.center);
                        InterfaceArt.Draw(arrow, "drainArrow");
                        GUI.matrix = matrix;
                    }
                }
                InterfaceArt.Draw(new Rect(inner.x, y + 44, 26, 28), "drain");
                InterfaceArt.Draw(new Rect(inner.x + 30, y + 44, 105, 28), "activeDrain");
                InterfaceArt.Number(new Rect(inner.x + 137, y + 46, 26, 24), drain == DrainHatchId.First ? "1" : "2");
                TestHudSkin.Bar(new Rect(inner.x + 170, y + 48, inner.width - 260, 20), round.DrainHatchProgress, RotatingColor);
                GUI.Label(new Rect(inner.xMax - 80, y + 44, 80, 28), $"{round.DrainHatchProgress:P0}",
                    TestHudSkin.Text(22, FontStyle.Normal, TextAnchor.MiddleRight));
            }
            else GUI.Label(new Rect(inner.x, y + 44, inner.width, 28), "배수구 없음 (게이트 열림)",
                TestHudSkin.Text(22, FontStyle.Normal, TextAnchor.MiddleCenter));
            return rect.yMax;
        }

        private static void DrawEscapeScore(NetworkRound round, Rect row)
        {
            const float slotSize = 30, slotGap = 5;
            int required = round.EscapeRequiredCount, escaped = round.EscapedCount;
            float x = row.x + (row.width - 108 - required * (slotSize + slotGap)) * .5f;
            var accent = escaped >= required ? new Color(.55f, .94f, .85f) : Color.white;
            Lobby.RoomLettering.Draw(new Rect(x, row.y, 43, row.height), "탈출", 23, accent);
            InterfaceArt.Number(new Rect(x + 47, row.y + 8, 52, 26), $"{escaped}/{required}");
            for (int slot = 0; slot < required; slot++)
                InterfaceArt.Draw(new Rect(x + 108 + slot * (slotSize + slotGap), row.y + 6, slotSize, slotSize),
                    slot < escaped ? "ready" : "waiting");
        }

        private static void DrawHostButton(NetworkRound round, Rect rect)
        {
            if (Lobby.LobbyMenu.RoomButton(rect, "다시 시작", 23, useLettering: true)) round.RestartRound();
        }

        // These two stamps use dark ink. A paper backing preserves contrast on the charcoal HUD.
        private static void DrawInkIcon(Rect rect, string key)
        {
            var paper = InterfaceArt.Regions["paper"];
            var texture = RuinedUi.Texture(InterfaceArt.Atlas);
            if (texture != null)
                RuinedUi.Region(rect, InterfaceArt.Atlas, paper, new Vector2(texture.width, texture.height));
            InterfaceArt.Draw(new Rect(rect.x + 5, rect.y + 5, rect.width - 10, rect.height - 10), key);
        }

        // Show the shared count and the local interaction without revealing other active valves.
        private static readonly string[] ValveFrames = { "progress0", "progress1", "progress2", "progress3", "progress4" };

        private const float ValveRowHeight = 48, ValveRowGap = 6;
        private static Rect ValvePanelRect(int required) =>
            new(TestHudSkin.Margin, TestHudSkin.Margin, 166, 58 + required * (ValveRowHeight + ValveRowGap));

        private static void DrawValves(NetworkRound round, float paperOpacity)
        {
            int required = round.RequiredOpenValveCount;
            if (required <= 0) return;

            var rect = ValvePanelRect(required);
            TestHudSkin.GamePanel(rect, paperOpacity);
            var inner = Inset(rect);
            Lobby.RoomLettering.Draw(new Rect(inner.x, inner.y, inner.width, 23), "밸브", 21, Gold);

            int open = round.OpenValveCount;
            for (int slot = 0; slot < required; slot++)
            {
                bool isOpen = slot < open;
                var row = new Rect(inner.x, inner.y + 30 + slot * (ValveRowHeight + ValveRowGap), inner.width, ValveRowHeight);
                Color state = isOpen ? Gold : new Color(.65f, .63f, .59f);
                TestHudSkin.Fill(row, isOpen ? new Color(.29f, .21f, .11f, .8f * paperOpacity) : new Color(0, 0, 0, .3f * paperOpacity));
                TestHudSkin.Fill(new Rect(row.x, row.y, 2, row.height), state);
                DrawInkIcon(new Rect(row.x + 8, row.y + 4, 40, 40), isOpen ? "valveOpen" : "valve");
                Lobby.RoomLettering.Draw(new Rect(row.x + 55, row.y, row.width - 59, row.height),
                    isOpen ? "열림" : "닫힘", 17, state, centered: true);
            }
        }

        private static void DrawValveWork(NetworkRound round, float paperOpacity)
        {
            var player = NetworkPlayer.Local;
            if (player != null && player.Status == PlayerStatus.Alive && player.Role == PlayerRole.Runner &&
                player.TryGetComponent(out PlayerInteractor interactor) && interactor.WorkingValve is ValveId id &&
                round.TryGetValveState(id, out var valve))
            {
                var rect = ValvePanelRect(round.RequiredOpenValveCount);
                var work = new Rect(rect.x, rect.yMax + 8, rect.width, 55);
                TestHudSkin.GamePanel(work, paperOpacity);
                var box = new Rect(work.x + 12, work.y + 7, 40, 40);
                Lobby.RoomLettering.Draw(new Rect(box.xMax + 8, work.y, work.xMax - box.xMax - 20, work.height),
                    "작업 중", 17, Gold, centered: true);
                string border = valve.Status switch
                {
                    ValveStatus.Reflowing => "reflowFrame",
                    ValveStatus.Closed => "closedFrame",
                    _ => ValveFrames[Mathf.RoundToInt(Mathf.Clamp01(valve.Progress) * 4)]
                };
                InterfaceArt.Draw(box, border);
                string wheel = valve.Status switch
                {
                    ValveStatus.Reflowing => "reflow", ValveStatus.Rotating => "rotating",
                    ValveStatus.Open => "valveOpen", _ => "valve"
                };
                InterfaceArt.Draw(new Rect(box.x + 5, box.y + 5, 30, 30), wheel);
            }
        }

        // Underwater valve (B · E) pressed without diving: shown for any B · E, active or not.
        private static void DrawValveDiveHint()
        {
            var player = NetworkPlayer.Local;
            // Kept up even once diving: a press made right after going under can still reach the server
            // before our position does, and the player should then know to press again.
            if (player == null || player.HasEchoBody ||
                !player.TryGetComponent(out PlayerInteractor interactor) || !interactor.ShowsValveDiveHint) return;
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

        private const float HealthPanelHeight = 110;
        internal const float RunnerHealthTopFromBottom = TestHudSkin.Margin + HealthPanelHeight;

        private static void DrawRunnerHealth(NetworkRound round, float paperOpacity)
        {
            var player = NetworkPlayer.Local;
            if (player == null || player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive) return;

            int totalHalves = Mathf.Max(1, round.FullHalves);
            int currentHalves = player.HealthHalves < 0 ? totalHalves : Mathf.Clamp(player.HealthHalves, 0, totalHalves);
            var panel = new Rect(TestHudSkin.Margin, TestHudSkin.Height - RunnerHealthTopFromBottom, 330, HealthPanelHeight);
            TestHudSkin.GamePanel(panel, paperOpacity);

            float barX = panel.x + 22, barWidth = panel.width - 44;
            Lobby.RoomLettering.Draw(new Rect(barX, panel.y + 12, 80, 27), "체력", 23);
            Lobby.RoomLettering.Draw(new Rect(panel.xMax - 132, panel.y + 12, 112, 27),
                $"{currentHalves / 2f:0.#} / {totalHalves / 2f:0.#}", 21, centered: true);
            RuinedUi.Health(new Rect(barX, panel.y + 46, barWidth, 16), currentHalves, totalHalves);
            Lobby.RoomLettering.Draw(new Rect(barX, panel.y + 72, 80, 23), "기력", 17, Gold);
            float staminaRatio = player.TryGetComponent(out PlayerMovement movement)
                ? movement.SprintStaminaRatio : 1f;
            RuinedUi.Stamina(new Rect(barX + 66, panel.y + 78, barWidth - 66, 11), staminaRatio);
        }

        private static void DrawRunnerInventory(float paperOpacity)
        {
            var player = NetworkPlayer.Local;
            if (player == null || player.Role != PlayerRole.Runner || player.Status != PlayerStatus.Alive) return;

            const float width = 404, height = 182;
            var panel = new Rect(TestHudSkin.Width - TestHudSkin.Margin - width,
                TestHudSkin.Height - TestHudSkin.Margin - height, width, height);
            TestHudSkin.GamePanel(panel, paperOpacity);
            int selectedSlot = player.TryGetComponent(out PlayerInteractor interactor) ? interactor.SelectedItemSlot : 1;
            string item = selectedSlot switch { 1 => "붕대", 2 => "찰칵이", 3 => "오리", _ => "소리통" };
            Lobby.RoomLettering.Draw(new Rect(panel.x + 22, panel.y + 10, 180, 28), item, 23, Gold);
            for (int slot = 1; slot <= 4; slot++)
            {
                int count = slot switch { 1 => player.BandageCount, 2 => player.ClickerCount,
                    3 => player.DecoyCount, _ => player.SoundCanStage != SoundCanStage.None ? 1 : 0 };
                var cell = new Rect(panel.x + 26 + (slot - 1) * 93, panel.y + 45, 68, 68);
                RuinedUi.Item(cell, slot, count > 0, selectedSlot == slot);
                Lobby.RoomLettering.Draw(new Rect(cell.x - 10, cell.y, 20, 20), slot.ToString(), 15,
                    selectedSlot == slot ? Gold : Color.white);
                if (count > 1) Lobby.RoomLettering.Draw(new Rect(cell.xMax - 22, cell.yMax - 21, 25, 21),
                    count.ToString(), 17, centered: true);
            }
            string usage = selectedSlot switch
            {
                1 => "G 홀드: 붕대 사용", 2 => "Q/우클릭: 찰칵이 사용", 3 => "G 홀드: 궤도 · 떼면 투척",
                _ => player.SoundCanStage switch
                {
                    SoundCanStage.Empty => "G: 목소리 3초 녹음",
                    SoundCanStage.Recording => $"녹음 {player.SoundCanRecordingProgress * 3f:F1}/3초 · G: 취소",
                    SoundCanStage.Recorded => "G: 앞쪽 바닥에 설치", SoundCanStage.Placed => "G: 설치한 소리통 원격 재생",
                    SoundCanStage.Placing => $"소리통 설치 {player.SoundCanRecordingProgress:P0} · G: 취소",
                    SoundCanStage.Playing => "녹음된 목소리 재생 중", _ => "E: 소리통 줍기"
                }
            };
            GUI.Label(new Rect(panel.x + 8, panel.y + 116, panel.width - 16, 28), usage,
                TestHudSkin.Text(19, FontStyle.Normal, TextAnchor.MiddleCenter));
            if (selectedSlot == 4 && interactor != null && !string.IsNullOrEmpty(interactor.SoundCanHint))
                GUI.Label(new Rect(panel.x + 8, panel.y + 144, panel.width - 16, 26), interactor.SoundCanHint,
                    TestHudSkin.Text(17, FontStyle.Normal, TextAnchor.MiddleCenter));
        }

        private static void DrawIdentity(NetworkPlayer player, float paperOpacity)
        {
            if (player == null) return;
            const float width = 248;
            var rect = new Rect(TestHudSkin.Width - TestHudSkin.Margin - width, TestHudSkin.Margin, width, 96);
            TestHudSkin.GamePanel(rect, paperOpacity);
            var accent = player.Role == PlayerRole.Seeker ? SeekerColor : player.Role == PlayerRole.Runner ? RunnerColor : Gold;
            TestHudSkin.Fill(new Rect(rect.x + 20, rect.y + 20, 3, 56), accent);
            Lobby.RoomLettering.Draw(new Rect(rect.x + 36, rect.y + 12, width - 56, 40), RoleLabel(player.Role), 33);
            Lobby.RoomLettering.Draw(new Rect(rect.x + 36, rect.y + 55, width - 56, 26),
                player.IsCrouching ? "웅크림" : StatusLabel(player.Status), 18, accent);
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
            PlayerStatus.Spectating => "관전 중",
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
