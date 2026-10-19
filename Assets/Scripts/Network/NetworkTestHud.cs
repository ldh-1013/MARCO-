using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network
{
    // Temporary IMGUI panel for the LAN test scene: Host / IP Join / status, player count and roles.
    [RequireComponent(typeof(LanConnector))]
    public sealed class NetworkTestHud : MonoBehaviour
    {
        private const float PanelWidth = 340;

        [SerializeField] private string joinAddress = "127.0.0.1";

        // Input.mousePosition is bottom-left based; the panel spans the full height on the left.
        public static bool IsPointerOver => Input.mousePosition.x <= 16 + PanelWidth;

        private LanConnector connector;
        private GUIStyle statusStyle;

        private void Awake() => connector = GetComponent<LanConnector>();

        private void OnGUI()
        {
            statusStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(16, 16, PanelWidth, Screen.height - 32), GUI.skin.box);
            switch (connector.Status)
            {
                case LanConnectionStatus.Connected:
                    GUILayout.Label("연결됨", statusStyle);
                    GUILayout.Label(connector.IsHost ? "접속: Host" : "접속: Client");
                    GUILayout.Label($"접속 인원: {connector.PlayerCount}명", statusStyle);
                    DrawRoles();
                    DrawRound();
                    if (GUILayout.Button("연결 끊기", GUILayout.Height(32))) connector.Leave();
                    break;

                case LanConnectionStatus.Connecting:
                    GUILayout.Label("연결 중…", statusStyle);
                    if (GUILayout.Button("취소", GUILayout.Height(32))) connector.Leave();
                    break;

                default:
                    GUILayout.Label(connector.Status == LanConnectionStatus.Failed ? "연결 실패 — IP를 확인하세요" : "오프라인", statusStyle);
                    if (GUILayout.Button("Host", GUILayout.Height(32))) connector.Host();
                    GUILayout.Space(8);
                    GUILayout.Label("Host IP");
                    joinAddress = GUILayout.TextField(joinAddress);
                    if (GUILayout.Button("Join", GUILayout.Height(32))) connector.Join(joinAddress);
                    break;
            }
            GUILayout.EndArea();
        }

        private void DrawRoles()
        {
            foreach (var player in NetworkPlayer.All)
                if (player.IsOwner) GUILayout.Label($"내 역할: {player.Role}", statusStyle);
            foreach (var player in NetworkPlayer.All)
                GUILayout.Label($"P{player.PlayerId}: {player.Role}{(player.IsOwner ? " (나)" : "")}");
        }

        private void DrawRound()
        {
            var round = NetworkRound.Instance;
            if (round == null) return;

            GUILayout.Space(8);
            if (round.Mode == RoundMode.NotStarted)
            {
                if (connector.IsHost && GUILayout.Button("라운드 시작 (Esc로 커서 풀기)", GUILayout.Height(32)))
                {
                    // [Server] on StartRound silently skips the call unless the scene object is spawned on the server.
                    Debug.Log($"[MARCO][HUD] 라운드 시작 클릭 (IsSpawned {round.IsSpawned}, IsServerInitialized {round.IsServerInitialized})");
                    round.StartRound();
                }
                else if (!connector.IsHost) GUILayout.Label("Host가 라운드를 시작하길 기다리는 중");
                return;
            }

            GUILayout.Label(round.Mode == RoundMode.Full
                ? $"라운드 진행 중 · 남은 시간 {round.RemainingSeconds:0}초"
                : "밸브 전용 모드 (Runner 2명 미만)");
            if (round.Result != RoundResult.InProgress) GUILayout.Label($"라운드 종료: {round.Result}");
            GUILayout.Label(round.GateOpen ? "게이트: 열림" : "게이트: 닫힘", statusStyle);
            for (var id = ValveId.A; id <= ValveId.E; id++)
            {
                round.TryGetValveState(id, out var state);
                GUILayout.Label(state.Active ? $"밸브 {id}: {state.Status} {state.Progress:P0}" : $"밸브 {id}: 비활성");
            }
            GUILayout.Label("E 홀드: 가까운 밸브 작업 (Runner만)");
        }
    }
}
