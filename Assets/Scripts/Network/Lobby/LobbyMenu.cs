using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Lobby
{
    // The prototype lobby (RoomSessionPrototype's OnGUI) on top of the FishNet session: nickname,
    // Create room (Unity Relay), Join (room code or public room list),
    // and a shared ready countdown. Until the round starts it covers the game screen.
    // In game, an unlocked cursor shows 계속하기 / 게임 나가기 · 로비로.
    [RequireComponent(typeof(LanConnector), typeof(UnityRelayLobby))]
    public sealed class LobbyMenu : MonoBehaviour
    {
        [Header("Lobby layout (1000 x 700 reference canvas)")]
        [SerializeField] private Vector2 menuPosition = new(8, 330);
        [SerializeField] private Vector2 nicknamePosition = new(350, 140);
        [SerializeField, Range(24, 54)] private int menuFontSize = 40;

        private LanConnector connector;
        private UnityRelayLobby relay;
        private string playerName = "";
        private string status = "";
        private Vector2 rosterScroll, roomStatusScroll;
        private bool showJoinInput;
        private LanConnectionStatus lastStatus;
        private bool leavingOnPurpose;
        private NetworkPlayer nicknameSentFor;
        private bool wasOpen = true;
        private string relayJoinInput = "";
        private LobbyIntro intro;
        private LobbyCctvBackdrop backdrop;
        private LobbyMicCalibration micCalibration;
        public bool ShowsBackdrop => IsOpen && (intro == null || !intro.IsPlaying);

        private bool Joined => connector.Status == LanConnectionStatus.Connected;
        private static bool GameStarted => NetworkRound.Instance != null && NetworkRound.Instance.Mode != RoundMode.NotStarted;
        // Lobby screens are up whenever the round has not started for this player.
        public bool IsOpen => !Joined || !GameStarted;
        // In a room, before the round (the room panel is up); visible = and not hidden by the intro.
        public bool InRoomLobby => Joined && !GameStarted;
        public bool RoomLobbyVisible => InRoomLobby && (intro == null || !intro.IsPlaying);

        private void Awake()
        {
            connector = GetComponent<LanConnector>();
            relay = GetComponent<UnityRelayLobby>();
            intro = GetComponent<LobbyIntro>();
            if (intro == null) intro = gameObject.AddComponent<LobbyIntro>();
            backdrop = GetComponent<LobbyCctvBackdrop>();
            if (backdrop == null) backdrop = gameObject.AddComponent<LobbyCctvBackdrop>();
            micCalibration = GetComponent<LobbyMicCalibration>();
            if (micCalibration == null) micCalibration = gameObject.AddComponent<LobbyMicCalibration>();
        }

        private void Update()
        {
            if (IsOpen && Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            // The host pressed 게임 시작: hand the cursor straight to the game.
            if (wasOpen && !IsOpen)
            {
                Cursor.lockState = CursorLockMode.Locked;
                if (connector.IsHost) relay.LockLobbyForGame();
            }
            wasOpen = IsOpen;
            TrackConnection();

            // Tell the server our nickname once our player exists (the prototype's NAME message).
            var local = NetworkPlayer.Local;
            if (local != null && local != nicknameSentFor && local.IsClientInitialized)   // not while leaving (the RPC would be dropped)
            {
                nicknameSentFor = local;
                local.SetNicknameServer(playerName);
            }
        }

        private void TrackConnection()
        {
            var now = connector.Status;
            if (now == lastStatus) return;
            if (now == LanConnectionStatus.Connected)
                status = connector.IsHost ? "친구를 초대하고 참가자를 기다리세요." : "방 입장 완료";
            else if (now == LanConnectionStatus.Failed)
            {
                status = connector.TimedOut ? "접속 시간 초과. 주소와 연결 상태를 확인하세요."
                    : "연결 실패. 네트워크 상태를 확인하세요.";
                if (connector.UsingRelay) relay.Stop();
            }
            else if (now == LanConnectionStatus.Offline && lastStatus == LanConnectionStatus.Connected)
            {
                if (!leavingOnPurpose) status = "호스트와 연결이 종료되었습니다.";
                relay.LeaveLobby();
            }
            if (now != LanConnectionStatus.Connected) nicknameSentFor = null;
            leavingOnPurpose = false;
            lastStatus = now;
        }

        private void Leave(string message)
        {
            leavingOnPurpose = true;
            if (connector.UsingRelay) relay.Stop();
            else connector.Leave();
            showJoinInput = false;
            status = message;
            Cursor.lockState = CursorLockMode.None;
        }

        private void OnGUI()
        {
            if (Marco.Network.Settings.SettingsMenu.IsOpen) return;   // the settings window (uGUI) is on top
            if (intro != null && intro.IsPlaying) return;
            if (!IsOpen)
            {
                DrawInGameMenu();
                return;
            }

            // Opaque backdrop over the game view; as a HUD panel it also keeps clicks from relocking the cursor.
            TestHudSkin.Begin();
            var full = new Rect(0, 0, TestHudSkin.Width, TestHudSkin.Height);
            TestHudSkin.Panel(full);
            backdrop.Draw(full);

            GUI.depth = 0;
            Matrix4x4 previousMatrix = GUI.matrix;
            bool previousEnabled = GUI.enabled;
            float scale = Mathf.Min(Screen.width / 1000f, Screen.height / 700f);
            float offsetX = (Screen.width - 1000 * scale) * 0.5f;
            float offsetY = (Screen.height - 700 * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            if (Joined) DrawRoom();
            else DrawEntryMenu(scale, offsetX);
            GUI.matrix = previousMatrix;
            GUI.enabled = previousEnabled;
        }

        // Prototype: shown in game while the cursor is free.
        private void DrawInGameMenu()
        {
            if (Cursor.lockState == CursorLockMode.Locked) return;
            // The result screen frees the cursor for its own vote button; leaving goes through the vote.
            if (NetworkRound.Instance != null && NetworkRound.Instance.IsResultScreenShown) return;
            TestHudSkin.Begin();
            var rect = new Rect(TestHudSkin.Width - 300, TestHudSkin.Height - 372, 276, 146);
            TestHudSkin.GamePanel(rect);
            GUI.depth = -10;
            if (RuinedUi.Button(new Rect(rect.x + 16, rect.y + 16, rect.width - 32, 52), "계속하기", 22, light: true))
                Cursor.lockState = CursorLockMode.Locked;
            if (RuinedUi.Button(new Rect(rect.x + 16, rect.y + 78, rect.width - 32, 52), "게임 나가기 · 로비로", 22, light: true))
                Leave("게임에서 나왔습니다.");
        }

        internal static void RoomPanel(Rect rect)
        {
            var texture = RuinedUi.Texture(InterfaceArt.Atlas);
            if (texture == null) TestHudSkin.Fill(rect, new Color(.09f, .09f, .085f, .98f * GUI.color.a));
            else RuinedUi.Region(rect, InterfaceArt.Atlas, InterfaceArt.Regions["paper"],
                new Vector2(texture.width, texture.height), new Color(.24f, .235f, .22f));
        }

        internal static bool RoomButton(Rect rect, string label, int size, bool primary = false, bool useLettering = false)
        {
            string key = "button/" + label;
            if (RoomArt.Has(key))
            {
                bool pressed = GUI.Button(rect, GUIContent.none, GUIStyle.none);
                Color saved = GUI.color;
                if (!GUI.enabled) GUI.color *= new Color(.68f, .68f, .68f);
                else if (rect.Contains(Event.current.mousePosition)) GUI.color *= new Color(1f, .91f, .73f);
                RoomArt.Draw(rect, key);
                GUI.color = saved;
                return pressed;
            }
            Color face = primary && GUI.enabled ? new Color(.44f, .33f, .15f) : new Color(.12f, .12f, .11f);
            TestHudSkin.Fill(rect, face);
            TestHudSkin.Fill(new Rect(rect.x, rect.yMax - 2, rect.width, 2),
                primary && GUI.enabled ? new Color(.9f, .71f, .39f) : new Color(.43f, .41f, .36f));
            if (!useLettering) return RuinedUi.Button(rect, label, size, light: true);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            Color tint = rect.Contains(Event.current.mousePosition) ? new Color(1, .86f, .56f) : Color.white;
            RoomLettering.Draw(rect, label, size, tint, centered: true);
            return clicked;
        }

        private void DrawRoom()
        {
            const float width = 720, pad = 22, inner = width - 2 * pad, row = 32;
            int members = 0, ready = 0;
            foreach (var player in NetworkPlayer.All)
            {
                if (string.IsNullOrEmpty(player.Nickname)) continue;
                members++;
                if (player.LobbyReady) ready++;
            }
            var local = NetworkPlayer.Local;
            int countdown = NetworkRound.Instance != null ? NetworkRound.Instance.LobbyCountdown : 0;
            float rosterHeight = Mathf.Clamp(members, 2, 5) * row;
            string message = connector.UsingRelay ? relay.Status : status;

            var statusText = new GUIContent(message);
            var caption = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            float messageHeight = caption.CalcHeight(statusText, inner - 24);
            float statusHeight = Mathf.Clamp(messageHeight, 26, 64);
            float actionsY = 138 + rosterHeight + (connector.IsHost ? 56 : 8);
            float panelHeight = actionsY + 56 + statusHeight + pad;
            const float calibrationHeight = 152, gap = 12;
            float total = panelHeight + (micCalibration.HasInput ? gap + calibrationHeight : 0);
            var panel = new Rect((1000 - width) / 2, (700 - total) / 2, width, panelHeight);
            RoomArt.Draw(panel, "panel/대기방");
            GUI.BeginGroup(panel);
            RoomArt.Draw(new Rect(pad, 12, inner, 40), "label/대기방", 36);
            RoomArt.Draw(new Rect(pad, 54, inner, 1), "list/구분선");
            RoomArt.Draw(new Rect(pad, 60, 88, 32), "label/참가자", 26);
            RoomLettering.Draw(new Rect(pad + 98, 63, 90, 30), members + "명", 22);
            string roleHint = members == 1 ? "혼자 시작하면 도망자로 참가합니다" : "역할 무작위 배정 · 술래 1명";
            RoomLettering.Draw(new Rect(pad, 98, inner - 152, 36), roleHint, 18);
            RoomLettering.Draw(new Rect(width - pad - 145, 98, 145, 36), $"준비 {ready}/{members}", 22, centered: true);

            var roster = new Rect(pad, 134, inner, rosterHeight);
            RoomArt.Draw(roster, "list/명단 배경");
            float listWidth = inner - (members > 5 ? 24 : 0);
            rosterScroll = GUI.BeginScrollView(roster, rosterScroll, new Rect(0, 0, listWidth, members * row));
            float y = 0;
            foreach (var player in NetworkPlayer.All)
            {
                if (string.IsNullOrEmpty(player.Nickname)) continue;
                var strip = new Rect(0, y + 2, listWidth, row - 4);
                RoomArt.Draw(strip, player.IsRoomHost ? "list/방장 행" : "list/참가자 행");
                var badge = new Rect(12, y, 60, row);
                if (player.IsRoomHost) RoomArt.Draw(badge, "label/방장", 20);
                else RoomLettering.Draw(badge, "#" + player.PlayerId, 17);
                RoomLettering.Draw(new Rect(84, y, listWidth - 210, row), player.Nickname, 19);
                if (player.LobbyReady)
                    RoomLettering.Draw(new Rect(listWidth - 104, y, 92, row), "준비 완료", 17,
                        new Color(.55f, .94f, .85f), centered: true);
                y += row;
            }
            GUI.EndScrollView();

            if (connector.IsHost)
            {
                float inviteY = roster.yMax + 10;
                bool useCode = connector.UsingRelay;
                string invite = useCode ? relay.CurrentLobbyCode : connector.InviteAddress;
                RoomArt.Draw(new Rect(pad, inviteY, 100, 34), useCode ? "label/방 코드" : "label/초대 IP", 24);
                var value = new Rect(pad + 112, inviteY, inner - 246, 34);
                RoomLettering.Draw(value, invite, useCode ? 26 : 22);
                if (RoomButton(new Rect(width - pad - 120, inviteY, 120, 34),
                    useCode ? "코드 복사" : "IP 복사", 20)) GUIUtility.systemCopyBuffer = invite;
            }
            var readyButton = new Rect(pad, actionsY, inner * .62f - 5, 42);
            GUI.enabled = local != null && local.IsClientInitialized && !string.IsNullOrEmpty(local.Nickname);
            bool localReady = local != null && local.LobbyReady;
            if (RoomButton(readyButton, localReady ? "준비 취소" : "준비", 24, primary: !localReady, useLettering: true))
                local.SetLobbyReadyServer(!localReady);
            if (countdown > 0)
                InterfaceArt.Draw(new Rect(readyButton.xMax - 57, readyButton.y + 5, 36, 32), "count" + countdown);
            GUI.enabled = true;
            if (RoomButton(new Rect(pad + inner * .62f + 5, actionsY, inner * .38f - 5, 42),
                "방 나가기", 22)) Leave("방에서 나왔습니다.");

            roomStatusScroll = GUI.BeginScrollView(new Rect(pad, actionsY + 56, inner, statusHeight),
                roomStatusScroll, new Rect(0, 0, inner - 24, messageHeight));
            var messageRect = new Rect(0, 0, inner - 24, messageHeight);
            if (statusText.text.IndexOf('\n') < 0 && caption.CalcSize(statusText).x <= messageRect.width) RoomLettering.Draw(messageRect, statusText.text, 17);
            else GUI.Label(messageRect, statusText, caption);
            GUI.EndScrollView();
            GUI.EndGroup();
            if (micCalibration.HasInput)
                micCalibration.Draw(new Rect(panel.x, panel.yMax + gap, width, calibrationHeight));
        }

        private void DrawEntryMenu(float scale, float offsetX)
        {
            bool canConnect = connector.Status != LanConnectionStatus.Connecting;
            GUI.enabled = canConnect && !showJoinInput;
            var input = new GUIStyle(GUI.skin.textField) { fontSize = 24, alignment = TextAnchor.MiddleLeft };
            // Keep the nickname field inside the reference canvas.
            float nicknameX = Mathf.Clamp(nicknamePosition.x, 20, 680);
            float nicknameY = Mathf.Clamp(nicknamePosition.y, 20, 590);
            var nicknameInput = new GUIStyle(GUIStyle.none)
            { font = input.font, fontSize = 24, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            foreach (var state in new[] { nicknameInput.normal, nicknameInput.hover, nicknameInput.focused, nicknameInput.active })
            { state.background = null; state.textColor = new Color(.9f, .9f, .88f); }
            var nicknameRect = new Rect(nicknameX, nicknameY, 300, 40);
            GUI.SetNextControlName("LobbyNickname");
            Color previousCursorColor = GUI.skin.settings.cursorColor;
            GUI.skin.settings.cursorColor = Color.white;
            playerName = GUI.TextField(nicknameRect, playerName, 20, nicknameInput);
            GUI.skin.settings.cursorColor = previousCursorColor;
            if (string.IsNullOrEmpty(playerName) && GUI.GetNameOfFocusedControl() != "LobbyNickname")
                RuinedUi.Label(nicknameRect, "닉네임", 20, light: true);
            // Menu X is a real screen-edge margin, not a centered-canvas coordinate.
            float menuX = (Mathf.Clamp(menuPosition.x, 0, 100) - offsetX) / Mathf.Max(0.001f, scale);
            float menuY = Mathf.Clamp(menuPosition.y, 20, 380);
            GUI.enabled = canConnect && !showJoinInput && relay.CanCreate;
            if (RuinedUi.Button(new Rect(menuX, menuY, 330, 65), "방 만들기", menuFontSize, light: true)) CreateRelayRoom();
            GUI.enabled = canConnect && !showJoinInput;
            if (RuinedUi.Button(new Rect(menuX, menuY + 80, 330, 65), "방 찾기", menuFontSize, light: true))
            {
                showJoinInput = true;
                relay.RefreshRooms();
            }
            if (RuinedUi.Button(new Rect(menuX, menuY + 160, 330, 65), "설정", menuFontSize, light: true)) Marco.Network.Settings.SettingsMenu.OpenSettingsFromMenu();
            if (showJoinInput) DrawRoomBrowser(scale, offsetX, canConnect);
            if (connector.Status == LanConnectionStatus.Connecting) status = "방에 연결 중…";
            GUI.enabled = true;
            var message = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            string displayedStatus = string.IsNullOrEmpty(status) ? relay.Status : status;
            if (!string.IsNullOrEmpty(displayedStatus)) GUI.Label(new Rect(menuX, 650, 920, 45), displayedStatus, message);
        }

        private float firstVisibleRoom;

        private void DrawRoomBrowser(float scale, float offsetX, bool canConnect)
        {
            GUI.enabled = true;
            DrawDim(scale, offsetX, .7f);
            var panel = new Rect(170, 38, 660, 600);
            GUI.DrawTexture(panel, RuinedUi.Texture(RuinedUi.PaperAsset));
            // Match the printed template. Paging in whole rows keeps buttons on the five printed lines.
            Rect Map(float x, float y, float w, float h) => new Rect(panel.x + x * panel.width / 1315f,
                panel.y + y * panel.height / 1197f, w * panel.width / 1315f, h * panel.height / 1197f);
            RuinedUi.Label(Map(350, 145, 600, 80), "방 찾기", 34);
            RuinedUi.Label(Map(155, 240, 230, 38), "방 코드", 18);
            var input = new GUIStyle(GUI.skin.textField) { fontSize = 22, alignment = TextAnchor.MiddleLeft };
            foreach (var state in new[] { input.normal, input.focused, input.hover, input.active })
            { state.background = null; state.textColor = RuinedUi.Ink; }
            GUI.enabled = canConnect;
            relayJoinInput = GUI.TextField(Map(160, 290, 740, 58), relayJoinInput, 8, input);
            if (RuinedUi.Button(Map(940, 284, 224, 68), "코드 참가", 20)) JoinRelayRoom();
            GUI.enabled = true;
            RuinedUi.Label(Map(160, 393, 760, 54), "공개 방 목록", 21);
            GUI.enabled = !relay.IsRefreshingRooms;
            if (RuinedUi.Button(Map(975, 394, 190, 53), relay.IsRefreshingRooms ? "불러오는 중…" : "새로고침", 18))
                relay.RefreshRooms();
            GUI.enabled = true;
            int count = relay.AvailableRooms.Count;
            int maxFirst = Mathf.Max(0, count - 5);
            var listRect = Map(168, 488, 947, 470);
            if (Event.current.type == EventType.ScrollWheel && listRect.Contains(Event.current.mousePosition))
            {
                firstVisibleRoom += Mathf.Sign(Event.current.delta.y);
                Event.current.Use();
            }
            firstVisibleRoom = Mathf.Clamp(firstVisibleRoom, 0, maxFirst);
            if (count > 5)
                firstVisibleRoom = GUI.VerticalScrollbar(Map(1135, 488, 24, 470), firstVisibleRoom, 5, 0, count);
            int first = Mathf.Clamp(Mathf.RoundToInt(firstVisibleRoom), 0, maxFirst);
            var roomText = RuinedUi.InkText(17);
            roomText.clipping = TextClipping.Clip;
            for (int row = 0; row < 5 && first + row < count; row++)
            {
                var room = relay.AvailableRooms[first + row];
                float y = 489 + row * 94;
                GUI.Label(Map(185, y + 12, 740, 65), room.Name + "  ·  " + room.Players.Count + "/" + room.MaxPlayers + "명", roomText);
                GUI.enabled = canConnect;
                if (RuinedUi.Button(Map(950, y + 19, 145, 54), "참가", 20)) relay.JoinRoomById(room.Id);
                GUI.enabled = true;
            }
            if (count == 0)
                GUI.Label(Map(185, 500, 720, 155), relay.RoomListStatus, RuinedUi.InkText(17, true));
            if (RuinedUi.Button(Map(913, 1019, 250, 69), "취소", 22)) showJoinInput = false;
            GUI.Label(Map(160, 1000, 710, 110), relay.Status, RuinedUi.InkText(14, true));
        }

        private void JoinRelayRoom()
        {
            playerName = NetworkPlayer.CleanNickname(playerName);
            relay.JoinRoomCode(relayJoinInput);
        }

        private void CreateRelayRoom()
        {
            playerName = NetworkPlayer.CleanNickname(playerName);
            status = string.Empty;
            relay.CreateLobby(playerName);
        }

        private static void DrawDim(float scale, float offsetX, float alpha)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(0, 0, 0, alpha);
            GUI.DrawTexture(new Rect(-offsetX / Mathf.Max(0.001f, scale), 0,
                Screen.width / Mathf.Max(0.001f, scale), 700), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static GUIStyle TextButtonStyle(int fontSize)
        {
            // Start without a skin: Unity's button skin may supply fallback backgrounds.
            var style = new GUIStyle
            {
                font = GUI.skin.font,
                fontSize = fontSize, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0)
            };
            foreach (var state in new[] { style.normal, style.hover, style.active, style.focused,
                style.onNormal, style.onHover, style.onActive, style.onFocused })
            {
                state.background = null;
                state.textColor = Color.white;
            }
            style.hover.textColor = new Color(0.65f, 0.85f, 1f);
            style.active.textColor = Color.gray;
            return style;
        }
    }
}
