using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Marco.Prototype
{
    public sealed class RoomSessionPrototype : MonoBehaviour
    {
        [SerializeField] private int port = 7777;
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

        private sealed class Peer : IDisposable
        {
            public readonly TcpClient Client;
            public readonly StreamReader Reader;
            public readonly StreamWriter Writer;
            public Task<string> Read;
            public string Name;
            public int Id;
            public float Deadline;
            public Peer(TcpClient client)
            {
                Client = client; client.NoDelay = true;
                Reader = new StreamReader(client.GetStream()); Writer = new StreamWriter(client.GetStream()) { AutoFlush = true };
                Read = Reader.ReadLineAsync();
            }
            public void Dispose() { Client.Close(); }
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
                if (acceptTask != null && acceptTask.IsCompleted)
                {
                    var peer = new Peer(acceptTask.GetAwaiter().GetResult()) { Id = nextId++, Deadline = Time.unscaledTime + 8 }; peers.Add(peer);
                    acceptTask = listener.AcceptTcpClientAsync();
                }
                if (connectTask != null && connectTask.IsCompleted)
                {
                    server = new Peer(connectTask.GetAwaiter().GetResult()); connectTask = null;
                    playerName = CleanName(playerName);
                    server.Writer.WriteLine("NAME " + EncodeName(playerName));
                    deadline = Time.unscaledTime + 8;
                }
                if (!joined && (connectTask != null || server != null) && Time.unscaledTime > deadline) { Leave(); status = "접속 시간 초과. 주소와 연결 상태를 확인하세요."; }
                for (int i = peers.Count - 1; i >= 0; i--)
                {
                    var peer = peers[i];
                    if (!peer.Read.IsCompleted)
                    {
                        if (peer.Name == null && Time.unscaledTime > peer.Deadline) { peer.Dispose(); peers.RemoveAt(i); }
                        continue;
                    }
                    string incoming = null;
                    try { incoming = peer.Read.GetAwaiter().GetResult(); } catch (IOException) { } catch (SocketException) { }
                    if (peer.Name == null && incoming != null && incoming.StartsWith("NAME "))
                    {
                        try { peer.Name = CleanName(DecodeName(incoming.Substring(5))); }
                        catch (FormatException) { peer.Dispose(); peers.RemoveAt(i); continue; }
                        peer.Read = peer.Reader.ReadLineAsync(); BroadcastMembers();
                    }
                    else { peer.Dispose(); peers.RemoveAt(i); BroadcastMembers(); }
                }
                if (server != null && server.Read.IsCompleted)
                {
                    string line = server.Read.GetAwaiter().GetResult();
                    if (line == null) { Leave(); status = "호스트와 연결이 종료되었습니다."; return; }
                    if (!line.StartsWith("MARCO/2 ROSTER "))
                    { Leave(); status = "호환되지 않는 방 응답입니다."; return; }
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
            catch (Exception e) { Leave(); status = "연결 실패: " + e.GetBaseException().Message; Debug.LogWarning("[Room] " + status); }
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
            foreach (var peer in peers) if (peer.Name != null) peer.Writer.WriteLine("MARCO/2 ROSTER " + string.Join("|", entries));
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
        }
        private void OnDestroy() { Leave(); }

        private void OnGUI()
        {
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
                rosterScroll = GUILayout.BeginScrollView(rosterScroll, GUILayout.Height(210));
                foreach (string name in roster) GUILayout.Label(name, GUI.skin.box, GUILayout.Height(34));
                GUILayout.EndScrollView();
                if (GUILayout.Button("게임 시작", GUILayout.Height(40))) status = "시작 버튼 확인 완료 — 게임 시작 연결은 다음 단계입니다.";
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
