using System;
using System.Collections;
using Marco.Core.WinCondition;
using Marco.Network.Lobby;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Marco.Network.Settings
{
    // Esc menu and settings window (기획서 12.6 · 14.2 P4). Local UI only: the game never pauses, nothing is sent,
    // the server's timers (camping breath and the rest) keep running while it is open.
    //   Esc            the only place that reads Esc: opens the menu in game (계속하기 / 설정 / 방 나가기 / 게임 종료),
    //                  the settings window directly where nobody is connected yet; Esc again goes one step back
    //   while open     the cursor stays free (every gameplay input already requires a locked cursor), the
    //                  microphone and voice keep working; on close the cursor locks again inside a running round
    //   network flow   made only where FishNet's NetworkManager or the LobbyMenu exists — never in the old
    //                  prototype scenes (Assets/Prototype), which keep their own Esc and F6
    public sealed class SettingsMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        private static SettingsMenu instance;
        private static bool hooked;

        private enum View { None, Menu, Settings }
        private enum Confirm { None, Leave, Quit }

        private View view;
        private bool settingsFromMenu;
        private Confirm confirm;
        private bool networkFlow;
        private CursorLockMode cursorBefore;

        private Canvas canvas;
        private GameObject menuRoot, escPanel, settingsPanel, confirmPanel, tooltip;
        private RawImage blur;
        private Image dim;
        private RenderTexture blurTexture;
        private Coroutine capture;
        private Text confirmText, tooltipText, fpsText;
        private Button confirmOk;
        private readonly GameObject[] tabs = new GameObject[4];
        private readonly Image[] tabButtons = new Image[4];
        private int tab;

        // Graphics / audio / controls widgets.
        private Slider fovSlider, brightnessSlider, volumeSlider, mouseSlider;
        private Text fovValue, brightnessValue, volumeValue, mouseValue, displayValue, resolutionValue, frameValue;
        private Toggle colorBlindToggle, fpsToggle, invertToggle;
        private Text micDeviceText, micGradeText;
        private RectTransform micBar;
        private bool building;
        private SettingsKeyGuide keyGuide;
        private Resolution[] resolutions = Array.Empty<Resolution>();
        private int resolutionIndex = -1;

        // Saving: gathered for half a second after a change, and on close.
        private bool dirty;
        private float saveAt;

        // FOV: bound once per local player, recomputed only when the camera's aspect changes.
        private NetworkPlayer fovPlayer;
        private Camera fovCamera;
        private float fovAspect = -1f, fovApplied = float.NaN, fovPrefab = 60f;

        // Connection / round / scene changes while open close the menu safely.
        private LanConnectionStatus lastStatus;
        private RoundResult lastResult = RoundResult.InProgress;
        private LanConnector connector;

        // Microphone meter (read only from MicrophoneVisionController) and the device line.
        private NetworkPlayer micPlayer;
        private MicrophoneVisionController micController;
        private int shownGrade = -2;
        private float nextDeviceCheck;
        private volatile bool audioConfigChanged;

        // FPS: smoothed, text only when the number changes.
        private static readonly string[] FpsText = MakeFpsText();
        private int shownFps = -1;
        private float fpsTime;
        private int fpsFrames;

        private static readonly string[] GradeText = { "마이크 사용 중이 아님", "조용함", "속삭임", "대화", "고함" };
        private static readonly string[] DisplayText = { "그대로(실행 설정)", "전체 화면", "전체 화면 창모드", "창 모드" };
        private static readonly string[] FrameText = { "60", "120", "144", "240", "제한 없음" };
        private static readonly string[] TabNames = { "그래픽", "오디오", "컨트롤", "키 가이드" };
        private const string Notice = "게임은 멈추지 않습니다";
        private const string MicTip = "Windows의 기본 마이크를 사용합니다. 바꾸려면 Windows 소리 설정에서 기본 입력 장치를 바꾼 뒤 게임을 다시 시작하세요.";
        private const string OutputTip = "Windows의 기본 스피커를 사용합니다. 바꾸려면 Windows 소리 설정에서 기본 출력 장치를 바꾸세요. 게임 중에 바꾸면 소리가 잠시 끊길 수 있습니다.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsOpen = false;
            instance = null;
            if (hooked) SceneManager.sceneLoaded -= OnSceneLoaded;
            hooked = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (!SettingsRuntime.Enabled) return;
            if (!hooked) { SceneManager.sceneLoaded += OnSceneLoaded; hooked = true; }
            TryCreate();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (instance != null) { instance.networkFlow = IsNetworkFlow(); instance.CloseAll(); }
                else TryCreate();
            }
            catch (Exception exception) { SettingsRuntime.Report("sceneLoaded", exception); }
        }

        private static bool IsNetworkFlow() =>
            FindAnyObjectByType<FishNet.Managing.NetworkManager>() != null || FindAnyObjectByType<LobbyMenu>() != null;

        private static void TryCreate()
        {
            if (instance != null || !IsNetworkFlow()) return;
            var go = new GameObject("Settings Menu");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SettingsMenu>();
            instance.networkFlow = true;
        }

        // LobbyMenu's "설정" button: the settings window itself (no 방 나가기 before a room exists).
        public static void OpenSettingsFromMenu()
        {
            if (instance == null) return;
            instance.Open(View.Settings);
        }

        private void Awake()
        {
            try { Build(); }
            catch (Exception exception) { SettingsRuntime.Report("build", exception); enabled = false; }
        }

        private void OnEnable() => AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;

        private void OnDisable()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (IsOpen) CloseAll();
        }

        private void OnDestroy()
        {
            ReleaseBlur();
            if (instance == this) { instance = null; IsOpen = false; }
        }

        // May come from the audio thread: only a flag here, the work happens in Update.
        private void OnAudioConfigurationChanged(bool deviceWasChanged) => audioConfigChanged = true;

        private void Update()
        {
            if (audioConfigChanged)
            {
                audioConfigChanged = false;
                Debug.Log("[MARCO][Settings] 오디오 출력 장치 구성이 바뀜 — 마스터 볼륨을 다시 적용");
                SettingsRuntime.ApplyVolume();
            }
            TrackFov();
            SettingsRuntime.KeepFrameLimit();
            TickFps();
            if (dirty && Time.unscaledTime >= saveAt) { dirty = false; SettingsRuntime.Save(); }
            if (!networkFlow) return;

            WatchSession();
            if (Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
            if (IsOpen && view == View.Settings && tab == 1) TickMicrophone();
        }

        // Every other cursor owner (lobby, result vote, cheat panel, player spawn) may set the lock in its Update;
        // while the menu is open it stays free.
        private void LateUpdate()
        {
            if (!IsOpen) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        private void HandleEscape()
        {
            if (confirm != Confirm.None) { ShowConfirm(Confirm.None); return; }
            if (IsOpen)
            {
                if (view == View.Settings && settingsFromMenu) ShowView(View.Menu);
                else CloseAll();
                return;
            }
            // Placing a sound can: Esc does nothing (G, Left Alt or leaving the conditions cancel it).
            var local = NetworkPlayer.Local;
            if (local != null && (local.IsPlacingSoundCan || local.SoundCanStage == SoundCanStage.Placing)) return;
            Open(Connected() ? View.Menu : View.Settings);
        }

        private bool Connected()
        {
            if (connector == null) connector = FindAnyObjectByType<LanConnector>();
            return connector != null && connector.Status == LanConnectionStatus.Connected;
        }

        private void Open(View first)
        {
            if (canvas == null) return;
            if (!IsOpen)
            {
                cursorBefore = Cursor.lockState;
                IsOpen = true;
                settingsFromMenu = first == View.Menu;
                lastStatus = connector != null ? connector.Status : LanConnectionStatus.Offline;
                lastResult = NetworkRound.Instance != null ? NetworkRound.Instance.Result : RoundResult.InProgress;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                view = first;
                // The blurred backdrop is the frame under the menu: capture first, then show.
                if (capture != null) StopCoroutine(capture);
                capture = StartCoroutine(CaptureThenShow());
                return;
            }
            ShowView(first);
        }

        private IEnumerator CaptureThenShow()
        {
            yield return new WaitForEndOfFrame();
            bool blurred = false;
            try { blurred = MakeBlur(); }
            catch (Exception exception) { SettingsRuntime.Report("blur", exception); ReleaseBlur(); }
            capture = null;
            if (!IsOpen) { ReleaseBlur(); yield break; }
            blur.enabled = blurred;
            dim.color = SettingsUi.BlackA(blurred ? .6f : .85f);
            menuRoot.SetActive(true);
            ShowView(view);
        }

        // Screen at 1/2 → 1/4 → 1/8, the last one shown stretched with bilinear filtering (no new shader).
        private bool MakeBlur()
        {
            ReleaseBlur();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            if (shot == null) return false;
            try
            {
                int w = Mathf.Max(8, shot.width), h = Mathf.Max(8, shot.height);
                var half = RenderTexture.GetTemporary(w / 2, h / 2, 0);
                var quarter = RenderTexture.GetTemporary(w / 4, h / 4, 0);
                half.filterMode = quarter.filterMode = FilterMode.Bilinear;
                blurTexture = new RenderTexture(Mathf.Max(1, w / 8), Mathf.Max(1, h / 8), 0) { filterMode = FilterMode.Bilinear, name = "Settings blur" };
                Graphics.Blit(shot, half);
                Graphics.Blit(half, quarter);
                Graphics.Blit(quarter, blurTexture);
                RenderTexture.ReleaseTemporary(half);
                RenderTexture.ReleaseTemporary(quarter);
                blur.texture = blurTexture;
                return true;
            }
            finally { Destroy(shot); }
        }

        private void ReleaseBlur()
        {
            if (blur != null) { blur.texture = null; blur.enabled = false; }
            if (blurTexture != null) { blurTexture.Release(); Destroy(blurTexture); blurTexture = null; }
        }

        private void CloseAll()
        {
            if (!IsOpen && menuRoot != null && !menuRoot.activeSelf) return;
            if (capture != null) { StopCoroutine(capture); capture = null; }
            ShowConfirm(Confirm.None);
            HideTooltip();
            if (menuRoot != null) menuRoot.SetActive(false);
            ReleaseBlur();
            view = View.None;
            bool wasOpen = IsOpen;
            IsOpen = false;
            if (dirty) { dirty = false; SettingsRuntime.Save(); }
            if (!wasOpen) return;
            // Back into a running round: lock again; anywhere else the cursor goes back to how it was.
            var round = NetworkRound.Instance;
            var lobby = FindAnyObjectByType<LobbyMenu>();
            bool inRound = NetworkPlayer.Local != null && round != null && round.Mode != RoundMode.NotStarted &&
                round.Result == RoundResult.InProgress && !round.IsResultScreenShown && (lobby == null || !lobby.IsOpen);
            Cursor.lockState = inRound ? CursorLockMode.Locked : cursorBefore;
        }

        // A dropped connection, a round that just ended or a scene change closes the menu.
        private void WatchSession()
        {
            if (!IsOpen) return;
            try
            {
                var status = connector != null ? connector.Status : LanConnectionStatus.Offline;
                bool dropped = lastStatus == LanConnectionStatus.Connected && status != LanConnectionStatus.Connected;
                lastStatus = status;
                var round = NetworkRound.Instance;
                var result = round != null ? round.Result : RoundResult.InProgress;
                bool ended = lastResult == RoundResult.InProgress && result != RoundResult.InProgress;
                lastResult = result;
                if (dropped || ended) CloseAll();
            }
            catch (Exception exception) { SettingsRuntime.Report("session", exception); CloseAll(); }
        }

        private void ShowView(View next)
        {
            view = next;
            if (escPanel != null) escPanel.SetActive(next == View.Menu);
            if (settingsPanel != null) settingsPanel.SetActive(next == View.Settings);
            if (next == View.Settings) { SyncWidgets(); SelectTab(tab); }
        }

        // ------------------------------------------------------------------ build

        private void Build()
        {
            var root = new GameObject("Settings Canvas", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            root.AddComponent<GraphicRaycaster>();
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Settings EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }

            fpsText = SettingsUi.Label(root.transform, "FPS", 0, 0, 160, 32, "", 20, SettingsUi.Light, TextAnchor.MiddleLeft);
            var fpsRect = fpsText.rectTransform;
            fpsRect.anchorMin = fpsRect.anchorMax = fpsRect.pivot = new Vector2(0, 0);
            fpsRect.anchoredPosition = new Vector2(16, 112);
            fpsText.gameObject.SetActive(false);

            menuRoot = SettingsUi.Stretch(root.transform, "Menu").gameObject;
            var blurRect = SettingsUi.Stretch(menuRoot.transform, "Blur");
            blur = blurRect.gameObject.AddComponent<RawImage>();
            blur.raycastTarget = false;
            blur.enabled = false;
            var dimRect = SettingsUi.Stretch(menuRoot.transform, "Dim");
            dim = SettingsUi.Fill(dimRect, SettingsUi.BlackA(.85f), true);   // also swallows clicks outside the panels

            BuildEscPanel(menuRoot.transform);
            BuildSettingsPanel(menuRoot.transform);
            BuildConfirm(menuRoot.transform);
            BuildTooltip(menuRoot.transform);
            menuRoot.SetActive(false);
        }

        private static RectTransform Centered(Transform parent, string name, float w, float h) =>
            SettingsUi.Rect(parent, name, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(w, h));

        private void BuildEscPanel(Transform parent)
        {
            var panel = Centered(parent, "Esc Menu", 460, 440);
            SettingsUi.Fill(panel, SettingsUi.BlackA(.82f), true);
            escPanel = panel.gameObject;
            SettingsUi.Label(panel, "Notice", 0, 18, 460, 36, Notice, 22, SettingsUi.Amber, TextAnchor.MiddleCenter);
            SettingsUi.Button(panel, "Continue", 50, 76, 360, 68, "계속하기", 26, CloseAll);
            SettingsUi.Button(panel, "Settings", 50, 160, 360, 68, "설정", 26, () => { settingsFromMenu = true; ShowView(View.Settings); });
            SettingsUi.Button(panel, "Leave", 50, 244, 360, 68, "방 나가기", 26, () => ShowConfirm(Confirm.Leave));
            SettingsUi.Button(panel, "Quit", 50, 328, 360, 68, "게임 종료", 26, () => ShowConfirm(Confirm.Quit));
            escPanel.SetActive(false);
        }

        private void BuildSettingsPanel(Transform parent)
        {
            const float W = 1000, H = 640;
            var panel = Centered(parent, "Settings", W, H);
            SettingsUi.Fill(panel, SettingsUi.BlackA(.86f), true);
            settingsPanel = panel.gameObject;
            SettingsUi.Label(panel, "Notice", 0, 10, W, 30, Notice, 20, SettingsUi.Amber, TextAnchor.MiddleCenter);
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var button = SettingsUi.Button(panel, "Tab " + TabNames[i], 20 + i * 240, 46, 228, 52, TabNames[i], 24, () => SelectTab(index));
                tabButtons[i] = button.GetComponent<Image>();
                var page = SettingsUi.At(panel, "Page " + TabNames[i], 0, 110, W, H - 190);
                tabs[i] = page.gameObject;
            }
            building = true;
            BuildGraphics(tabs[0].transform);
            BuildAudio(tabs[1].transform);
            BuildControls(tabs[2].transform);
            keyGuide = new SettingsKeyGuide();
            keyGuide.Build(tabs[3].transform, W);
            building = false;
            SettingsUi.Button(panel, "Defaults", W - 380, H - 70, 200, 52, "기본값 복원", 22, RestoreDefaults);
            SettingsUi.Button(panel, "Close", W - 170, H - 70, 150, 52, "닫기", 22,
                () => { if (settingsFromMenu) ShowView(View.Menu); else CloseAll(); });
            settingsPanel.SetActive(false);
        }

        private const float LabelX = 60, ControlX = 440, ControlW = 420, RowH = 58;

        private static void RowLabel(Transform page, int row, string text) =>
            SettingsUi.Label(page, "Label " + text, LabelX, 14 + row * RowH, 360, 44, text, 22, SettingsUi.Light);

        private Text ValueLabel(Transform page, int row) =>
            SettingsUi.Label(page, "Value " + row, ControlX + ControlW + 16, 14 + row * RowH, 110, 44, "", 22, SettingsUi.Light);

        private void BuildGraphics(Transform page)
        {
            RowLabel(page, 0, "FOV (가로)");
            fovSlider = SettingsUi.Slider(page, "FOV", ControlX, 14, ControlW, 44, SettingsValues.FovMin, SettingsValues.FovMax, true,
                v => Change(s => s.Fov = v));
            fovValue = ValueLabel(page, 0);
            RowLabel(page, 1, "밝기(파문 대비)");
            brightnessSlider = SettingsUi.Slider(page, "Brightness", ControlX, 14 + RowH, ControlW, 44, SettingsValues.BrightnessMin * 100f,
                SettingsValues.BrightnessMax * 100f, true, v => Change(s => s.Brightness = v / 100f));
            brightnessValue = ValueLabel(page, 1);
            RowLabel(page, 2, "색맹 모드");
            colorBlindToggle = SettingsUi.Toggle(page, "Color Blind", ControlX, 14 + 2 * RowH + 4, 36, v => Change(s => s.ColorBlind = v));
            RowLabel(page, 3, "디스플레이 모드");
            displayValue = SettingsUi.Stepper(page, "Display", ControlX, 14 + 3 * RowH, ControlW, 44,
                d => Change(s => s.DisplayMode = Wrap(s.DisplayMode + 1 + d, DisplayText.Length) - 1));
            RowLabel(page, 4, "해상도");
            resolutionValue = SettingsUi.Stepper(page, "Resolution", ControlX, 14 + 4 * RowH, ControlW, 44, StepResolution);
            RowLabel(page, 5, "프레임 제한");
            frameValue = SettingsUi.Stepper(page, "Frame Limit", ControlX, 14 + 5 * RowH, ControlW, 44,
                d => Change(s => s.FrameLimit = Wrap(s.FrameLimit + d, SettingsValues.FrameLimits.Length)));
            RowLabel(page, 6, "프레임 표시(FPS)");
            fpsToggle = SettingsUi.Toggle(page, "Show FPS", ControlX, 14 + 6 * RowH + 4, 36, v => Change(s => s.ShowFps = v));
        }

        private void BuildAudio(Transform page)
        {
            RowLabel(page, 0, "마이크 장치");
            micDeviceText = SettingsUi.Label(page, "Mic Device", ControlX, 14, ControlW + 60, 44, "", 20, SettingsUi.Light);
            Info(page, ControlX + ControlW + 70, 10, MicTip);
            RowLabel(page, 1, "출력 장치");
            SettingsUi.Label(page, "Output Device", ControlX, 14 + RowH, ControlW + 60, 44, "시스템 기본값", 20, SettingsUi.Light);
            Info(page, ControlX + ControlW + 70, 10 + RowH, OutputTip);
            RowLabel(page, 2, "마스터 볼륨");
            volumeSlider = SettingsUi.Slider(page, "Master Volume", ControlX, 14 + 2 * RowH, ControlW, 44, 0, 100, true,
                v => Change(s => s.MasterVolume = v / 100f));
            volumeValue = ValueLabel(page, 2);
            RowLabel(page, 3, "효과음/UI 볼륨");
            SettingsUi.Label(page, "Effects Volume", ControlX, 14 + 3 * RowH, ControlW, 44, "준비 중", 20, SettingsUi.LightA(.4f));
            RowLabel(page, 4, "마이크 레벨");
            var barBack = SettingsUi.At(page, "Mic Bar", ControlX, 14 + 4 * RowH + 12, ControlW, 20);
            SettingsUi.Fill(barBack, SettingsUi.LightA(.12f));
            micBar = SettingsUi.Rect(barBack, "Level", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, .5f), Vector2.zero, new Vector2(0, 0));
            SettingsUi.Fill(micBar, SettingsUi.AmberA(.85f));
            micGradeText = ValueLabel(page, 4);
            micGradeText.rectTransform.sizeDelta = new Vector2(230, 44);
            micGradeText.fontSize = 20;
        }

        private void BuildControls(Transform page)
        {
            RowLabel(page, 0, "마우스 감도");
            mouseSlider = SettingsUi.Slider(page, "Mouse", ControlX, 14, ControlW, 44, SettingsValues.MouseMin * 100f, SettingsValues.MouseMax * 100f, true,
                v => Change(s => s.MouseScale = v / 100f));
            mouseValue = ValueLabel(page, 0);
            RowLabel(page, 1, "Y축 반전");
            invertToggle = SettingsUi.Toggle(page, "Invert Y", ControlX, 14 + RowH + 4, 36, v => Change(s => s.InvertY = v));
        }

        private void Info(Transform page, float x, float y, string message)
        {
            var mark = SettingsUi.Label(page, "Info", x, y, 30, 30, "ⓘ", 20, SettingsUi.Amber, TextAnchor.MiddleCenter);
            mark.raycastTarget = true;
            var trigger = mark.gameObject.AddComponent<SettingsTooltipTrigger>();
            trigger.Message = message;
            trigger.Show = ShowTooltip;
            trigger.Hide = HideTooltip;
        }

        private void BuildConfirm(Transform parent)
        {
            var panel = Centered(parent, "Confirm", 620, 260);
            SettingsUi.Fill(panel, SettingsUi.BlackA(.95f), true);
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = SettingsUi.AmberA(.6f); outline.effectDistance = new Vector2(2, -2);
            confirmText = SettingsUi.Label(panel, "Message", 30, 24, 560, 120, "", 24, SettingsUi.Light, TextAnchor.MiddleCenter);
            confirmOk = SettingsUi.Button(panel, "OK", 80, 170, 210, 60, "확인", 24, ConfirmAccepted);
            SettingsUi.Button(panel, "Cancel", 330, 170, 210, 60, "취소", 24, () => ShowConfirm(Confirm.None));
            confirmPanel = panel.gameObject;
            confirmPanel.SetActive(false);
        }

        private void BuildTooltip(Transform parent)
        {
            var panel = SettingsUi.Rect(parent, "Tooltip", new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(0, 1), Vector2.zero, new Vector2(460, 120));
            SettingsUi.Fill(panel, SettingsUi.BlackA(.95f));
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = SettingsUi.LightA(.5f); outline.effectDistance = new Vector2(1, -1);
            tooltipText = SettingsUi.Label(panel, "Text", 14, 10, 432, 100, "", 18, SettingsUi.Light, TextAnchor.UpperLeft);
            tooltip = panel.gameObject;
            tooltip.SetActive(false);
        }

        private void ShowTooltip(string message, RectTransform near)
        {
            if (tooltip == null || near == null) return;
            tooltipText.text = message;
            var rt = (RectTransform)tooltip.transform;
            rt.SetAsLastSibling();
            rt.position = near.position + new Vector3(near.rect.width * near.lossyScale.x, 0, 0);
            tooltip.SetActive(true);
        }

        private void HideTooltip()
        {
            if (tooltip != null) tooltip.SetActive(false);
        }

        // ------------------------------------------------------------------ confirm (방 나가기 / 게임 종료)

        private void ShowConfirm(Confirm next)
        {
            confirm = next;
            if (confirmPanel == null) return;
            confirmPanel.SetActive(next != Confirm.None);
            if (next == Confirm.None) return;
            confirmPanel.transform.SetAsLastSibling();
            confirmOk.interactable = true;
            if (next == Confirm.Quit) confirmText.text = "게임을 종료할까요?";
            else confirmText.text = connector != null && connector.IsHost
                ? "방을 나갈까요?\n호스트가 나가면 모두 세션이 종료됩니다."
                : "방을 나갈까요?";
        }

        private void ConfirmAccepted()
        {
            // One press only: the button is off before anything else happens.
            if (confirmOk == null || !confirmOk.interactable) return;
            confirmOk.interactable = false;
            var what = confirm;
            try
            {
                if (what == Confirm.Leave)
                {
                    var target = connector != null ? connector : FindAnyObjectByType<LanConnector>();
                    CloseAll();
                    // The same path as the HUD's "연결 끊기" button.
                    if (target != null) target.Leave();
                }
                else if (what == Confirm.Quit)
                {
                    SettingsRuntime.Save();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            }
            catch (Exception exception) { SettingsRuntime.Report("confirm", exception); CloseAll(); }
        }

        // ------------------------------------------------------------------ values

        private void SelectTab(int index)
        {
            tab = Mathf.Clamp(index, 0, tabs.Length - 1);
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] != null) tabs[i].SetActive(i == tab);
                if (tabButtons[i] != null) tabButtons[i].color = i == tab ? SettingsUi.AmberA(.55f) : SettingsUi.LightA(.24f);
            }
            HideTooltip();
            if (tab == 1) { RefreshMicDevice(); shownGrade = -2; }
        }

        private void Change(Action<SettingsValues> edit)
        {
            if (building || SettingsRuntime.Current == null) return;
            try
            {
                edit(SettingsRuntime.Current);
                SettingsRuntime.ApplyAll(false);
                fovApplied = float.NaN;
                RefreshLabels();
                dirty = true;
                saveAt = Time.unscaledTime + .5f;
            }
            catch (Exception exception) { SettingsRuntime.Report("change", exception); }
        }

        private void RestoreDefaults()
        {
            SettingsRuntime.Replace(new SettingsValues());
            fovApplied = float.NaN;
            SyncWidgets();
            SettingsRuntime.Save();
            dirty = false;
        }

        private void SyncWidgets()
        {
            var s = SettingsRuntime.Current;
            if (s == null) return;
            building = true;
            try
            {
                fovSlider.SetValueWithoutNotify(s.Fov);
                brightnessSlider.SetValueWithoutNotify(Mathf.Round(s.Brightness * 100f));
                colorBlindToggle.SetIsOnWithoutNotify(s.ColorBlind);
                fpsToggle.SetIsOnWithoutNotify(s.ShowFps);
                volumeSlider.SetValueWithoutNotify(Mathf.Round(s.MasterVolume * 100f));
                mouseSlider.SetValueWithoutNotify(Mathf.Round(s.MouseScale * 100f));
                invertToggle.SetIsOnWithoutNotify(s.InvertY);
                resolutions = DistinctResolutions();
                resolutionIndex = -1;
                for (int i = 0; i < resolutions.Length; i++)
                    if (resolutions[i].width == s.ResolutionWidth && resolutions[i].height == s.ResolutionHeight) resolutionIndex = i;
                RefreshLabels();
            }
            finally { building = false; }
        }

        private void RefreshLabels()
        {
            var s = SettingsRuntime.Current;
            fovValue.text = Mathf.RoundToInt(s.Fov) + "°";
            brightnessValue.text = s.Brightness.ToString("0.00");
            volumeValue.text = Mathf.RoundToInt(s.MasterVolume * 100f) + "%";
            mouseValue.text = s.MouseScale.ToString("0.00");
            displayValue.text = DisplayText[Mathf.Clamp(s.DisplayMode + 1, 0, DisplayText.Length - 1)];
            frameValue.text = FrameText[Mathf.Clamp(s.FrameLimit, 0, FrameText.Length - 1)];
            resolutionValue.text = s.ResolutionWidth > 0 ? s.ResolutionWidth + " × " + s.ResolutionHeight : "그대로(" + Screen.width + " × " + Screen.height + ")";
            fpsText.gameObject.SetActive(s.ShowFps);
        }

        private void StepResolution(int direction)
        {
            if (resolutions.Length == 0) resolutions = DistinctResolutions();
            if (resolutions.Length == 0) return;
            resolutionIndex = Wrap(resolutionIndex + direction, resolutions.Length);
            var r = resolutions[resolutionIndex];
            Change(s => { s.ResolutionWidth = r.width; s.ResolutionHeight = r.height; });
        }

        private static Resolution[] DistinctResolutions()
        {
            var all = Screen.resolutions;
            var list = new System.Collections.Generic.List<Resolution>(all.Length);
            foreach (var r in all)
            {
                bool seen = false;
                foreach (var have in list) if (have.width == r.width && have.height == r.height) { seen = true; break; }
                if (!seen) list.Add(r);
            }
            return list.ToArray();
        }

        private static int Wrap(int value, int count) => count <= 0 ? 0 : ((value % count) + count) % count;

        // ------------------------------------------------------------------ FOV (horizontal → vertical)

        private void TrackFov()
        {
            try
            {
                var local = NetworkPlayer.Local;
                if (local != fovPlayer)
                {
                    fovPlayer = local;
                    fovCamera = null;
                    fovAspect = -1f;
                    fovApplied = float.NaN;
                    if (local != null)
                    {
                        // The player's own first-person camera only (imported model cameras are switched off and skipped).
                        foreach (var camera in local.GetComponentsInChildren<Camera>(true))
                            if (camera.name == "PlayerCamera") { fovCamera = camera; break; }
                        if (fovCamera != null) fovPrefab = fovCamera.fieldOfView;
                    }
                }
                if (fovCamera == null) { if (!ReferenceEquals(fovCamera, null)) { fovCamera = null; fovPlayer = null; } return; }
                float aspect = fovCamera.aspect;
                if (aspect == fovAspect && !float.IsNaN(fovApplied)) return;
                fovAspect = aspect;
                var s = SettingsRuntime.Current;
                if (s != null && SettingsRuntime.TryVerticalFov(s.Fov, aspect, out float vertical))
                {
                    fovCamera.fieldOfView = vertical;
                    fovApplied = vertical;
                }
                else
                {
                    fovCamera.fieldOfView = fovPrefab;
                    fovApplied = fovPrefab;
                }
            }
            catch (Exception exception) { SettingsRuntime.Report("fov", exception); fovApplied = 0f; }
        }

        // ------------------------------------------------------------------ microphone meter (read only)

        private void TickMicrophone()
        {
            try
            {
                var local = NetworkPlayer.Local;
                if (local != micPlayer)
                {
                    micPlayer = local;
                    micController = local != null ? local.GetComponentInChildren<MicrophoneVisionController>(true) : null;
                }
                int grade = -1;
                float fill = 0f;
                if (micController != null && micController.HasMicrophoneInput)
                {
                    var meter = micController.LastMeter;
                    grade = Mathf.Clamp(meter.level, 0, 3);
                    fill = Mathf.InverseLerp(-60f, 0f, meter.dbfs);   // display scale only, not a grading threshold
                }
                micBar.sizeDelta = new Vector2(ControlW * fill, 0f);
                if (grade != shownGrade)
                {
                    shownGrade = grade;
                    micGradeText.text = GradeText[grade + 1];
                }
                if (Time.unscaledTime >= nextDeviceCheck) RefreshMicDevice();
            }
            catch (Exception exception) { SettingsRuntime.Report("microphone", exception); }
        }

        // The device actually recording (Microphone.devices[0] is not guaranteed to be Windows' default).
        private void RefreshMicDevice()
        {
            nextDeviceCheck = Time.unscaledTime + 1f;
            try
            {
                var devices = Microphone.devices;
                string text;
                if (devices.Length == 0) text = "마이크 없음";
                else
                {
                    string using_ = null;
                    foreach (var d in devices) if (Microphone.IsRecording(d)) { using_ = d; break; }
                    text = using_ != null ? "시스템 기본값 (" + using_ + ")" : "시스템 기본값 (사용 중인 장치 없음)";
                }
                if (micDeviceText.text != text) micDeviceText.text = text;
            }
            catch (Exception exception) { SettingsRuntime.Report("micDevice", exception); }
        }

        // ------------------------------------------------------------------ FPS

        private static string[] MakeFpsText()
        {
            var array = new string[1001];
            for (int i = 0; i < array.Length; i++) array[i] = "FPS " + i;
            return array;
        }

        private void TickFps()
        {
            var s = SettingsRuntime.Current;
            if (s == null || !s.ShowFps || fpsText == null) return;
            fpsFrames++;
            fpsTime += Time.unscaledDeltaTime;
            if (fpsTime < .5f) return;
            int fps = Mathf.Clamp(Mathf.RoundToInt(fpsFrames / fpsTime), 0, 1000);
            fpsFrames = 0; fpsTime = 0f;
            if (fps == shownFps) return;
            shownFps = fps;
            fpsText.text = FpsText[fps];
        }
    }
}
