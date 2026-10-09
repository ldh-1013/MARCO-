using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Prototype
{
    public sealed class RoomSessionPrototype : MonoBehaviour
    {
        [SerializeField] private int port = 7777;
        [Header("Registered character names (all participants use the host's selection)")]
        [SerializeField] private string hunterAssetName = "Demon";
        [SerializeField] private string survivorAssetName = "UnityChan";
        [Header("Lobby layout (1000 x 700 reference canvas)")]
        [SerializeField] private Vector2 menuPosition = new Vector2(8, 330);
        [SerializeField] private Vector2 nicknamePosition = new Vector2(350, 140);
        [SerializeField, Range(24, 54)] private int menuFontSize = 40;
        private string address = "";
        private string joinInput = "";
        private string status = "";
        private string advertisedIp = "";
        private TcpListener listener;
        private Task<TcpClient> acceptTask;
        private Task<TcpClient> connectTask;
        private readonly List<Peer> peers = new List<Peer>();
        private Peer server;
        private bool joined;
        private int members;
        private float deadline;
        private string playerName = "Player";
        private readonly List<string> roster = new List<string>();
        private Vector2 rosterScroll;
        private int nextId = 1;
        private bool showJoinInput;
        private bool showSettings;
        private static RoomSessionPrototype instance;
        private bool gameStarted;
        private readonly Dictionary<int, PlayerPoseState> latestPoses = new Dictionary<int, PlayerPoseState>();
        private readonly HashSet<int> activePlayers = new HashSet<int>();
        public static RoomSessionPrototype Instance => instance;
        public event Action<PlayerPoseState> PoseReceived;
        public event Action<int> PlayerLeft;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public event Action DevelopmentRolesChanged;
        private readonly Dictionary<int, float> developmentRoleSwitchAt = new Dictionary<int, float>();
        private float developmentRequestAt;
        public bool CanDevelopmentSwitchRole => joined && gameStarted && MatchLaunchContext.IsActive
            && SceneManager.GetActiveScene().name == MatchLaunchContext.GameScene;
#endif
        public bool TryGetPose(int id, out PlayerPoseState pose) => latestPoses.TryGetValue(id, out pose);
        public bool IsHost => listener != null;
        public bool GameStarted => gameStarted;
        public int MemberCount => members;
        public bool CanStartGame => joined && IsHost && !gameStarted &&
            peers.TrueForAll(p => p.Name != null);

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            MatchLaunchContext.Clear();
            DontDestroyOnLoad(gameObject);
        }

        private sealed class Peer : IDisposable
        {
            public readonly TcpClient Client;
            public readonly StreamReader Reader;
            public readonly StreamWriter Writer;
            public Task<string> Read;
            public string Name;
            public int Id;
            public float Deadline;
            private readonly Queue<string> outgoing = new Queue<string>();
            private Task writing;
            public Peer(TcpClient client)
            {
                Client = client; client.NoDelay = true;
                Reader = new StreamReader(client.GetStream()); Writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                Read = Reader.ReadLineAsync();
            }
            public void Dispose() { Client.Close(); }
            public void Send(string message)
            {
                if (outgoing.Count >= 128) throw new IOException("Peer outgoing queue overflow");
                outgoing.Enqueue(message);
            }
            public void PumpWrites()
            {
                if (writing != null && !writing.IsCompleted) return;
                if (writing != null) { writing.GetAwaiter().GetResult(); writing = null; }
                if (outgoing.Count > 0) writing = Writer.WriteLineAsync(outgoing.Dequeue());
            }
        }

        private void Start()
        {
            advertisedIp = "127.0.0.1";
            foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip)) { advertisedIp = ip.ToString(); break; }
        }

        public static string EncodeAddress(string endpoint) => "MARCO-" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(endpoint)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        public static bool TryParseAddress(string input, out IPAddress ip, out int targetPort)
        {
            ip = null; targetPort = 7777;
            try
            {
                input = input.Trim();
                if (input.StartsWith("MARCO-", StringComparison.Ordinal))
                {
                    string encoded = input.Substring(6).Replace('-', '+').Replace('_', '/');
                    encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
                    input = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                }
                string[] parts = input.Split(':');
                if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
                return parts.Length == 1 || (int.TryParse(parts[1], out targetPort) && targetPort > 0 && targetPort <= 65535);
            }
            catch { return false; }
        }

        private void CreateRoom()
        {
            try
            {
                if (!IPAddress.TryParse(advertisedIp, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) { status = "공유할 IPv4 주소를 확인하세요."; return; }
                listener = new TcpListener(IPAddress.Any, port); listener.Start(); acceptTask = listener.AcceptTcpClientAsync();
                playerName = CleanName(playerName);
                address = advertisedIp + ":" + port; joined = true; members = 1; status = "친구를 초대하고 참가자를 기다리세요.";
                roster.Clear(); roster.Add("[방장] " + playerName);
                Debug.Log("[Room] Host created; port=" + port);
            }
            catch (Exception e) { Leave(); status = "방 생성 실패: " + e.Message; }
        }

        private static async Task<TcpClient> Connect(IPAddress ip, int targetPort)
        {
            var client = new TcpClient();
            try { await client.ConnectAsync(ip, targetPort); return client; }
            catch { client.Close(); throw; }
        }

        private void JoinRoom()
        {
            if (!TryParseAddress(joinInput, out var ip, out int targetPort)) { status = "올바른 IP:port 또는 MARCO 코드를 입력하세요."; return; }
            connectTask = Connect(ip, targetPort); deadline = Time.unscaledTime + 8; status = "방에 연결 중…";
        }

        private void Update()
        {
            try
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (Input.GetKeyDown(KeyCode.F10)) RequestDevelopmentRoleSwitch();
#endif
                if (acceptTask != null && acceptTask.IsCompleted)
                {
                    var client = acceptTask.GetAwaiter().GetResult();
                    if (gameStarted) client.Close();
                    else if (peers.Count < 15) peers.Add(new Peer(client) { Id = nextId++, Deadline = Time.unscaledTime + 8 });
                    else client.Close();
                    acceptTask = listener.AcceptTcpClientAsync();
                }
                if (connectTask != null && connectTask.IsCompleted)
                {
                    server = new Peer(connectTask.GetAwaiter().GetResult()); connectTask = null;
                    playerName = CleanName(playerName);
                    server.Send("NAME " + EncodeName(playerName));
                    deadline = Time.unscaledTime + 8;
                }
                if (!joined && (connectTask != null || server != null) && Time.unscaledTime > deadline) { Leave(); status = "접속 시간 초과. 주소와 연결 상태를 확인하세요."; }
                for (int i = peers.Count - 1; i >= 0; i--)
                {
                    var peer = peers[i];
                    try { peer.PumpWrites(); }
                    catch (Exception) { DropPeer(i); continue; }
                    if (!peer.Read.IsCompleted)
                    {
                        if (peer.Name == null && Time.unscaledTime > peer.Deadline) DropPeer(i);
                        continue;
                    }
                    string incoming = null;
                    try { incoming = peer.Read.GetAwaiter().GetResult(); } catch (IOException) { } catch (SocketException) { }
                    if (peer.Name == null && incoming != null && incoming.StartsWith("NAME "))
                    {
                        try { peer.Name = CleanName(DecodeName(incoming.Substring(5))); }
                        catch (FormatException) { DropPeer(i); continue; }
                        peer.Read = peer.Reader.ReadLineAsync(); BroadcastMembers();
                    }
                    else if (gameStarted && peer.Name != null && incoming != null)
                    {
                        // Identity comes from the accepted socket, never from an untrusted payload.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        if (incoming == RoomLaunchProtocol.DevelopmentRoleRequest) SwitchDevelopmentRole(peer.Id);
                        else
#endif
                        if (PlayerPoseProtocol.TryParse(incoming, out var pose) && pose.PlayerId == peer.Id && AcceptPose(pose))
                            foreach (var other in peers) if (other != peer && other.Name != null) other.Send(PlayerPoseProtocol.Encode(pose));
                        peer.Read = peer.Reader.ReadLineAsync();
                    }
                    else DropPeer(i);
                }
                server?.PumpWrites();
                if (server != null && server.Read.IsCompleted)
                {
                    string line = server.Read.GetAwaiter().GetResult();
                    if (line == null) { ReturnToLobby("호스트와 연결이 종료되었습니다."); return; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (RoomLaunchProtocol.TryParseDevelopmentRoles(line, out int changedLocalId, out var changedMembers))
                    {
                        ApplyDevelopmentRoles(changedLocalId, changedMembers);
                        server.Read = server.Reader.ReadLineAsync(); return;
                    }
#endif
                    if (RoomLaunchProtocol.TryParseMatch(line, out int localId, out var matchMembers))
                    {
                        server.Read = server.Reader.ReadLineAsync();
                        BeginGame(localId, matchMembers); return;
                    }
                    if (RoomLaunchProtocol.TryParseStart(line, out var role))
                    {
                        server.Read = server.Reader.ReadLineAsync();
                        BeginGame(role);
                        return;
                    }
                    if (gameStarted)
                    {
                        if (PlayerPoseProtocol.TryParse(line, out var pose) && pose.PlayerId != MatchLaunchContext.LocalPlayerId) AcceptPose(pose);
                        else if (line.StartsWith("MARCO/3 LEFT ") && int.TryParse(line.Substring(13), out int departed))
                        {
                            if (departed != MatchLaunchContext.LocalPlayerId && activePlayers.Remove(departed))
                            { latestPoses.Remove(departed); PlayerLeft?.Invoke(departed); }
                        }
                        server.Read = server.Reader.ReadLineAsync(); return;
                    }
                    if (!line.StartsWith("MARCO/2 ROSTER "))
                    { ReturnToLobby("호환되지 않는 방 응답입니다."); return; }
                    roster.Clear();
                    foreach (string entry in line.Substring(15).Split('|'))
                    {
                        int separator = entry.IndexOf(':');
                        if (separator < 1) throw new FormatException("Invalid roster");
                        string id = entry.Substring(0, separator);
                        roster.Add((id == "0" ? "[방장] " : "[" + id + "] ") + CleanName(DecodeName(entry.Substring(separator + 1))));
                    }
                    members = roster.Count;
                    joined = true; status = "방 입장 완료"; server.Read = server.Reader.ReadLineAsync();
                    Debug.Log("[Room] Joined; members=" + members);
                }
            }
            catch (Exception e) { ReturnToLobby("연결 실패: " + e.GetBaseException().Message); Debug.LogWarning("[Room] " + status); }
        }

        public void StartGame()
        {
            if (!CanStartGame) return;
            if (!Application.CanStreamedLevelBeLoaded(MatchLaunchContext.GameScene))
            { status = "통합 게임 씬이 빌드 씬 목록에 없습니다."; return; }
            try
            {
                var ids = new List<int> { 0 };
                foreach (var peer in peers) if (peer.Name != null) ids.Add(peer.Id);
                int hunterId = PickRandomHunter(ids);
                if (CharacterAssetDefinition.Find(hunterAssetName) == null || CharacterAssetDefinition.Find(survivorAssetName) == null)
                { status = "캐릭터 에셋 이름을 등록 목록에서 확인하세요."; return; }
                var matchMembers = new List<MatchMember>();
                foreach (int id in ids)
                {
                    var role = RoomLaunchProtocol.RoleFor(id, hunterId);
                    matchMembers.Add(new MatchMember(id, role, role == PrototypePlayerRole.Hunter ? hunterAssetName : survivorAssetName));
                }
                var assignment = matchMembers.ToArray();
                foreach (var peer in peers)
                    if (peer.Name != null)
                        peer.Send(RoomLaunchProtocol.StartCommand(peer.Id, assignment));
                BeginGame(0, assignment);
            }
            catch (Exception e) { ReturnToLobby("게임 시작 실패: " + e.GetBaseException().Message); }
        }

        public static int PickRandomHunter(IList<int> playerIds)
        {
            if (playerIds == null || playerIds.Count == 0) throw new ArgumentException("At least one real participant is required.", nameof(playerIds));
            // Uniform participant index, not a fixed host role or a coin toss per player.
            return playerIds[UnityEngine.Random.Range(0, playerIds.Count)];
        }

        private void BeginGame(PrototypePlayerRole role)
        {
            if (gameStarted) return;
            MatchLaunchContext.Begin(role);
            BeginGame(MatchLaunchContext.LocalPlayerId, MatchLaunchContext.Members);
        }
        private void BeginGame(int localId, MatchMember[] matchMembers)
        {
            if (gameStarted) return;
            if (!Application.CanStreamedLevelBeLoaded(MatchLaunchContext.GameScene))
            { ReturnToLobby("통합 게임 씬이 빌드 씬 목록에 없습니다."); return; }
            foreach (var member in matchMembers)
                if (CharacterAssetDefinition.Find(member.AssetName) == null)
                { ReturnToLobby("호스트가 선택한 캐릭터 에셋이 이 클라이언트에 없습니다: " + member.AssetName); return; }
            gameStarted = true;
            MatchLaunchContext.Begin(localId, matchMembers);
            activePlayers.Clear(); foreach (var member in matchMembers) activePlayers.Add(member.Id);
            members = matchMembers.Length;
            status = "게임 씬으로 이동 중…";
            // Keep sockets and roster alive while the lobby scene unloads.
            SceneManager.LoadSceneAsync(MatchLaunchContext.GameScene, LoadSceneMode.Single);
            Debug.Log("[Room] Starting shared scene; role=" + MatchLaunchContext.LocalRole + "; id=" + localId);
        }

        private bool AcceptPose(PlayerPoseState pose)
        {
            if (!activePlayers.Contains(pose.PlayerId) || (latestPoses.TryGetValue(pose.PlayerId, out var prior) &&
                (pose.Sequence <= prior.Sequence || pose.Attack < prior.Attack || pose.Interaction < prior.Interaction))) return false;
            latestPoses[pose.PlayerId] = pose; PoseReceived?.Invoke(pose); return true;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool RequestDevelopmentRoleSwitch()
        {
            if (!CanDevelopmentSwitchRole || Time.unscaledTime < developmentRequestAt) return false;
            developmentRequestAt = Time.unscaledTime + .5f;
            try
            {
                if (IsHost) return SwitchDevelopmentRole(MatchLaunchContext.LocalPlayerId);
                server?.Send(RoomLaunchProtocol.DevelopmentRoleRequest);
                return server != null;
            }
            catch (Exception e) { ReturnToLobby("개발 역할 전환 실패: " + e.GetBaseException().Message); return false; }
        }

        private bool SwitchDevelopmentRole(int playerId)
        {
            if (!IsHost || !CanDevelopmentSwitchRole || !activePlayers.Contains(playerId)
                || (developmentRoleSwitchAt.TryGetValue(playerId, out float next) && Time.unscaledTime < next)) return false;
            var current = Array.Find(MatchLaunchContext.Members, member => member.Id == playerId);
            var ids = new List<int>();
            foreach (var member in MatchLaunchContext.Members) if (activePlayers.Contains(member.Id)) ids.Add(member.Id);
            int hunterId = playerId;
            if (current.Role == PrototypePlayerRole.Hunter)
            {
                ids.Remove(playerId);
                // Solo development mode may have a survivor and no hunter.
                hunterId = ids.Count == 0 ? -1 : PickRandomHunter(ids);
            }
            if (CharacterAssetDefinition.Find(hunterAssetName) == null || CharacterAssetDefinition.Find(survivorAssetName) == null) return false;
            var changed = new List<MatchMember>();
            foreach (var member in MatchLaunchContext.Members)
            {
                if (!activePlayers.Contains(member.Id)) continue;
                var role = RoomLaunchProtocol.RoleFor(member.Id, hunterId);
                // Preserve custom assets on participants whose role didn't change.
                string assetName = role == member.Role ? member.AssetName
                    : role == PrototypePlayerRole.Hunter ? hunterAssetName : survivorAssetName;
                changed.Add(new MatchMember(member.Id, role, assetName));
            }
            var assignment = changed.ToArray();
            developmentRoleSwitchAt[playerId] = Time.unscaledTime + .5f;
            foreach (var peer in peers) if (peer.Name != null)
                peer.Send(RoomLaunchProtocol.DevelopmentRolesCommand(peer.Id, assignment));
            return ApplyDevelopmentRoles(MatchLaunchContext.LocalPlayerId, assignment);
        }

        private bool ApplyDevelopmentRoles(int localId, MatchMember[] assignment)
        {
            if (!CanDevelopmentSwitchRole || localId != MatchLaunchContext.LocalPlayerId || assignment.Length != activePlayers.Count) return false;
            foreach (var member in assignment)
                if (!activePlayers.Contains(member.Id) || CharacterAssetDefinition.Find(member.AssetName) == null) return false;
            // The parser has already checked unique IDs and the one-hunter invariant.
            MatchLaunchContext.Begin(localId, assignment);
            DevelopmentRolesChanged?.Invoke();
            Debug.Log("[DEV] Role switched; player=" + localId + "; role=" + MatchLaunchContext.LocalRole);
            return true;
        }
#endif
        public void PublishPose(PlayerPoseState pose)
        {
            if (!gameStarted || pose.PlayerId != MatchLaunchContext.LocalPlayerId) return;
            string message = PlayerPoseProtocol.Encode(pose);
            if (!PlayerPoseProtocol.TryParse(message, out _)) return;
            try
            {
                if (IsHost)
                { foreach (var peer in peers) if (peer.Name != null) peer.Send(message); }
                else server?.Send(message);
            }
            catch (Exception e) { ReturnToLobby("동작 전송 실패: " + e.GetBaseException().Message); }
        }
        private void DropPeer(int index)
        {
            var peer = peers[index]; peer.Dispose(); peers.RemoveAt(index);
            if (!gameStarted) { BroadcastMembers(); return; }
            activePlayers.Remove(peer.Id); latestPoses.Remove(peer.Id); PlayerLeft?.Invoke(peer.Id); members = 1 + peers.Count;
            foreach (var other in peers) if (other.Name != null) other.Send("MARCO/3 LEFT " + peer.Id);
        }

        public void ReturnToLobby(string message = "게임에서 나왔습니다.")
        {
            bool loadLobby = gameStarted;
            Leave();
            status = message;
            Cursor.lockState = CursorLockMode.None;
            if (loadLobby) SceneManager.LoadSceneAsync(MatchLaunchContext.LobbyScene, LoadSceneMode.Single);
        }

        private void BroadcastMembers()
        {
            roster.Clear(); roster.Add("[방장] " + playerName);
            var entries = new List<string> { "0:" + EncodeName(playerName) };
            foreach (var peer in peers)
            {
                if (peer.Name == null) continue;
                entries.Add(peer.Id + ":" + EncodeName(peer.Name)); roster.Add("[" + peer.Id + "] " + peer.Name);
            }
            members = roster.Count;
            foreach (var peer in peers) if (peer.Name != null) peer.Send("MARCO/2 ROSTER " + string.Join("|", entries));
        }

        private static string EncodeName(string name) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(name));
        private static string DecodeName(string name) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(name));
        private static string CleanName(string name)
        {
            var cleaned = new System.Text.StringBuilder();
            foreach (char c in name ?? "") if (!char.IsControl(c) && c != '<' && c != '>') cleaned.Append(c);
            string result = cleaned.ToString().Trim();
            if (result.Length == 0) return "Player";
            return result.Substring(0, Math.Min(20, result.Length));
        }

        private void Leave()
        {
            if (acceptTask != null)
            {
                var pendingAccept = acceptTask;
                pendingAccept.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Close(); else { var ignored = t.Exception; } });
            }
            listener?.Stop(); listener = null; acceptTask = null;
            foreach (var peer in peers) peer.Dispose(); peers.Clear(); server?.Dispose(); server = null;
            if (connectTask != null)
            {
                var pending = connectTask;
                pending.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Close(); else { var ignored = t.Exception; } });
            }
            connectTask = null; joined = false; members = 0; address = "";
            showJoinInput = false;
            showSettings = false;
            roster.Clear();
            nextId = 1;
            gameStarted = false;
            latestPoses.Clear();
            activePlayers.Clear();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            developmentRoleSwitchAt.Clear(); developmentRequestAt = 0;
