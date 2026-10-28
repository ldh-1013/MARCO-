using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Lobby
{
    // The prototype lobby (RoomSessionPrototype's OnGUI) on top of the FishNet session: nickname,
    // Create room (Unity Relay), Join (room code or public room list),
    // and the host's 게임 시작, which starts the round. Until then it covers the whole game screen.
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
        private string playerName = "Player";
        private string status = "";
        private Vector2 rosterScroll;
        private bool showJoinInput;
        private LanConnectionStatus lastStatus;
        private bool leavingOnPurpose;
        private NetworkPlayer nicknameSentFor;
        private bool wasOpen = true;
        private string relayJoinInput = "";

        private bool Joined => connector.Status == LanConnectionStatus.Connected;
        private static bool GameStarted => NetworkRound.Instance != null && NetworkRound.Instance.Mode != RoundMode.NotStarted;
        // Lobby screens are up whenever the round has not started for this player.
        public bool IsOpen => !Joined || !GameStarted;

        private void Awake()
        {
            connector = GetComponent<LanConnector>();
            relay = GetComponent<UnityRelayLobby>();
        }

        private void Update()
        {
            if (IsOpen && Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            // The host pressed 게임 시작: hand the cursor straight to the game.
            if (wasOpen && !IsOpen) Cursor.lockState = CursorLockMode.Locked;
            wasOpen = IsOpen;
            TrackConnection();

            // Tell the server our nickname once our player exists (the prototype's NAME message).
            var local = NetworkPlayer.Local;
            if (local != null && local != nicknameSentFor)
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

        private bool CanStartGame
        {
            get
            {
                if (!Joined || !connector.IsHost || GameStarted || NetworkRound.Instance == null) return false;
                foreach (var player in NetworkPlayer.All) if (string.IsNullOrEmpty(player.Nickname)) return false;
                return true;
            }
        }

        private void StartGame()
        {
            if (!CanStartGame) return;
            relay.LockLobbyForGame();
            NetworkRound.Instance.StartRound();
        }

        private void OnGUI()
        {
            if (!IsOpen)
            {
                DrawInGameMenu();
                return;
            }

            // Opaque backdrop over the game view; as a HUD panel it also keeps clicks from relocking the cursor.
            TestHudSkin.Begin();
            var full = new Rect(0, 0, TestHudSkin.Width, TestHudSkin.Height);
            TestHudSkin.Panel(full);
            TestHudSkin.Fill(full, Color.black);

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
            TestHudSkin.Begin();
            var rect = new Rect(TestHudSkin.Width - 300, TestHudSkin.Height - 170, 276, 146);
            TestHudSkin.Panel(rect);
            GUI.depth = -10;
            if (GUI.Button(new Rect(rect.x + 16, rect.y + 16, rect.width - 32, 52), "계속하기", TestHudSkin.Button(22)))
                Cursor.lockState = CursorLockMode.Locked;
            if (GUI.Button(new Rect(rect.x + 16, rect.y + 78, rect.width - 32, 52), "게임 나가기 · 로비로", TestHudSkin.Button(22)))
                Leave("게임에서 나왔습니다.");
        }

        private void DrawRoom()
        {
            var panel = new Rect(200, 130, 600, 440);
            GUILayout.BeginArea(panel);
            GUILayout.Space(15);
            int members = 0;
            foreach (var player in NetworkPlayer.All) if (!string.IsNullOrEmpty(player.Nickname)) members++;
            GUILayout.Label("참가자 " + members + "명");
            GUILayout.Label("역할 무작위 배정 · 헌터 1명 (각자 " + (100f / Mathf.Max(1, members)).ToString("0.#") + "% 확률), 나머지는 서바이버");
            rosterScroll = GUILayout.BeginScrollView(rosterScroll, GUILayout.Height(210));
            foreach (var player in NetworkPlayer.All)
            {
                if (string.IsNullOrEmpty(player.Nickname)) continue;
                string label = (player.IsRoomHost ? "[방장] " : "[" + player.PlayerId + "] ") + player.Nickname;
                GUILayout.Label(label, GUI.skin.box, GUILayout.Height(34));
            }
            GUILayout.EndScrollView();
            GUI.enabled = CanStartGame;
            if (GUILayout.Button(connector.IsHost ? "게임 시작" : "방장의 시작을 기다리는 중", GUILayout.Height(40))) StartGame();
            GUI.enabled = true;
            if (connector.IsHost)
            {
                GUILayout.Space(12);
                if (connector.UsingRelay)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("방 코드 " + relay.CurrentLobbyCode, GUILayout.Width(220));
                    if (GUILayout.Button("코드 복사", GUILayout.Width(100)))
                        GUIUtility.systemCopyBuffer = relay.CurrentLobbyCode;
                    GUILayout.EndHorizontal();
                }
                else
                {
                    string address = connector.InviteAddress;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("초대 IP", GUILayout.Width(60)); GUILayout.TextField(address);
                    if (GUILayout.Button("IP 복사", GUILayout.Width(85))) GUIUtility.systemCopyBuffer = address;
                    GUILayout.EndHorizontal();
                }
            }
            if (GUILayout.Button("방 나가기")) Leave("방에서 나왔습니다.");
            GUILayout.Space(15); GUILayout.Label(connector.UsingRelay ? relay.Status : status);
            GUILayout.EndArea();
        }

        private void DrawEntryMenu(float scale, float offsetX)
        {
            bool canConnect = connector.Status != LanConnectionStatus.Connecting;
            GUI.enabled = canConnect && !showJoinInput;
            var label = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            var input = new GUIStyle(GUI.skin.textField) { fontSize = 24, alignment = TextAnchor.MiddleLeft };
            // Keep the nickname field inside the reference canvas.
            float nicknameX = Mathf.Clamp(nicknamePosition.x, 20, 680);
            float nicknameY = Mathf.Clamp(nicknamePosition.y, 20, 590);
            var nicknameLabel = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 };
            var nicknameInput = new GUIStyle(input) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(nicknameX, nicknameY, 300, 35), "닉네임", nicknameLabel);
            playerName = GUI.TextField(new Rect(nicknameX, nicknameY + 42, 300, 48), playerName, 20, nicknameInput);
            // Menu X is a real screen-edge margin, not a centered-canvas coordinate.
            float menuX = (Mathf.Clamp(menuPosition.x, 0, 100) - offsetX) / Mathf.Max(0.001f, scale);
            float menuY = Mathf.Clamp(menuPosition.y, 20, 380);
            var menu = TextButtonStyle(menuFontSize);
            GUI.enabled = canConnect && !showJoinInput && relay.CanCreate;
            if (GUI.Button(new Rect(menuX, menuY, 330, 65), "방 만들기", menu)) CreateRelayRoom();
            GUI.enabled = canConnect && !showJoinInput;
            if (GUI.Button(new Rect(menuX, menuY + 80, 330, 65), "Join", menu))
            {
                showJoinInput = true;
                relay.RefreshRooms();
            }
            if (showJoinInput)
            {
                GUI.enabled = true;
                DrawDim(scale, offsetX, 0.7f);
                GUI.Box(new Rect(200, 76, 600, 560), GUIContent.none);
                GUI.Label(new Rect(235, 96, 530, 36), "방 코드 또는 공개 방 목록", label);
                GUI.Label(new Rect(235, 138, 120, 34), "방 코드", label);
                GUI.enabled = canConnect;
                relayJoinInput = GUI.TextField(new Rect(350, 138, 270, 40), relayJoinInput, 8, input);
                var action = TextButtonStyle(26);
                if (GUI.Button(new Rect(632, 138, 130, 40), "코드 참가", action)) JoinRelayRoom();
                GUI.enabled = true;
                GUI.Label(new Rect(235, 188, 350, 30), relay.RoomListStatus, messageStyle());
                if (GUI.Button(new Rect(620, 184, 142, 34), relay.IsRefreshingRooms ? "불러오는 중…" : "새로고침", action))
                    relay.RefreshRooms();
                roomListScroll = GUI.BeginScrollView(new Rect(235, 224, 527, 320), roomListScroll,
                    new Rect(0, 0, 500, Mathf.Max(320, relay.AvailableRooms.Count * 52)));
                for (int i = 0; i < relay.AvailableRooms.Count; i++)
                {
                    var room = relay.AvailableRooms[i];
                    var row = new Rect(0, i * 52, 500, 46);
                    GUI.Box(row, GUIContent.none);
                    GUI.Label(new Rect(10, row.y + 4, 325, 38),
                        room.Name + "  ·  " + room.Players.Count + "/" + room.MaxPlayers + "명", label);
                    if (GUI.Button(new Rect(350, row.y + 5, 140, 36), "참가", action))
                        relay.JoinRoomById(room.Id);
                }
                GUI.EndScrollView();
                if (GUI.Button(new Rect(582, 566, 180, 42), "취소", action)) showJoinInput = false;
                GUI.Label(new Rect(235, 614, 527, 30), relay.Status, messageStyle());
            }
            if (connector.Status == LanConnectionStatus.Connecting) status = "방에 연결 중…";
            GUI.enabled = true;
            var message = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            string displayedStatus = string.IsNullOrEmpty(status) ? relay.Status : status;
            if (!string.IsNullOrEmpty(displayedStatus)) GUI.Label(new Rect(menuX, 650, 920, 45), displayedStatus, message);
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

        private Vector2 roomListScroll;

        private static GUIStyle messageStyle() => new(GUI.skin.label) { fontSize = 15, wordWrap = true };

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
