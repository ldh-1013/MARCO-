using System.Collections.Generic;
using Marco.Network.Player;
using Marco.Network.Vision;
using Marco.Network.Voice;
using UnityEngine;

namespace Marco.Network.Lobby
{
    // 기획서 5.2 ③ · 부록 A [마이크 캘리브레이션] — the lobby's 3 s "가장 작은 목소리로 말해보세요" prompt.
    // Client only: the offset moves this player's own grade boundaries (MicrophoneVisionController); the server keeps
    // trusting the grade it is sent, as before. Prompts by itself the first time the room lobby shows, only when this
    // microphone has no saved offset; "마이크 다시 측정" is here only (not in a round, not in the settings window — 12.6).
    public sealed class LobbyMicCalibration : MonoBehaviour
    {
        private static readonly Color Cyan = new(.498f, .906f, .878f);    // #7FE7E0 (16.2)
        private static readonly Color Amber = new(1f, .722f, .302f);      // #FFB84D (16.2)
        private static readonly Color Pale = new(.91f, .91f, .91f);       // #E8E8E8 (16.2 환경)

        // Skipped or fell back this session: no automatic prompt again for that device until the game restarts.
        private static readonly HashSet<string> settledThisSession = new();

        private readonly MicCalibrationPrompt prompt = new();
        private MicCalibrationStore store;
        private LobbyMenu menu;
        private NetworkPlayer micOwner;
        private MicrophoneVisionController mic;
        private string device;
        private bool hasSaved;
        private bool lobbyWasShown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() => settledThisSession.Clear();

        private void Awake()
        {
            store = MicCalibrationStore.ForPlayerPrefs();
            menu = GetComponent<LobbyMenu>();
        }

        private void OnDisable() => CancelMeasuring();

        private void Update()
        {
            var local = NetworkPlayer.Local;
            if (local != micOwner)
            {
                CancelMeasuring();
                micOwner = local;
                mic = local != null ? local.GetComponentInChildren<MicrophoneVisionController>(true) : null;
                device = null;
            }
            if (mic == null || !mic.HasMicrophoneInput) return;

            if (mic.MicrophoneDevice != device)
            {
                // A different microphone: its own saved offset, or 0 and a prompt at the next lobby showing.
                CancelMeasuring();
                device = mic.MicrophoneDevice;
                float? saved = store.Load(device);
                hasSaved = saved.HasValue;
                mic.SetCalibrationOffset(saved ?? 0f);
                prompt.Restart();
                lobbyWasShown = false;
            }

            if (menu == null || !menu.InRoomLobby)
            {
                CancelMeasuring();   // the round started (or we left): the attempt is not counted
                lobbyWasShown = false;
                return;
            }
            bool shown = menu.RoomLobbyVisible;
            if (shown && !lobbyWasShown && !hasSaved && !settledThisSession.Contains(device) &&
                (prompt.Current == MicCalibrationPrompt.State.Idle || prompt.Current == MicCalibrationPrompt.State.Cancelled))
                Begin();
            if (shown) lobbyWasShown = true;
        }

        private void Begin()
        {
            if (mic == null || !prompt.Start()) return;
            mic.BeginCalibration(OnMeasured, OnCancelled);
        }

        private void OnMeasured(MicCalibration.Result result, bool muted)
        {
            var state = prompt.Report(result, muted);
            if (state == MicCalibrationPrompt.State.Success)
            {
                store.Save(device, prompt.Offset);
                hasSaved = true;
                mic.SetCalibrationOffset(prompt.Offset);
            }
            else if (state == MicCalibrationPrompt.State.Fallback)
            {
                mic.SetCalibrationOffset(0f);   // not saved: asked again next session
                if (device != null) settledThisSession.Add(device);
            }
        }

        private void OnCancelled() => prompt.Cancel();

        private void CancelMeasuring()
        {
            if (prompt.Current != MicCalibrationPrompt.State.Measuring) return;
            if (mic != null) mic.CancelCalibration();
            prompt.Cancel();
        }

        private void Skip()
        {
            CancelMeasuring();
            prompt.Skip();
            if (mic != null) mic.SetCalibrationOffset(0f);   // this session only — nothing is saved
            if (device != null) settledThisSession.Add(device);
        }

        private void Remeasure()
        {
            CancelMeasuring();
            prompt.Restart();
            Begin();
        }

        // Drawn by LobbyMenu.DrawRoom under the room panel (1000 x 700 reference canvas).
        public void Draw(Rect rect)
        {
            if (mic == null || !mic.HasMicrophoneInput) return;
            TestHudSkin.Fill(rect, new Color(0, 0, 0, .72f));
            var text = new Rect(rect.x + 16, rect.y + 10, rect.width - 32, 30);
            var buttonA = new Rect(rect.xMax - 16 - 190, rect.yMax - 16 - 40, 190, 40);
            var buttonB = new Rect(buttonA.x - 12 - 150, buttonA.y, 150, 40);
            switch (prompt.Current)
            {
                case MicCalibrationPrompt.State.Measuring:
                    Line(text, "가장 작은 목소리로 말해보세요", Pale);
                    TestHudSkin.Bar(new Rect(rect.x + 16, rect.y + 48, rect.width - 32, 10), mic.CalibrationProgress, Cyan);
                    if (RuinedUi.Button(buttonA, "건너뛰기", 22, light: true)) Skip();
                    break;
                case MicCalibrationPrompt.State.Retry:
                    Line(text, FailureText(prompt.LastFailure), Amber);
                    Line(new Rect(text.x, text.y + 28, text.width, 24), "시도 " + prompt.Attempts + " / " + MicCalibration.MaxAttempts, Pale, 18);
                    if (RuinedUi.Button(buttonA, "다시 시도", 22, light: true)) Begin();
                    if (RuinedUi.Button(buttonB, "건너뛰기", 22, light: true)) Skip();
                    break;
                case MicCalibrationPrompt.State.Fallback:
                    Line(text, "마이크 보정 없이 기본 감도로 진행합니다", Amber);
                    if (RuinedUi.Button(buttonA, "마이크 다시 측정", 22, light: true)) Remeasure();
                    break;
                case MicCalibrationPrompt.State.Skipped:
                    Line(text, "기본 감도로 진행합니다", Pale);
                    if (RuinedUi.Button(buttonA, "마이크 다시 측정", 22, light: true)) Remeasure();
                    break;
                default:   // Success, or a saved offset loaded quietly; Idle / Cancelled waiting for the next showing
                    Line(text, hasSaved || prompt.Current == MicCalibrationPrompt.State.Success ? "보정 완료" : "마이크 보정 전", Cyan);
                    if (RuinedUi.Button(buttonA, "마이크 다시 측정", 22, light: true)) Remeasure();
                    break;
            }
        }

        public static string FailureText(MicCalibrationPrompt.Failure failure) => failure switch
        {
            MicCalibrationPrompt.Failure.Clipped => "소리가 너무 커요. 마이크에서 조금 떨어져서 다시 말해 보세요",
            MicCalibrationPrompt.Failure.Muted => "음소거 중이에요",
            _ => "목소리가 잘 안 들려요. 마이크에 조금 더 가까이 가서 다시 말해 보세요",
        };

        private static void Line(Rect rect, string message, Color color, int size = 22)
        {
            var style = new GUIStyle(TestHudSkin.Text(size)) { wordWrap = true };
            style.normal.textColor = color;
            GUI.Label(rect, message, style);
        }
    }
}