#endif
            MatchLaunchContext.Clear();
        }
        private void OnDestroy()
        {
            if (instance != this) return;
            Leave(); instance = null;
        }

        private void OnGUI()
        {
            if (gameStarted)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                GUI.depth = -10;
                GUI.Label(new Rect(35, 155, 850, 28), "[DEV] F10 헌터 ↔ 서바이버 · 멀티에서는 역할 교환");
#endif
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    GUI.depth = -10;
                    if (GUI.Button(new Rect(Screen.width - 230, 15, 200, 40), "계속하기")) Cursor.lockState = CursorLockMode.Locked;
                    if (GUI.Button(new Rect(Screen.width - 230, 65, 200, 40), "게임 나가기 · 로비로")) ReturnToLobby();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (GUI.Button(new Rect(Screen.width - 230, 115, 200, 40), "[DEV] 역할 전환 (F10)")) RequestDevelopmentRoleSwitch();
#endif
                }
                return;
            }
            GUI.depth = 0;
            Matrix4x4 previousMatrix = GUI.matrix;
            bool previousEnabled = GUI.enabled;
            float scale = Mathf.Min(Screen.width / 1000f, Screen.height / 700f);
            float offsetX = (Screen.width - 1000 * scale) * 0.5f;
            float offsetY = (Screen.height - 700 * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(offsetX, offsetY, 0), Quaternion.identity, new Vector3(scale, scale, 1));
            if (!joined)
            {
                DrawEntryMenu(scale, offsetX);
                GUI.matrix = previousMatrix;
                GUI.enabled = previousEnabled;
                return;
            }
            var panel = new Rect(200, 130, 600, 440);
            GUILayout.BeginArea(panel);
            GUILayout.Space(15);
            if (joined)
            {
                GUILayout.Label("참가자 " + members + "명");
                GUILayout.Label("역할 무작위 배정 · 헌터 1명 (각자 " + (100f / Mathf.Max(1, members)).ToString("0.#") + "% 확률), 나머지는 서바이버");
                rosterScroll = GUILayout.BeginScrollView(rosterScroll, GUILayout.Height(210));
                foreach (string name in roster) GUILayout.Label(name, GUI.skin.box, GUILayout.Height(34));
                GUILayout.EndScrollView();
                GUI.enabled = CanStartGame;
                if (GUILayout.Button(IsHost ? "게임 시작" : "방장의 시작을 기다리는 중", GUILayout.Height(40))) StartGame();
                GUI.enabled = true;
                if (listener != null)
                {
                    GUILayout.Space(12);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("초대 IP", GUILayout.Width(60)); GUILayout.TextField(address);
                    if (GUILayout.Button("IP 복사", GUILayout.Width(85))) GUIUtility.systemCopyBuffer = address;
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("방 나가기")) { Leave(); status = "방에서 나왔습니다."; }
            }
            GUILayout.Space(15); GUILayout.Label(status);
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
            GUI.enabled = previousEnabled;
        }

        private GUIStyle TextButtonStyle(int fontSize)
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
                state.scaledBackgrounds = null;
                state.textColor = Color.white;
            }
            style.hover.textColor = new Color(0.65f, 0.85f, 1f);
            style.active.textColor = Color.gray;
            return style;
        }

        private void DrawEntryMenu(float scale, float offsetX)
        {
            bool canConnect = connectTask == null && server == null;
            GUI.enabled = canConnect && !showJoinInput && !showSettings;
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
            if (GUI.Button(new Rect(menuX, menuY, 330, 65), "Create room", menu)) CreateRoom();
            if (GUI.Button(new Rect(menuX, menuY + 80, 330, 65), "Join", menu)) showJoinInput = true;
            if (GUI.Button(new Rect(menuX, menuY + 160, 330, 65), "setting", menu)) showSettings = true;
            if (showJoinInput)
            {
                GUI.enabled = true;
                Color previousColor = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.7f);
                GUI.DrawTexture(new Rect(-offsetX / Mathf.Max(0.001f, scale), 0,
                    Screen.width / Mathf.Max(0.001f, scale), 700), Texture2D.whiteTexture);
                GUI.color = previousColor;
                GUI.Box(new Rect(280, 220, 440, 260), GUIContent.none);
                GUI.Label(new Rect(310, 240, 380, 40), "Join room", label);
                GUI.Label(new Rect(310, 290, 380, 35), "초대 IP / 코드", label);
                GUI.enabled = canConnect;
                joinInput = GUI.TextField(new Rect(310, 330, 380, 45), joinInput, input);
                var action = TextButtonStyle(26);
                if (GUI.Button(new Rect(310, 410, 150, 40), "접속", action)) JoinRoom();
                if (GUI.Button(new Rect(510, 410, 150, 40), "취소", action)) showJoinInput = false;
            }
            if (showSettings)
            {
                GUI.enabled = true;
                Color previousColor = GUI.color;
                GUI.color = new Color(0, 0, 0, 0.7f);
                GUI.DrawTexture(new Rect(-offsetX / Mathf.Max(0.001f, scale), 0,
                    Screen.width / Mathf.Max(0.001f, scale), 700), Texture2D.whiteTexture);
                GUI.color = previousColor;
                GUI.Box(new Rect(280, 220, 440, 260), GUIContent.none);
                GUI.Label(new Rect(310, 240, 380, 40), "setting", label);
                GUI.Label(new Rect(310, 310, 380, 40), "설정 항목은 준비 중입니다.", label);
                if (GUI.Button(new Rect(310, 410, 150, 40), "닫기", TextButtonStyle(26))) showSettings = false;
            }
            GUI.enabled = true;
            var message = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
            if (!string.IsNullOrEmpty(status))
                GUI.Label(new Rect(menuX, 650, 920, 45), status, message);
        }
    }
}
