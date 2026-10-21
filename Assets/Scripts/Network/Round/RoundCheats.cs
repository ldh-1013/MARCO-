#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Marco.Network.Round
{
    // Dev-only F1~F5 keys for checking round results without playing a full round. Host only:
    // the keys call NetworkRound's server cheats, which still run every outcome through Core.
    // Created automatically so no scene has to carry it; compiled out of release builds.
    public sealed class RoundCheats : MonoBehaviour
    {
        private const float FastForwardSeconds = 60f;

        private static readonly string[] Lines =
        {
            "[치트 · Host 전용]",
            "F1  활성 밸브 전부 열기",
            "F2  도망자 1명 태그",
            "F3  도망자 1명 탈출",
            $"F4  {FastForwardSeconds:0}초 빨리감기",
            "F5  최후 생존자 페이즈",
        };

        private GUIStyle style;

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
            if (!TryGetHostRound(out var round)) return;

            if (Input.GetKeyDown(KeyCode.F1)) round.CheatOpenActiveValves();
            else if (Input.GetKeyDown(KeyCode.F2)) round.CheatTagRunner();
            else if (Input.GetKeyDown(KeyCode.F3)) round.CheatEscapeRunner();
            else if (Input.GetKeyDown(KeyCode.F4)) round.CheatFastForward(FastForwardSeconds);
            else if (Input.GetKeyDown(KeyCode.F5)) round.CheatEnterLastSurvivorPhase();
        }

        // Top-right corner; NetworkTestHud owns the left side.
        private void OnGUI()
        {
            if (!TryGetHostRound(out _)) return;

            style ??= new GUIStyle(GUI.skin.label) { fontSize = 14 };
            const float width = 220, lineHeight = 20;
            GUI.Box(new Rect(Screen.width - width - 16, 16, width, Lines.Length * lineHeight + 12), GUIContent.none);
            for (int i = 0; i < Lines.Length; i++)
                GUI.Label(new Rect(Screen.width - width - 8, 22 + i * lineHeight, width - 16, lineHeight), Lines[i], style);
        }
    }
}
#endif
