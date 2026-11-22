using Marco.Network.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Network.Lobby
{
    // 표시 전용. Displays the separately authored Lobby_CCTV scene and listens to voice activity.
    [DisallowMultipleComponent]
    public sealed class LobbyCctvBackdrop : MonoBehaviour
    {
        private LobbyMenu menu;
        private LanConnector connector;
        private GameObject stage;
        private Camera cameraFeed;
        private RenderTexture feed;
        private Texture2D noise;
        private float speech, desiredSpeech, speechUntil, retryAt;
        private AudioClip previewMic;
        private string device;
        private readonly float[] micFrame = new float[320];
        private int lastMicPosition, speechFrames;
        private string microphoneStatus = "";
        private GUIStyle telemetry;
        private bool active;

        private void Awake()
        {
            menu = GetComponent<LobbyMenu>();
            connector = GetComponent<LanConnector>();
        }

        private void OnEnable() => LobbyVoiceActivity.Heard += OnVoice;

        private void OnVoice(float level)
        {
            if (!active) return;
            desiredSpeech = level;
            speechUntil = Time.unscaledTime + .14f;
        }

        private void Update()
        {
            active = menu != null && menu.ShowsBackdrop;
            if (stage == null) TryBuildStage();
            if (stage != null) stage.SetActive(active);
            if (cameraFeed != null) cameraFeed.enabled = active;
            if (!active)
            {
                ReleaseMicrophone();
                speech = desiredSpeech = 0;
                return;
            }
            UpdatePreviewMicrophone();
            float target = Time.unscaledTime < speechUntil ? desiredSpeech : 0;
            speech = Mathf.Lerp(speech, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime / (target > speech ? .2f : .3f)));
            if (sceneStage != null) sceneStage.SetSpeech(speech);
        }

        private bool loading, ownsScene;
        private LobbyCctvStage sceneStage;
        private const string BackdropScene = "Assets/Scenes/Lobby/Lobby_CCTV.unity";

        private void TryBuildStage()
        {
            if (loading) return;
            var existing = SceneManager.GetSceneByPath(BackdropScene);
            if (existing.isLoaded) { AttachScene(existing); return; }
            if (!Application.CanStreamedLevelBeLoaded(BackdropScene)) return;
            loading = true;
            ownsScene = true;
            var request = SceneManager.LoadSceneAsync(BackdropScene, LoadSceneMode.Additive);
            request.completed += _ =>
            {
                var loaded = SceneManager.GetSceneByPath(BackdropScene);
                if (this == null) { if (loaded.isLoaded) SceneManager.UnloadSceneAsync(loaded); return; }
                loading = false;
                AttachScene(loaded);
            };
        }

        private void AttachScene(Scene loaded)
        {
            foreach (GameObject root in loaded.GetRootGameObjects())
                if (root.TryGetComponent<LobbyCctvStage>(out var found)) { sceneStage = found; break; }
            if (sceneStage == null) return;
            stage = sceneStage.gameObject;
            cameraFeed = sceneStage.feedCamera;
            feed = new RenderTexture(960, 540, 24) { name = "Lobby CCTV feed", filterMode = FilterMode.Bilinear };
            feed.Create();
            cameraFeed.targetTexture = feed;
            cameraFeed.enabled = active && isActiveAndEnabled;
            stage.SetActive(active && isActiveAndEnabled);
            noise = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var pixels = new Color[128 * 128];
            var random = new System.Random(73);
            for (int i = 0; i < pixels.Length; i++) { float n = (float)random.NextDouble(); pixels[i] = new Color(n, n, n, .045f); }
            noise.SetPixels(pixels);
            noise.Apply(false, true);
        }

        private void UpdatePreviewMicrophone()
        {
            // A connected player's existing VAD/voice stream supplies the level. Never
            // open a second device or stop the new player's microphone during handoff.
            if (NetworkPlayer.Local != null || connector.Status == LanConnectionStatus.Connecting)
            {
                ReleaseMicrophone();
                microphoneStatus = Input.GetKey(KeyCode.LeftAlt) ? "MIC MUTED" : "VOICE LINK ACTIVE";
                return;
            }
            if (previewMic == null && Time.unscaledTime >= retryAt)
            {
                retryAt = Time.unscaledTime + 2;
                microphoneStatus = "MIC UNAVAILABLE";
                if (Microphone.devices.Length == 0) return;
                device = Microphone.devices[0];
                if (Microphone.IsRecording(device)) return;
                try { previewMic = Microphone.Start(device, true, 1, 16000); }
                catch (System.Exception) { previewMic = null; }
                lastMicPosition = 0;
            }
            if (previewMic == null) return;
            if (!Microphone.IsRecording(device)) { ReleaseMicrophone(); return; }
            microphoneStatus = "MIC ACTIVE";
            int position = Microphone.GetPosition(device);
            if (Input.GetKey(KeyCode.LeftAlt)) { speechFrames = 0; lastMicPosition = position; microphoneStatus = "MIC MUTED"; return; }
            int available = (position - lastMicPosition + previewMic.samples) % previewMic.samples;
            if (available < micFrame.Length) return;
            int offset = (position - micFrame.Length + previewMic.samples) % previewMic.samples;
            previewMic.GetData(micFrame, offset);
            lastMicPosition = position;
            double energy = 0;
            foreach (float value in micFrame) energy += value * value;
            speechFrames = energy / micFrame.Length > .00001 ? speechFrames + 1 : 0;
            if (speechFrames >= 3) LobbyVoiceActivity.Publish(micFrame);
        }

        private void ReleaseMicrophone()
        {
            if (previewMic == null) return;
            if (NetworkPlayer.Local == null && !string.IsNullOrEmpty(device)) Microphone.End(device);
            Destroy(previewMic);
            previewMic = null;
            speechFrames = 0;
        }

        public void Draw(Rect rect)
        {
            var previousColor = GUI.color;
            GUI.color = Color.white;
            if (feed != null) GUI.DrawTexture(rect, feed, ScaleMode.ScaleAndCrop);
            else TestHudSkin.Fill(rect, new Color(.015f, .025f, .025f));
            if (noise != null)
            {
                float tick = Mathf.Floor(Time.unscaledTime * 12);
                GUI.DrawTextureWithTexCoords(rect, noise, new Rect(tick * .13f, tick * .31f, 8, 5));
            }
            for (float y = 0; y < rect.height; y += 5) TestHudSkin.Fill(new Rect(0, y, rect.width, 1), new Color(0, 0, 0, .16f));
            // Darken the menu edge, leaving the subject and water readable on the right.
            for (int i = 0; i < 24; i++) TestHudSkin.Fill(new Rect(i * rect.width / 48, 0, rect.width / 48 + 1, rect.height), new Color(0, 0, 0, .65f * (1 - i / 24f)));
            TestHudSkin.Fill(new Rect(0, 0, rect.width, 55), new Color(0, 0, 0, .65f));
            TestHudSkin.Fill(new Rect(0, rect.height - 55, rect.width, 55), new Color(0, 0, 0, .65f));
            telemetry ??= new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold };
            telemetry.normal.textColor = new Color(.65f, .77f, .71f);
            GUI.Label(new Rect(28, 12, 520, 30), "CAM 03  /  INDOOR POOL  /  LIVE", telemetry);
            GUI.Label(new Rect(rect.width - 350, 12, 330, 30), System.DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss"), telemetry);
            GUI.Label(new Rect(28, rect.height - 42, 600, 32), feed == null ? "CONNECTING CAMERA..." : microphoneStatus + "  /  HOLD LEFT ALT TO MUTE", telemetry);
            GUI.Label(new Rect(rect.width - 270, rect.height - 42, 240, 32), speech > .08f ? "VOICE DETECTED" : "AUDIO MONITOR", telemetry);
            GUI.color = previousColor;
        }

        private void OnDisable()
        {
            LobbyVoiceActivity.Heard -= OnVoice;
            ReleaseMicrophone();
            if (stage != null) stage.SetActive(false);
        }

        private void OnDestroy()
        {
            if (cameraFeed != null) cameraFeed.targetTexture = null;
            if (feed != null) { feed.Release(); Destroy(feed); }
            if (noise != null) Destroy(noise);
            if (ownsScene && stage != null && stage.scene.isLoaded) SceneManager.UnloadSceneAsync(stage.scene);
        }
    }
}
