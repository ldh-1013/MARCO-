#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Marco.Core.WinCondition;
using UnityEngine;

namespace Marco.Network.Round
{
    // Dev-only cheat panel (Tab) for checking round results without playing a full round. Host only:
    // F1~F5 and F9 plus the buttons call NetworkRound's server cheats. F9 swaps the host's role
    // with an active player of the opposite role when possible.
    // The panel also owns round start/restart. Opening it frees the cursor; closing relocks it.
    // Created automatically so no scene has to carry it; compiled out of release builds.
    public sealed class RoundCheats : MonoBehaviour
    {
        private const float FastForwardSeconds = 60f;
        private const float PanelWidth = 640, ButtonHeight = 56, Gap = 8;

        private static readonly (KeyCode Key, string Label)[] Cheats =
        {
            (KeyCode.F1, "F1   마스터키: 활성 밸브 전부 열기"),
            (KeyCode.F2, "F2   도망자 1명 태그"),
            (KeyCode.F3, "F3   도망자 1명 탈출"),
            (KeyCode.F4, $"F4   {FastForwardSeconds:0}초 빨리감기"),
            (KeyCode.F5, "F5   최후 생존자 페이즈 시작"),
            (KeyCode.F9, "F9   내 역할 전환: 생존자 ↔ 헌터"),
        };

        private bool open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject(nameof(RoundCheats));
            DontDestroyOnLoad(go);
            go.AddComponent<RoundCheats>();
        }

        // NetworkBehaviour's IsXxx properties read its NetworkObject without a null check, and that
        // reference is unset until FishNet initializes the scene object, so test it first.
        private static bool TryGetHostRound(out NetworkRound round)
        {
            round = NetworkRound.Instance;
            return round != null && round.NetworkObject != null && round.NetworkObject.IsHostInitialized;
        }

        private void Update()
        {
            if (!TryGetHostRound(out var round))
            {
                // Hosting stopped: drop the panel without relocking a cursor nothing uses anymore.
                open = false;
                TestHudSkin.HoldsCursor = false;
                return;
            }

            if (Input.GetKeyDown(KeyCode.Tab)) SetOpen(!open);
            // Esc toggles the lock in PlayerMovement; keep the cursor free while the panel is up.
            if (open && Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;

            foreach (var (key, _) in Cheats)
                if (Input.GetKeyDown(key)) Run(round, key);
        }

        private void SetOpen(bool value)
        {
            open = value;
            TestHudSkin.HoldsCursor = value;
            Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;
        }

        private static void Run(NetworkRound round, KeyCode key)
        {
            switch (key)
            {
                case KeyCode.F1: round.CheatOpenActiveValves(); break;
                case KeyCode.F2: round.CheatTagRunner(); break;
                case KeyCode.F3: round.CheatEscapeRunner(); break;
                case KeyCode.F4: round.CheatFastForward(FastForwardSeconds); break;
                case KeyCode.F5: round.CheatEnterLastSurvivorPhase(); break;
                case KeyCode.F9: round.CheatToggleHostRole(); break;
            }
        }

        // Bottom center, clear of the HUD's top / left / right panels and the centered result.
        private void OnGUI()
        {
            if (!TryGetHostRound(out var round)) return;

            TestHudSkin.Begin();
            if (!open)
            {
                var hint = new Rect((TestHudSkin.Width - 300) / 2, TestHudSkin.Height - 56 - TestHudSkin.Margin, 300, 56);
                TestHudSkin.Fill(hint, TestHudSkin.PanelColor);
                GUI.Label(hint, "Tab: 치트 패널", TestHudSkin.Text(24, FontStyle.Bold, TextAnchor.MiddleCenter));
                return;
            }

            float height = TestHudSkin.Padding * 2 + 48 + (Cheats.Length + 1) * (ButtonHeight + Gap) + 16;
            var rect = new Rect((TestHudSkin.Width - PanelWidth) / 2, TestHudSkin.Height - height - TestHudSkin.Margin, PanelWidth, height);
            TestHudSkin.Panel(rect);
            float x = rect.x + TestHudSkin.Padding, width = PanelWidth - TestHudSkin.Padding * 2;
            float y = rect.y + TestHudSkin.Padding;
            GUI.Label(new Rect(x, y, width, 40), "치트 · Host 전용  (Tab 닫기)", TestHudSkin.Text(28, FontStyle.Bold));
            y += 48;

            var button = TestHudSkin.Button(24);
            DrawRoundButton(round, new Rect(x, y, width, ButtonHeight), button);
            y += ButtonHeight + Gap + 16;

            // Cheats need a running round; NetworkRound ignores them otherwise.
            GUI.enabled = round.Mode != RoundMode.NotStarted;
            foreach (var (key, label) in Cheats)
            {
                if (GUI.Button(new Rect(x, y, width, ButtonHeight), label, button)) Run(round, key);
                y += ButtonHeight + Gap;
            }
            GUI.enabled = true;
        }

        private static void DrawRoundButton(NetworkRound round, Rect rect, GUIStyle style)
        {
            if (round.Mode == RoundMode.NotStarted)
            {
                if (!GUI.Button(rect, "▶  라운드 시작", style)) return;
                // [Server] on StartRound silently skips the call unless the scene object is spawned on the server.
                Debug.Log($"[MARCO][Cheats] 라운드 시작 클릭 (IsSpawned {round.IsSpawned}, IsServerInitialized {round.IsServerInitialized})");
                round.StartRound();
                return;
            }

            // ValvesOnly never decides a result, so the host can restart it at any time.
            bool canRestart = round.Result != RoundResult.InProgress || round.Mode == RoundMode.ValvesOnly;
            GUI.enabled = canRestart;
            if (GUI.Button(rect, canRestart ? "↻  다시 시작" : "↻  다시 시작 (라운드 진행 중)", style)) round.RestartRound();
            GUI.enabled = true;
        }
    }
}
#endif
