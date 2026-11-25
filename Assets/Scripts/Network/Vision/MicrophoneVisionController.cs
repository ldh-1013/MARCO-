using System;
using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Voice;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Vision
{
    // Microphone-driven darkness/light, owner only: it sits on the player camera, which only the
    // owner activates. Samples never leave this client (step 5 voice chat reads VoiceSamplesReady).
    // Copied from Prototype MicrophoneVisionController; only the foot dot shader is now a reference.
    public sealed class MicrophoneVisionController : MonoBehaviour
    {
        private const int SampleRate = 48000;
        private const int FrameSamples = 960; // 20 ms at 48 kHz.
        private const int RequiredConsecutiveFrames = 3;
        private const int VoiceHangoverFrames = 8; // Bridge brief level dips without transmitting indefinitely.
        private const int VoicePreRollFrames = 2; // Keep the first 40 ms of speech before VAD confirms it.
        private const float ClickerCameraFlashSeconds = 0.6f;
        private const float ClickerCameraFlashRadius = ClickerSpec.Radius;   // 부록 A [아이템]
        private const float ClickerAfterglowSeconds = 12f;
        private const float ClickerAfterglowPeakOpacity = 0.15f;
        private static readonly int ClickerTerrainId = Shader.PropertyToID("_MarcoClickerTerrain");
        private AudioClip microphoneClip;
        private string microphoneDevice;
        private readonly float[] sampleBuffer = new float[FrameSamples];
        private readonly float[][] voicePreRollBuffers =
        {
            new float[FrameSamples],
            new float[FrameSamples]
        };
        private float[] microphoneReadBuffer;
        private int voicePreRollWriteIndex;
        private int voicePreRollCount;
        private bool voicePacketBurstActive;
        // Raw 20 ms microphone frame and its loudness grade, only while speech is detected (voice chat, step 5).
        public event Action<float[], VoiceGrade> VoiceSamplesReady;
        // Consumed synchronously; this buffer is reused. No callback is raised while muted.
        public event Action<float[]> MicrophoneSamplesReady;
        // Read only, for the settings window's level meter: the last frame's dBFS and its level (0 silence … 3 shout).
        public (float dbfs, int level) LastMeter { get; private set; } = (float.NegativeInfinity, 0);
        public bool HasMicrophoneInput => microphoneClip != null && !string.IsNullOrEmpty(microphoneDevice) && Microphone.IsRecording(microphoneDevice);

        // 5.2 ③ · 부록 A [마이크 캘리브레이션]: personal offset on the −30 / −15 grade boundaries only (0 = the fixed
        // thresholds). Not applied to the −50 voice floor or to the vision radius below. Saving it is the lobby's job.
        private float calibrationOffsetDb;
        private MicCalibrationSession calibration;
        private Action<MicCalibration.Result, bool> calibrationDone;
        private Action calibrationCancelled;
        private bool calibrationMuted;
        public float CalibrationOffsetDb => calibrationOffsetDb;
        public bool IsCalibrating => calibration != null;
        public string MicrophoneDevice => microphoneDevice;
        // 0 … 1 through the current prompt (150 frames or 3 s, whichever is further along); 0 when not measuring.
        public float CalibrationProgress => calibration != null ? calibration.Progress(Time.unscaledTime) : 0f;
        public void SetCalibrationOffset(float offsetDb) => calibrationOffsetDb = float.IsNaN(offsetDb) ? 0f : MicCalibration.ClampOffset(offsetDb);
        // The level this controller gives a frame of this loudness (0 silence … 3 shout).
        private float? calibratedVoiceFloor;
        public void SetVoiceFloor(float? floor) => calibratedVoiceFloor = floor;

        public float VoiceFloorFor(MicCalibration.Result result) =>
            MicCalibration.VoiceFloorFor(result, silenceDb);

        public int GradeLevel(float dbfs)
        {
            if (calibratedVoiceFloor.HasValue && dbfs < calibratedVoiceFloor.Value) return 0;
            return MicCalibration.Grade(dbfs, calibrationOffsetDb, calibratedVoiceFloor ?? silenceDb);
        }

        // One 3 s prompt: every processed frame is also recorded until 150 frames or 3 s, then onComplete gets the
        // result once, with whether Left Alt (mute) was held during it. The frame keeps its normal outputs (voice,
        // rings, meter) — measuring only watches them. Cut short (this controller disabled): onCancelled instead.
        public void BeginCalibration(Action<MicCalibration.Result, bool> onComplete, Action onCancelled = null)
        {
            calibration = new MicCalibrationSession(Time.unscaledTime);
            calibrationDone = onComplete;
            calibrationCancelled = onCancelled;
            calibrationMuted = false;
        }

        // Stops the prompt without reporting anything (the caller cancelled it).
        public void CancelCalibration()
        {
            calibration = null;
            calibrationDone = null;
            calibrationCancelled = null;
        }

        private void FinishCalibrationIfDue()
        {
            if (calibration == null) return;
            if (Input.GetKey(KeyCode.LeftAlt)) calibrationMuted = true;
            if (!calibration.IsDue(Time.unscaledTime)) return;
            var result = calibration.Complete();
            var done = calibrationDone;
            bool muted = calibrationMuted;
            CancelCalibration();
            done?.Invoke(result, muted);
        }

        private void AbortCalibration()
        {
            if (calibration == null) return;
            var cancelled = calibrationCancelled;
            CancelCalibration();
            cancelled?.Invoke();
        }

        [Header("Brightness implementation")]
        [SerializeField] private BrightnessMode brightnessMode = BrightnessMode.MarcoMinus;

        [Header("MarcoMinus · world lighting")]
        [SerializeField] private Color worldLightColor = Color.white;
        [SerializeField, Min(-120f)] private float silenceFloorDb = -60f;
        [SerializeField, Min(-59f)] private float shoutCeilDb = -5f;
        [SerializeField, Min(0.01f)] private float levelCurvePower = 1.1f;
        [SerializeField, Min(0.01f)] private float radiusMin = 3f;
        [SerializeField, Min(0.01f)] private float radiusMax = 42f;
        [SerializeField, Min(0f)] private float intensityMin = 0.25f;
        [SerializeField, Min(0f)] private float intensityMax = 2.5f;
        [SerializeField, Min(0.1f)] private float falloffPower = 1.7f;
        [SerializeField, Min(0.01f)] private float brightenTimeConstant = 0.2f;
        [SerializeField, Min(0.01f)] private float darkenTimeConstant = 0.3f;
        [Header("Microphone dots under feet")]
        [SerializeField] private bool showMicrophoneFootDots = false;
        // A reference (not Shader.Find) so builds include it.
        [SerializeField] private Shader microphoneFootDotShader;
        [SerializeField] private Color microphoneFootDotColor = new Color(0.72f, 0.9f, 1f, 1f);
        [SerializeField, Min(0.02f)] private float microphoneFootDotRadius = 0.25f;
        [SerializeField, Min(0.1f)] private float microphoneFootDotLifetime = 5f;
        [SerializeField, Min(0.1f)] private float microphoneFootDotInterval = 2f;
        private int microphoneReadPosition;
        private int consecutiveVoiceFrames;
        private int voiceHangoverFramesRemaining;
        private VoiceGrade lastVoiceGrade = VoiceGrade.Whisper;
        private float revealUntil;
        private bool clickerFlashActive;
        private float clickerFlashStartedAt = float.NegativeInfinity;
        private float clickerFlashUntil;
        private Vector3 clickerFlashOrigin;
        private TerrainAfterglow terrainAfterglow;
        private readonly Dictionary<Renderer, bool> clickerHiddenRenderers = new();
        private MaterialPropertyBlock clickerVisibilityProperties;
        private float clickerVisibilityUntil;
        private int clickerFlashSourcePlayerId = -1;
        [Header("MARCO+ · screen-space mask")]
        [SerializeField] private float silenceDb = -50f;
        [SerializeField] private float fullVisionDb = -15f;
        [SerializeField, Range(0.1f, 1f)] private float maximumRadius = 0.85f;
        [SerializeField] private float closeSpeed = 2f;
        private float radius;
        private float targetRadius;
        private Texture2D mask;
        private readonly Color32[] maskPixels = new Color32[256 * 256];
        private float drawnRadius = -1;
        private float drawnAspect;
        private bool developmentFullVision;
        private static MicrophoneVisionController worldLightingOwner;
        private Camera visionCamera;
        private BrightnessMode appliedBrightnessMode;
        private bool brightnessModeInitialized;
        private float targetWorldRadius;
        private float targetWorldIntensity;
        private float worldRadius;
        private float worldIntensity;
        private MicrophoneFootDots microphoneFootDots;
        private float nextFootDotTime;
        private LightmapData[] savedLightmaps;
        private RenderSettingsSnapshot savedRenderSettings;
        private bool environmentCaptured;
        private readonly System.Collections.Generic.List<RendererLightingSnapshot> savedRenderers = new();
        private readonly System.Collections.Generic.List<CameraSnapshot> savedCameras = new();
        private readonly System.Collections.Generic.List<LightSnapshot> savedLights = new();

        private static readonly int WorldModeId = Shader.PropertyToID("_MarcoMinusEnabled");
        private static readonly int PlayerPositionId = Shader.PropertyToID("_MarcoPlayerPosition");
        private static readonly int LightRadiusId = Shader.PropertyToID("_MarcoLightRadius");
        private static readonly int LightIntensityId = Shader.PropertyToID("_MarcoLightIntensity");
        private static readonly int FalloffPowerId = Shader.PropertyToID("_MarcoFalloffPower");
        private static readonly int LightColorId = Shader.PropertyToID("_MarcoLightColor");
        private static readonly int ClickerEnabledId = Shader.PropertyToID("_MarcoClickerEnabled");
        private static readonly int ClickerPositionId = Shader.PropertyToID("_MarcoClickerPosition");
        private static readonly int ClickerRadiusId = Shader.PropertyToID("_MarcoClickerRadius");
        private static readonly int ClickerIntensityId = Shader.PropertyToID("_MarcoClickerIntensity");
        private static readonly int ClickerFalloffPowerId = Shader.PropertyToID("_MarcoClickerFalloffPower");
        private static readonly int ClickerColorId = Shader.PropertyToID("_MarcoClickerColor");

        private struct RendererLightingSnapshot
        {
            public Renderer renderer;
            public LightProbeUsage lightProbeUsage;
            public ReflectionProbeUsage reflectionProbeUsage;
            public int lightmapIndex;
            public Vector4 lightmapScaleOffset;
        }

        private struct CameraSnapshot
        {
            public Camera camera;
            public CameraClearFlags clearFlags;
            public Color backgroundColor;
        }

        private struct LightSnapshot
        {
            public Light light;
            public bool enabled;
        }

        private struct RenderSettingsSnapshot
        {
            public AmbientMode ambientMode;
            public Color ambientLight, ambientSky, ambientEquator, ambientGround;
            public float ambientIntensity, reflectionIntensity;
            public Material skybox;
            public Texture customReflection;
            public DefaultReflectionMode reflectionMode;
        }

        private bool IsFullVisionActive => developmentFullVision;
        public bool IsCenterVisible => IsFullVisionActive || clickerFlashActive || radius > 0.01f;
        internal float HudVisionLevel
        {
            get
            {
                if (IsFullVisionActive || clickerFlashActive) return 1f;
                return brightnessMode == BrightnessMode.MarcoPlus
                    ? Mathf.Clamp01(radius / Mathf.Max(.001f, maximumRadius))
                    : Mathf.Clamp01(worldIntensity / Mathf.Max(.001f, intensityMin * 4f));
            }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool DevelopmentFullVision => developmentFullVision;
#endif

        private void Start()
        {
            developmentFullVision = false;
            visionCamera = GetComponent<Camera>() ?? GetComponentInChildren<Camera>(true);
            terrainAfterglow = GetComponent<TerrainAfterglow>() ?? gameObject.AddComponent<TerrainAfterglow>();
            if (showMicrophoneFootDots)
            {
                microphoneFootDots = GetComponent<MicrophoneFootDots>();
                if (microphoneFootDots == null) microphoneFootDots = gameObject.AddComponent<MicrophoneFootDots>();
                int dotPoolSize = Mathf.CeilToInt(microphoneFootDotLifetime / microphoneFootDotInterval) + 1;
                microphoneFootDots.Configure(microphoneFootDotShader, microphoneFootDotColor, microphoneFootDotRadius, microphoneFootDotLifetime, dotPoolSize);
            }
            if (worldLightingOwner == null) worldLightingOwner = this;
            if (worldLightingOwner == this)
            {
                CaptureEnvironment();
                ApplyBrightnessMode();
            }
            else
            {
                appliedBrightnessMode = brightnessMode;
                brightnessModeInitialized = true;
            }
            // Development full vision is opt-in through F8. Starting with it
            // enabled bypasses the dark state and makes silence look like light.
            if (Microphone.devices.Length == 0) return;
            microphoneDevice = Microphone.devices[0]; microphoneClip = Microphone.Start(microphoneDevice, true, 1, SampleRate);
            if (microphoneClip != null)
                microphoneReadBuffer = new float[FrameSamples * Mathf.Max(1, microphoneClip.channels)];
            // 부록 A [마이크 캘리브레이션] 저장: this device's saved offset (none: 0 — the lobby asks for a prompt).
            var savedCalibration = MicCalibrationStore.ForPlayerPrefs();
            SetCalibrationOffset(savedCalibration.Load(microphoneDevice) ?? 0f);
            SetVoiceFloor(savedCalibration.LoadVoiceFloor(microphoneDevice));
        }

        private void Update()
        {
            if (clickerFlashActive && Time.unscaledTime >= clickerFlashUntil)
            {
                clickerFlashActive = false;
            }
            UpdateClickerVisibilitySuppression();

            if (worldLightingOwner == null)
            {
                worldLightingOwner = this;
                CaptureEnvironment();
                brightnessModeInitialized = false;
            }
            if (worldLightingOwner == this)
            {
                if (!brightnessModeInitialized || brightnessMode != appliedBrightnessMode)
                    ApplyBrightnessMode();
            }

            if (microphoneClip != null)
            {
                if (!Input.GetKey(KeyCode.LeftAlt)) ProcessAvailableMicrophoneFrames();
                else
                {
                    int mutePosition = Microphone.GetPosition(microphoneDevice);
                    if (mutePosition >= 0) microphoneReadPosition = mutePosition;
                }
            }
            if (Input.GetKey(KeyCode.LeftAlt))
            {
                targetRadius = radius = 0;
                targetWorldRadius = targetWorldIntensity = 0f;
                consecutiveVoiceFrames = 0;
                voiceHangoverFramesRemaining = 0;
                ClearVoicePreRoll();
            }
            else
            {
                if (brightnessMode == BrightnessMode.MarcoPlus && Time.unscaledTime > revealUntil)
                    targetRadius = 0;
            }
            FinishCalibrationIfDue();
            radius = Mathf.MoveTowards(radius, targetRadius, Time.unscaledDeltaTime * closeSpeed);
            if (worldLightingOwner == this)
            {
                SmoothWorldLighting(Time.unscaledDeltaTime);
                PushWorldLighting();
            }
            UpdateClickerFlashShader();
        }

        private void OnDestroy()
        {
            if (!string.IsNullOrEmpty(microphoneDevice) && Microphone.IsRecording(microphoneDevice)) Microphone.End(microphoneDevice);
            if (mask != null) Destroy(mask);
            RestoreClickerVisibilitySuppression();
            if (worldLightingOwner == this) SetClickerFlashShader(0f);
            terrainAfterglow?.ClearAll();
            RestoreEnvironment();
        }

        private void OnDisable()
        {
            AbortCalibration();   // a prompt cut short reports a cancel, not a result (it does not use an attempt)
            clickerFlashActive = false;
            clickerFlashStartedAt = float.NegativeInfinity;
            clickerFlashUntil = 0f;
            RestoreClickerVisibilitySuppression();
            if (worldLightingOwner != this) return;
            SetClickerFlashShader(0f);
            terrainAfterglow?.ClearAll();
            RestoreEnvironment();
            worldLightingOwner = null;
            brightnessModeInitialized = false;
        }

        private void ProcessFrame()
        {
            int channels = Mathf.Max(1, microphoneClip.channels);
            for (int i = 0; i < FrameSamples; i++)
            {
                float monoSample = 0f;
                for (int channel = 0; channel < channels; channel++)
                    monoSample += microphoneReadBuffer[i * channels + channel];
                sampleBuffer[i] = monoSample / channels;
            }
            MicrophoneSamplesReady?.Invoke(sampleBuffer);
            double squares = 0; for (var i = 0; i < sampleBuffer.Length; i++) squares += sampleBuffer[i] * sampleBuffer[i];
            var dbfs = 20f * Mathf.Log10(Mathf.Max(Mathf.Sqrt((float)(squares / sampleBuffer.Length)), 0.000001f));
            calibration?.Add(dbfs, MicCalibration.IsClippedFrame(microphoneReadBuffer, FrameSamples * channels));
            var level = GradeLevel(dbfs);   // 5.2 ⑥ — boundaries moved by the calibration offset (0 until the lobby sets one)
            LastMeter = (dbfs, level);
            consecutiveVoiceFrames = level == 0 ? 0 : consecutiveVoiceFrames + 1;
            bool recognizedVoice = level > 0 && consecutiveVoiceFrames >= RequiredConsecutiveFrames;
            VoiceGrade voiceGrade = level == 3 ? VoiceGrade.Shout : level == 2 ? VoiceGrade.Talk : VoiceGrade.Whisper;
            if (recognizedVoice)
            {
                if (!voicePacketBurstActive) SendVoicePreRoll(voiceGrade);
                voicePacketBurstActive = true;
                lastVoiceGrade = voiceGrade;
                voiceHangoverFramesRemaining = VoiceHangoverFrames;
                VoiceSamplesReady?.Invoke(sampleBuffer, voiceGrade);
            }
            else if (voiceHangoverFramesRemaining > 0)
            {
                voiceHangoverFramesRemaining--;
                VoiceSamplesReady?.Invoke(sampleBuffer, lastVoiceGrade);
                if (voiceHangoverFramesRemaining == 0) voicePacketBurstActive = false;
            }
            else voicePacketBurstActive = false;
            CacheVoicePreRollFrame();
            if (brightnessMode == BrightnessMode.MarcoMinus && level > 0 && consecutiveVoiceFrames >= RequiredConsecutiveFrames
                && microphoneFootDots != null && Time.unscaledTime >= nextFootDotTime)
            {
                microphoneFootDots.SpawnDot(GetFootWorldPosition());
                nextFootDotTime = Time.unscaledTime + microphoneFootDotInterval;
            }
            if (brightnessMode == BrightnessMode.MarcoPlus)
            {
                if (consecutiveVoiceFrames >= RequiredConsecutiveFrames)
                    targetRadius = Mathf.InverseLerp(silenceDb, Mathf.Max(silenceDb + 1f, fullVisionDb), dbfs) * maximumRadius;
            }
            else if (level > 0)
            {
                float norm = Mathf.InverseLerp(silenceFloorDb, Mathf.Max(silenceFloorDb + 1f, shoutCeilDb), dbfs);
                norm = Mathf.Pow(norm, levelCurvePower);
                targetWorldRadius = Mathf.Lerp(radiusMin, Mathf.Max(radiusMin, radiusMax), norm);
                targetWorldIntensity = Mathf.Lerp(intensityMin, Mathf.Max(intensityMin, intensityMax), norm);
            }
            else
            {
                targetWorldRadius = 0f;
                targetWorldIntensity = 0f;
            }
            if (consecutiveVoiceFrames < RequiredConsecutiveFrames) return;
            revealUntil = Time.unscaledTime + 0.06f;
        }

        private void ProcessAvailableMicrophoneFrames()
        {
            int position = Microphone.GetPosition(microphoneDevice);
            if (position < 0 || microphoneClip == null) return;

            int availableSamples = position >= microphoneReadPosition
                ? position - microphoneReadPosition
                : microphoneClip.samples - microphoneReadPosition + position;

            // The 1-second microphone ring buffer was overwritten while the main thread stalled.
            // Resume from the current position instead of replaying stale or discontinuous samples.
            if (availableSamples > microphoneClip.samples - FrameSamples)
            {
                microphoneReadPosition = position;
                consecutiveVoiceFrames = 0;
                voiceHangoverFramesRemaining = 0;
                ClearVoicePreRoll();
                return;
            }

            while (availableSamples >= FrameSamples)
            {
                if (!microphoneClip.GetData(microphoneReadBuffer, microphoneReadPosition)) return;
                microphoneReadPosition = (microphoneReadPosition + FrameSamples) % microphoneClip.samples;
                availableSamples -= FrameSamples;
                ProcessFrame();
            }
        }

        private void SendVoicePreRoll(VoiceGrade grade)
        {
            int first = (voicePreRollWriteIndex - voicePreRollCount + VoicePreRollFrames) % VoicePreRollFrames;
            for (int i = 0; i < voicePreRollCount; i++)
                VoiceSamplesReady?.Invoke(voicePreRollBuffers[(first + i) % VoicePreRollFrames], grade);
        }

        private void CacheVoicePreRollFrame()
        {
            Array.Copy(sampleBuffer, voicePreRollBuffers[voicePreRollWriteIndex], FrameSamples);
            voicePreRollWriteIndex = (voicePreRollWriteIndex + 1) % VoicePreRollFrames;
            voicePreRollCount = Mathf.Min(voicePreRollCount + 1, VoicePreRollFrames);
        }

        private void ClearVoicePreRoll()
        {
            voicePreRollWriteIndex = 0;
            voicePreRollCount = 0;
            voicePacketBurstActive = false;
        }

        private Vector3 GetFootWorldPosition()
        {
            Vector3 position = visionCamera != null ? visionCamera.transform.position : transform.position;
            CharacterController characterController = GetComponentInParent<CharacterController>();
            if (characterController != null)
            {
                position.y = characterController.bounds.min.y + 0.025f;
                return position;
            }

            RaycastHit[] hits = Physics.RaycastAll(position, Vector3.down, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closestDistance = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform.root)) continue;
                if (hit.distance >= closestDistance) continue;
                closestDistance = hit.distance;
                position.y = hit.point.y + 0.025f;
            }
            if (closestDistance == float.MaxValue) position.y -= 1.6f;
            return position;
        }

        public void SetDevelopmentFullVision(bool enabled)
        {
            if (developmentFullVision == enabled) return;
            bool wasFullVision = IsFullVisionActive;
            developmentFullVision = enabled;
            bool isFullVision = IsFullVisionActive;
            if (wasFullVision != isFullVision) ApplyFullVisionOverride(isFullVision);
        }

        /// <summary>Briefly lights nearby terrain and leaves its outline, without generating sound.</summary>
        public void TriggerClickerCameraFlash()
        {
            Vector3 origin = visionCamera != null ? visionCamera.transform.position : transform.position;
            int sourcePlayerId = NetworkPlayer.Local != null ? NetworkPlayer.Local.PlayerId : -1;
            TriggerClickerCameraFlash(origin, sourcePlayerId);
        }

        public void TriggerClickerCameraFlash(Vector3 origin, int sourcePlayerId)
        {
            clickerFlashActive = true;
            clickerFlashStartedAt = Time.unscaledTime;
            clickerFlashUntil = clickerFlashStartedAt + ClickerCameraFlashSeconds;
            clickerFlashOrigin = origin;
            if (terrainAfterglow == null)
                terrainAfterglow = GetComponent<TerrainAfterglow>() ?? gameObject.AddComponent<TerrainAfterglow>();
            // [11-23 V50] 기획서 7 · 연출 9.1 "캐릭터 · 오브젝트는 표시하지 않는다": on every screen the flash reaches, not only the
            // Seeker's (the bodies and objects are not lit by the flash, so they stood as black shapes against the lit terrain).
            BeginClickerVisibilitySuppression(origin, sourcePlayerId);
            UpdateClickerFlashShader();
            terrainAfterglow.CaptureFlash(origin, ClickerCameraFlashRadius, ClickerCameraFlashSeconds,
                ClickerAfterglowSeconds, ClickerAfterglowPeakOpacity);
        }

        // [11-23 V50] The concealment lasts the flash and its afterglow (0.6 s + 12 s, the existing values) on this screen only
        // (Renderer.forceRenderingOff, local, nothing sent). A body or object the local voice light reaches stays drawn — the
        // existing rule that the light shows bodies (the body shader's own _Marco* light, ShoutRimRules.LightVisibility) —
        // except the Runner who used the item on the Seeker's screen during the flash itself (the rule this code had before).
        // Echo bodies (CharacterBody.Ghost) keep their own rules (EchoBodyRules); the own body is never touched.
        private readonly List<Renderer> clickerObjectCandidates = new();
        private readonly List<Renderer> clickerFrameHide = new();
        private readonly HashSet<Renderer> clickerFrameSet = new();
        private readonly List<Renderer> clickerReleased = new();
        public static int ClickerHiddenBodies { get; private set; }
        public static int ClickerHiddenObjects { get; private set; }
        public static int ClickerLitKept { get; private set; }

        private void BeginClickerVisibilitySuppression(Vector3 origin, int sourcePlayerId)
        {
            RestoreClickerVisibilitySuppression();
            clickerFlashOrigin = origin;
            clickerFlashSourcePlayerId = sourcePlayerId;
            clickerVisibilityUntil = Time.unscaledTime + ClickerCameraFlashSeconds + ClickerAfterglowSeconds;
            // Objects: the item visuals in the flash radius, gathered once. [11-24 V54] Not map content: V50 also hid the map's own
            // non-terrain surfaces (benches, shoe racks, ladders, pipes — anything under a map root), which cut holes next to walls.
            // An item visual is a world-lit surface (no terrain flag) that the round code builds at run time (IsItemVisual).
            float radiusSquared = ClickerCameraFlashRadius * ClickerCameraFlashRadius;
            if (clickerVisibilityProperties == null)
                clickerVisibilityProperties = new MaterialPropertyBlock();
            var roundRoot = Marco.Network.Round.NetworkRound.Instance != null ? Marco.Network.Round.NetworkRound.Instance.transform : null;
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (renderer == null || renderer.bounds.SqrDistance(origin) > radiusSquared) continue;
                if (renderer.GetComponentInParent<NetworkPlayer>(true) != null) continue;
                var material = renderer.sharedMaterial;
                if (material == null || !material.HasProperty(ClickerTerrainId) || material.GetFloat(ClickerTerrainId) > 0.5f) continue;
                renderer.GetPropertyBlock(clickerVisibilityProperties);
                if (clickerVisibilityProperties.GetFloat(ClickerTerrainId) > 0.5f) continue;
                if (!IsItemVisual(renderer, roundRoot)) continue;
                clickerObjectCandidates.Add(renderer);
            }
            UpdateClickerVisibilitySuppression();
        }

        // The duck / sound can views live under NetworkRound; a pickup model (찰칵이 · 붕대) carries the world-lit material copies the round
        // code made for it (RuntimeMarcoSurfaceMaterials); a pickup without its model is the code-built cube in the "붕대 아이템" material.
        // Map furniture, ladders, pipes and the network scene's fixtures have none of these and are never hidden.
        private const string CodeBuiltItemMaterial = "붕대 아이템";   // NetworkRound.CreateBandageMaterial's name (no new value)
        private static bool IsItemVisual(Renderer renderer, Transform roundRoot)
        {
            if (roundRoot != null && renderer.transform.IsChildOf(roundRoot)) return true;
            if (renderer.GetComponentInParent<Character.RuntimeMarcoSurfaceMaterials>(true) != null) return true;
            var material = renderer.sharedMaterial;
            return material != null && material.name == CodeBuiltItemMaterial;
        }

        private void UpdateClickerVisibilitySuppression()
        {
            if (clickerVisibilityUntil <= 0f) return;
            if (Time.unscaledTime >= clickerVisibilityUntil)
            {
                RestoreClickerVisibilitySuppression();
                return;
            }

            bool inFlash = Time.unscaledTime < clickerFlashStartedAt + ClickerCameraFlashSeconds;
            NetworkPlayer local = NetworkPlayer.Local;
            bool seekerScreen = local != null && local.Role == PlayerRole.Seeker;
            clickerFrameHide.Clear(); clickerFrameSet.Clear();
            int bodies = 0, objects = 0, kept = 0;
            foreach (NetworkPlayer player in NetworkPlayer.All)
            {
                if (player == null || player == local) continue;
                var character = player.GetComponent<Character.CharacterBody>();
                if (character != null && character.Ghost != Character.GhostLook.None) continue;
                if (!SurvivorBodyVisibility.TryGetBody(player, out var parts)) continue;
                bool source = seekerScreen && inFlash && player.PlayerId == clickerFlashSourcePlayerId;
                float lit = 0f;
                foreach (var part in parts) if (part != null) lit = Mathf.Max(lit, ClickerLightVisibility(part.bounds));
                if (!source && lit >= ShoutRimRules.MinVisibility) { kept++; continue; }
                bodies++;
                foreach (var part in parts) if (part != null && clickerFrameSet.Add(part)) clickerFrameHide.Add(part);
            }
            foreach (var renderer in clickerObjectCandidates)
            {
                if (renderer == null) continue;
                if (ClickerLightVisibility(renderer.bounds) >= ShoutRimRules.MinVisibility) { kept++; continue; }
                objects++;
                if (clickerFrameSet.Add(renderer)) clickerFrameHide.Add(renderer);
            }
            // Hand back what the light reaches now; hide (again) the rest — re-applied every frame, since an animated or managed
            // renderer can be switched back on while the window runs.
            clickerReleased.Clear();
            foreach (var entry in clickerHiddenRenderers) if (entry.Key == null || !clickerFrameSet.Contains(entry.Key)) clickerReleased.Add(entry.Key);
            foreach (var renderer in clickerReleased)
            {
                if (renderer != null) renderer.forceRenderingOff = clickerHiddenRenderers[renderer];
                clickerHiddenRenderers.Remove(renderer);
            }
            foreach (var renderer in clickerFrameHide)
            {
                if (!clickerHiddenRenderers.ContainsKey(renderer)) clickerHiddenRenderers.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
            ClickerHiddenBodies = bodies; ClickerHiddenObjects = objects; ClickerLitKept = kept;
        }

        // The local voice light at the point of these bounds nearest to it, as the body shader computes it.
        private static float ClickerLightVisibility(Bounds bounds)
        {
            Vector4 light = Shader.GetGlobalVector(PlayerPositionId);
            float distance = Vector3.Distance(bounds.ClosestPoint(light), light);
            return ShoutRimRules.LightVisibility(distance, Shader.GetGlobalFloat(LightRadiusId), Shader.GetGlobalFloat(LightIntensityId),
                Shader.GetGlobalFloat(FalloffPowerId), Shader.GetGlobalFloat(WorldModeId) > .5f);
        }

        private void RestoreClickerVisibilitySuppression()
        {
            foreach (KeyValuePair<Renderer, bool> entry in clickerHiddenRenderers)
                if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
            clickerHiddenRenderers.Clear();
            clickerObjectCandidates.Clear();
            clickerVisibilityUntil = 0f;
            ClickerHiddenBodies = ClickerHiddenObjects = ClickerLitKept = 0;
        }

        private void UpdateClickerFlashShader()
        {
            if (!clickerFlashActive)
            {
                SetClickerFlashShader(0f);
                return;
            }

            float envelope = GetClickerFlashEnvelope(Time.unscaledTime - clickerFlashStartedAt);
            Shader.SetGlobalVector(ClickerPositionId, new Vector4(clickerFlashOrigin.x, clickerFlashOrigin.y, clickerFlashOrigin.z, 1f));
            Shader.SetGlobalFloat(ClickerRadiusId, ClickerCameraFlashRadius);
            Shader.SetGlobalFloat(ClickerIntensityId, intensityMax * envelope);
            Shader.SetGlobalFloat(ClickerFalloffPowerId, 1f);
            Shader.SetGlobalColor(ClickerColorId, worldLightColor);
            Shader.SetGlobalFloat(ClickerEnabledId, envelope > 0.001f ? 1f : 0f);
        }

        private static void SetClickerFlashShader(float intensity)
        {
            Shader.SetGlobalFloat(ClickerEnabledId, 0f);
            Shader.SetGlobalFloat(ClickerIntensityId, intensity);
        }

        private void ApplyFullVisionOverride(bool enabled)
        {
            if (worldLightingOwner != this || brightnessMode != BrightnessMode.MarcoMinus) return;

            targetWorldRadius = worldRadius = 0f;
            targetWorldIntensity = worldIntensity = 0f;
            if (enabled)
            {
                RestoreEnvironment();
                return;
            }

            CaptureEnvironment();
            ApplyBrightnessMode();
        }

        private void OnGUI()
        {
            GUI.depth = 100; // Darkness behind HUD (lower depths are drawn on top).
            if (brightnessMode == BrightnessMode.MarcoPlus && !developmentFullVision)
                DrawDarknessMask(radius);

            GUI.depth = 0;
            // Hidden under the result screen (기획서 12.5).
            var round = Marco.Network.Round.NetworkRound.Instance;
            if (Settings.SettingsMenu.IsOpen || (round != null && round.IsResultScreenShown)) return;
            var local = NetworkPlayer.Local;
            if (round == null || round.Mode == Round.RoundMode.NotStarted || local == null) return;
            string notice = Input.GetKey(KeyCode.LeftAlt) ? "마이크 음소거" : microphoneClip == null ? "마이크 연결 확인" : null;
            if (notice == null) return;
            TestHudSkin.Begin();
            float bottom = TestHudSkin.Margin + 4;
            if (local.Role == PlayerRole.Runner && local.Status == PlayerStatus.Alive)
                bottom = NetworkTestHud.RunnerHealthTopFromBottom + 4;
            Lobby.RoomLettering.Draw(new Rect(TestHudSkin.Margin + 12, TestHudSkin.Height - bottom - 34, 310, 34),
                notice, 21);
        }

        private void DrawDarknessMask(float revealRadius)
        {
            UpdateMask(revealRadius);
            Color previousColor = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), mask);
            GUI.color = previousColor;
        }

        private float GetClickerFlashEnvelope(float elapsed)
        {
            float totalTransitionTime = Mathf.Max(0.001f, brightenTimeConstant + darkenTimeConstant);
            float openingDuration = ClickerCameraFlashSeconds * brightenTimeConstant / totalTransitionTime;
            float openingProgress = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, openingDuration));
            if (elapsed <= openingDuration)
                return Mathf.SmoothStep(0f, 1f, openingProgress);

            float closingDuration = ClickerCameraFlashSeconds - openingDuration;
            float closingProgress = Mathf.Clamp01((elapsed - openingDuration) / Mathf.Max(0.001f, closingDuration));
            return 1f - Mathf.SmoothStep(0f, 1f, closingProgress);
        }

        private void CaptureEnvironment()
        {
            if (environmentCaptured) return;
            savedLightmaps = LightmapSettings.lightmaps;
            savedRenderSettings = new RenderSettingsSnapshot
            {
                ambientMode = RenderSettings.ambientMode,
                ambientLight = RenderSettings.ambientLight,
                ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor,
                ambientIntensity = RenderSettings.ambientIntensity,
                reflectionIntensity = RenderSettings.reflectionIntensity,
                skybox = RenderSettings.skybox,
                customReflection = RenderSettings.customReflectionTexture,
                reflectionMode = RenderSettings.defaultReflectionMode
            };

            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (renderer == null) continue;
                savedRenderers.Add(new RendererLightingSnapshot
                {
                    renderer = renderer,
                    lightProbeUsage = renderer.lightProbeUsage,
                    reflectionProbeUsage = renderer.reflectionProbeUsage,
                    lightmapIndex = renderer.lightmapIndex,
                    lightmapScaleOffset = renderer.lightmapScaleOffset
                });
            }
            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (camera != null) savedCameras.Add(new CameraSnapshot { camera = camera, clearFlags = camera.clearFlags, backgroundColor = camera.backgroundColor });
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (light != null) savedLights.Add(new LightSnapshot { light = light, enabled = light.enabled });
            environmentCaptured = true;
        }

        private void ApplyBrightnessMode()
        {
            if (worldLightingOwner != this) return;
            if (!environmentCaptured) CaptureEnvironment();
            appliedBrightnessMode = brightnessMode;
            brightnessModeInitialized = true;

            if (brightnessMode == BrightnessMode.MarcoMinus && !IsFullVisionActive)
            {
                targetRadius = radius = 0f;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;
                RenderSettings.ambientSkyColor = Color.black;
                RenderSettings.ambientEquatorColor = Color.black;
                RenderSettings.ambientGroundColor = Color.black;
                RenderSettings.ambientIntensity = 0f;
                RenderSettings.reflectionIntensity = 0f;
                RenderSettings.skybox = null;
                RenderSettings.customReflectionTexture = null;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                LightmapSettings.lightmaps = System.Array.Empty<LightmapData>();

                foreach (RendererLightingSnapshot state in savedRenderers)
                {
                    if (state.renderer == null) continue;
                    state.renderer.lightProbeUsage = LightProbeUsage.Off;
                    state.renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    state.renderer.lightmapIndex = 0xFFFF;
                }
                foreach (CameraSnapshot state in savedCameras)
                {
                    if (state.camera == null) continue;
                    state.camera.clearFlags = CameraClearFlags.SolidColor;
                    state.camera.backgroundColor = Color.black;
                }
                foreach (LightSnapshot state in savedLights)
                    if (state.light != null) state.light.enabled = false;
                // Clear any values left by a previous play session before the
                // first Update pushes this controller's microphone state.
                Shader.SetGlobalFloat(LightRadiusId, 0f);
                Shader.SetGlobalFloat(LightIntensityId, 0f);
                Shader.SetGlobalFloat(WorldModeId, 1f);
            }
            else
            {
                RestoreEnvironment();
                targetWorldRadius = worldRadius = 0f;
                targetWorldIntensity = worldIntensity = 0f;
                Shader.SetGlobalFloat(WorldModeId, 0f);
            }
        }

        private void RestoreEnvironment()
        {
            if (!environmentCaptured) return;
            RenderSettings.ambientMode = savedRenderSettings.ambientMode;
            RenderSettings.ambientLight = savedRenderSettings.ambientLight;
            RenderSettings.ambientSkyColor = savedRenderSettings.ambientSky;
            RenderSettings.ambientEquatorColor = savedRenderSettings.ambientEquator;
            RenderSettings.ambientGroundColor = savedRenderSettings.ambientGround;
            RenderSettings.ambientIntensity = savedRenderSettings.ambientIntensity;
            RenderSettings.reflectionIntensity = savedRenderSettings.reflectionIntensity;
            RenderSettings.skybox = savedRenderSettings.skybox;
            RenderSettings.customReflectionTexture = savedRenderSettings.customReflection;
            RenderSettings.defaultReflectionMode = savedRenderSettings.reflectionMode;
            LightmapSettings.lightmaps = savedLightmaps ?? System.Array.Empty<LightmapData>();

            foreach (RendererLightingSnapshot state in savedRenderers)
            {
                if (state.renderer == null) continue;
                state.renderer.lightProbeUsage = state.lightProbeUsage;
                state.renderer.reflectionProbeUsage = state.reflectionProbeUsage;
                state.renderer.lightmapIndex = state.lightmapIndex;
                state.renderer.lightmapScaleOffset = state.lightmapScaleOffset;
            }
            foreach (CameraSnapshot state in savedCameras)
            {
                if (state.camera == null) continue;
                state.camera.clearFlags = state.clearFlags;
                state.camera.backgroundColor = state.backgroundColor;
            }
            foreach (LightSnapshot state in savedLights)
                if (state.light != null) state.light.enabled = state.enabled;
            Shader.SetGlobalFloat(WorldModeId, 0f);
            environmentCaptured = false;
            savedRenderers.Clear();
            savedCameras.Clear();
            savedLights.Clear();
        }

        private void SmoothWorldLighting(float deltaTime)
        {
            float radiusTau = targetWorldRadius > worldRadius ? brightenTimeConstant : darkenTimeConstant;
            float intensityTau = targetWorldIntensity > worldIntensity ? brightenTimeConstant : darkenTimeConstant;
            float radiusBlend = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.001f, radiusTau));
            float intensityBlend = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.001f, intensityTau));
            worldRadius += (targetWorldRadius - worldRadius) * radiusBlend;
            worldIntensity += (targetWorldIntensity - worldIntensity) * intensityBlend;

            if (targetWorldRadius <= 0f && worldRadius <= 0.001f) worldRadius = 0f;
            if (targetWorldIntensity <= 0f && worldIntensity <= 0.001f) worldIntensity = 0f;
        }

        private void PushWorldLighting()
        {
            if (worldLightingOwner != this || brightnessMode != BrightnessMode.MarcoMinus || IsFullVisionActive) return;
            Vector3 position = visionCamera != null ? visionCamera.transform.position : transform.position;
            Shader.SetGlobalVector(PlayerPositionId, new Vector4(position.x, position.y, position.z, 1f));
            Shader.SetGlobalFloat(LightRadiusId, worldRadius);
            Shader.SetGlobalFloat(LightIntensityId, worldIntensity);
            Shader.SetGlobalFloat(FalloffPowerId, falloffPower);
            Shader.SetGlobalColor(LightColorId, worldLightColor);

        }

        private void UpdateMask(float revealRadius)
        {
            float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            if (mask == null) { mask = new Texture2D(256, 256, TextureFormat.RGBA32, false); mask.wrapMode = TextureWrapMode.Clamp; }
            if (Mathf.Abs(drawnRadius - revealRadius) < 0.002f && drawnAspect == aspect) return;
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                float dx = ((x + 0.5f) / 256f - 0.5f) * aspect;
                float dy = (y + 0.5f) / 256f - 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = revealRadius <= 0.001f ? 1f : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.Max(0, revealRadius - 0.04f), revealRadius, distance));
                maskPixels[y * 256 + x] = new Color32(0, 0, 0, (byte)(alpha * 255));
            }
            mask.SetPixels32(maskPixels); mask.Apply(false);
            drawnRadius = revealRadius; drawnAspect = aspect;
        }
    }

    public enum BrightnessMode { MarcoMinus, MarcoPlus }
}
