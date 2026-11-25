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
        private static readonly Color Amber = new(1f, .722f, .302f);      // #FFB84D (16.2)
        private static readonly Color Pale = new(.91f, .91f, .91f);       // #E8E8E8 (16.2 환경)

        // Skipped this session: no automatic prompt again for that device until the game restarts.
        private static readonly HashSet<string> settledThisSession = new();

        private readonly MicCalibrationPrompt prompt = new();
        private MicCalibrationStore store;
        private LobbyMenu menu;
        private NetworkPlayer micOwner;
        private MicrophoneVisionController mic;
        private string device;
        public bool HasInput => mic != null && mic.HasMicrophoneInput;
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
            if (!HasInput) return;

            if (mic.MicrophoneDevice != device)
            {
                // A different microphone: its own saved offset, or 0 and a prompt at the next lobby showing.
                CancelMeasuring();
                device = mic.MicrophoneDevice;
                float? saved = store.Load(device);
                hasSaved = saved.HasValue;
                mic.SetCalibrationOffset(saved ?? 0f);
                mic.SetVoiceFloor(store.LoadVoiceFloor(device));
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
                float floor = mic.VoiceFloorFor(result);
                store.Save(device, prompt.Offset, floor);
                mic.SetVoiceFloor(floor);
                hasSaved = true;
                mic.SetCalibrationOffset(prompt.Offset);
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
            if (mic != null) { mic.SetVoiceFloor(null); mic.SetCalibrationOffset(0f); }   // this session only — nothing is saved
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
            if (!HasInput) return;
            RoomArt.Draw(rect, "panel/마이크_보정");
            const float headerTop = 16, headerHeight = 32, contentLeft = 184;
            RoomArt.Draw(new Rect(rect.x + 22, rect.y + headerTop, 128, headerHeight), "label/마이크 보정", 24);
            TestHudSkin.Fill(new Rect(rect.x + 166, rect.y + 18, 1, rect.height - 36),
                new Color(.72f, .66f, .54f, .5f));
            var text = new Rect(rect.x + contentLeft, rect.y + 12, rect.width - contentLeft - 22, 56);
            var stateLabel = new Rect(text.x, rect.y + headerTop, text.width, headerHeight);
            var buttonA = new Rect(rect.xMax - 16 - 190, rect.yMax - 16 - 40, 190, 40);
            var buttonB = new Rect(buttonA.x - 12 - 150, buttonA.y, 150, 40);
            switch (prompt.Current)
            {
                case MicCalibrationPrompt.State.Measuring:
                    Line(text, "가장 작은 목소리로 말해보세요. 주변 잡음을 걸러낼 기준을 맞춥니다", Pale);
                    var gauge = new Rect(text.x, rect.y + 75, text.width, 10);
                    RoomArt.Draw(gauge, "list/측정 게이지 바탕");
                    GUI.BeginGroup(new Rect(gauge.x, gauge.y, gauge.width * Mathf.Clamp01(mic.CalibrationProgress), gauge.height));
                    RoomArt.Draw(new Rect(0, 0, gauge.width, gauge.height), "list/측정 게이지 채움");
                    GUI.EndGroup();
                    if (LobbyMenu.RoomButton(buttonA, "건너뛰기", 20, useLettering: true)) Skip();
                    break;
                case MicCalibrationPrompt.State.Retry:
                    Line(text, FailureText(prompt.LastFailure), Amber);
                    if (LobbyMenu.RoomButton(buttonA, "다시 시도", 20, useLettering: true)) Begin();
                    if (LobbyMenu.RoomButton(buttonB, "건너뛰기", 20, useLettering: true)) Skip();
                    break;
                case MicCalibrationPrompt.State.Skipped:
                    Line(stateLabel, "기본 감도로 진행합니다", Pale);
                    if (LobbyMenu.RoomButton(buttonA, "마이크 다시 측정", 20, useLettering: true)) Remeasure();
                    break;
                default:   // Success, or a saved offset loaded quietly; Idle / Cancelled waiting for the next showing
                    RoomArt.Draw(stateLabel, hasSaved || prompt.Current == MicCalibrationPrompt.State.Success ? "label/보정 완료" : "label/마이크 보정 전", 26);
                    if (LobbyMenu.RoomButton(buttonA, "마이크 다시 측정", 20, useLettering: true)) Remeasure();
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
            // Retry advice contains two sentences. Keep the action on its own baseline.
            int split = message.IndexOf(". ", System.StringComparison.Ordinal);
            if (split < 0) RoomLettering.Draw(rect, message, size, color);
            else
            {
                float half = rect.height * .5f;
                RoomLettering.Draw(new Rect(rect.x, rect.y, rect.width, half), message.Substring(0, split + 1), size, color);
                RoomLettering.Draw(new Rect(rect.x, rect.y + half, rect.width, half), message.Substring(split + 2), size, color);
            }
        }
    }
}
