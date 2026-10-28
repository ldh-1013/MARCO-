using System;
using System.Collections.Generic;
using Adrenak.RNNoise4Unity;
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
        private const int FrameSamples = 960; // 20 ms at 48 kHz; RNNoise processes 480-sample frames.
        private const int RequiredConsecutiveFrames = 3;
        private const float ClickerCameraFlashSeconds = 0.6f;
        private const float ClickerCameraFlashRadius = 6f;
        private const float ClickerAfterglowSeconds = 12f;
        private const float ClickerAfterglowPeakOpacity = 0.15f;
        private static readonly int ClickerTerrainId = Shader.PropertyToID("_MarcoClickerTerrain");
        private AudioClip microphoneClip;
        private string microphoneDevice;
        private readonly float[] sampleBuffer = new float[FrameSamples];
        private readonly float[] denoisedBuffer = new float[FrameSamples];
        private float[] microphoneReadBuffer;
        [Header("Microphone noise suppression")]
        [SerializeField] private bool useRnNoise = true;
        private Denoiser rnNoiseDenoiser;
        private bool rnNoiseUnavailableLogged;
        // Denoised 20 ms frame and its loudness grade, only while speech is detected (voice chat, step 5).
        public event Action<float[], VoiceGrade> VoiceSamplesReady;

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
        [SerializeField] private bool showMicrophoneFootDots = true;
        // A reference (not Shader.Find) so builds include it.
        [SerializeField] private Shader microphoneFootDotShader;
        [SerializeField] private Color microphoneFootDotColor = new Color(0.72f, 0.9f, 1f, 1f);
        [SerializeField, Min(0.02f)] private float microphoneFootDotRadius = 0.25f;
        [SerializeField, Min(0.1f)] private float microphoneFootDotLifetime = 5f;
        [SerializeField, Min(0.1f)] private float microphoneFootDotInterval = 2f;
        private float nextFrameTime;
        private int consecutiveVoiceFrames;
        private float revealUntil;
        private bool clickerFlashActive;
        private float clickerFlashStartedAt = float.NegativeInfinity;
        private float clickerFlashUntil;
        private Vector3 clickerFlashOrigin;
        private TerrainAfterglow terrainAfterglow;
        private readonly Dictionary<Renderer, bool> clickerHiddenRenderers = new();
        private MaterialPropertyBlock clickerVisibilityProperties;
        private float clickerVisibilityUntil;
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
        private string status = "마이크 준비 중… 작은 목소리로 말해 보세요.";
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
            if (Microphone.devices.Length == 0) { status = "마이크를 찾지 못했습니다 — F8 개발 시야로 화면을 확인할 수 있습니다."; return; }
            if (useRnNoise) TryCreateDenoiser();
            microphoneDevice = Microphone.devices[0]; microphoneClip = Microphone.Start(microphoneDevice, true, 1, SampleRate);
            if (microphoneClip != null)
                microphoneReadBuffer = new float[FrameSamples * Mathf.Max(1, microphoneClip.channels)];
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

            if (microphoneClip != null && !Input.GetKey(KeyCode.LeftAlt) && Time.unscaledTime >= nextFrameTime) ProcessFrame();
            if (Input.GetKey(KeyCode.LeftAlt))
            {
                targetRadius = radius = 0;
                targetWorldRadius = targetWorldIntensity = 0f;
                consecutiveVoiceFrames = 0;
                status = "마이크 강제 뮤트 (Left Alt)";
            }
            else
            {
                if (brightnessMode == BrightnessMode.MarcoPlus && Time.unscaledTime > revealUntil)
                    targetRadius = 0;
            }
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
            rnNoiseDenoiser?.Dispose();
            rnNoiseDenoiser = null;
            if (mask != null) Destroy(mask);
            RestoreClickerVisibilitySuppression();
            if (worldLightingOwner == this) SetClickerFlashShader(0f);
            terrainAfterglow?.ClearAll();
            RestoreEnvironment();
        }

        private void OnDisable()
        {
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
            nextFrameTime = Time.unscaledTime + 0.02f;
            var position = Microphone.GetPosition(microphoneDevice); if (position < FrameSamples && Time.unscaledTime < 0.1f) return;
            int channels = Mathf.Max(1, microphoneClip.channels);
            int readOffset = (position - FrameSamples + microphoneClip.samples) % microphoneClip.samples;
            if (!microphoneClip.GetData(microphoneReadBuffer, readOffset)) return;
            for (int i = 0; i < FrameSamples; i++)
            {
                float monoSample = 0f;
                for (int channel = 0; channel < channels; channel++)
                    monoSample += microphoneReadBuffer[i * channels + channel];
                sampleBuffer[i] = monoSample / channels;
            }
            float[] analysisBuffer = sampleBuffer;
            if (useRnNoise && rnNoiseDenoiser != null)
            {
                Array.Copy(sampleBuffer, denoisedBuffer, sampleBuffer.Length);
                try
                {
                    int processedSamples = rnNoiseDenoiser.Denoise(denoisedBuffer.AsSpan());
                    if (processedSamples == denoisedBuffer.Length) analysisBuffer = denoisedBuffer;
                }
                catch (Exception exception)
                {
                    rnNoiseDenoiser.Dispose();
                    rnNoiseDenoiser = null;
                    if (!rnNoiseUnavailableLogged)
                    {
                        Debug.LogWarning("RNNoise microphone suppression failed; using the raw microphone signal. " + exception.Message, this);
                        rnNoiseUnavailableLogged = true;
                    }
                }
            }
            double squares = 0; for (var i = 0; i < analysisBuffer.Length; i++) squares += analysisBuffer[i] * analysisBuffer[i];
            var dbfs = 20f * Mathf.Log10(Mathf.Max(Mathf.Sqrt((float)(squares / sampleBuffer.Length)), 0.000001f));
            var level = dbfs >= -15f ? 3 : dbfs >= -30f ? 2 : dbfs > silenceDb ? 1 : 0;
            consecutiveVoiceFrames = level == 0 ? 0 : consecutiveVoiceFrames + 1;
            bool recognizedVoice = level > 0 && consecutiveVoiceFrames >= RequiredConsecutiveFrames;
            VoiceGrade voiceGrade = level == 3 ? VoiceGrade.Shout : level == 2 ? VoiceGrade.Talk : VoiceGrade.Whisper;
            if (recognizedVoice) VoiceSamplesReady?.Invoke(analysisBuffer, voiceGrade);
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
            status = (level == 3 ? "고함" : level == 2 ? "대화" : "속삭임") + "  " + dbfs.ToString("F0") + " dBFS · 주변이 드러납니다";
        }

        private void TryCreateDenoiser()
        {
            try
            {
                rnNoiseDenoiser = new Denoiser();
            }
            catch (Exception exception)
            {
                rnNoiseDenoiser = null;
                if (!rnNoiseUnavailableLogged)
                {
                    Debug.LogWarning("RNNoise microphone suppression is unavailable on this platform; using the raw microphone signal. " + exception.Message, this);
                    rnNoiseUnavailableLogged = true;
                }
            }
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
            TriggerClickerCameraFlash(origin);
        }

        public void TriggerClickerCameraFlash(Vector3 origin)
        {
            clickerFlashActive = true;
            clickerFlashStartedAt = Time.unscaledTime;
            clickerFlashUntil = clickerFlashStartedAt + ClickerCameraFlashSeconds;
            clickerFlashOrigin = origin;
            if (terrainAfterglow == null)
                terrainAfterglow = GetComponent<TerrainAfterglow>() ?? gameObject.AddComponent<TerrainAfterglow>();
            BeginClickerVisibilitySuppression(origin);
            UpdateClickerFlashShader();
            terrainAfterglow.CaptureFlash(origin, ClickerCameraFlashRadius, ClickerCameraFlashSeconds,
                ClickerAfterglowSeconds, ClickerAfterglowPeakOpacity);
        }

        private void BeginClickerVisibilitySuppression(Vector3 origin)
        {
            RestoreClickerVisibilitySuppression();
            clickerFlashOrigin = origin;
            clickerVisibilityUntil = Time.unscaledTime + ClickerCameraFlashSeconds;
            UpdateClickerVisibilitySuppression();
        }

        private void UpdateClickerVisibilitySuppression()
        {
            if (Time.unscaledTime >= clickerVisibilityUntil)
            {
                RestoreClickerVisibilitySuppression();
                return;
            }

            float radiusSquared = ClickerCameraFlashRadius * ClickerCameraFlashRadius;
            if (clickerVisibilityProperties == null)
                clickerVisibilityProperties = new MaterialPropertyBlock();
            foreach (Renderer renderer in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (renderer == null || clickerHiddenRenderers.ContainsKey(renderer) ||
                    renderer.bounds.SqrDistance(clickerFlashOrigin) > radiusSquared)
                    continue;

                renderer.GetPropertyBlock(clickerVisibilityProperties);
                bool isTerrain = clickerVisibilityProperties.GetFloat(ClickerTerrainId) > 0.5f;
                if (isTerrain && renderer.GetComponentInParent<NetworkPlayer>(true) == null) continue;

                clickerHiddenRenderers.Add(renderer, renderer.forceRenderingOff);
                renderer.forceRenderingOff = true;
            }
        }

        private void RestoreClickerVisibilitySuppression()
        {
            foreach (KeyValuePair<Renderer, bool> entry in clickerHiddenRenderers)
                if (entry.Key != null) entry.Key.forceRenderingOff = entry.Value;
            clickerHiddenRenderers.Clear();
            clickerVisibilityUntil = 0f;
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
            GUI.Label(new Rect(35, Screen.height - 72, 900, 25), status);
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
