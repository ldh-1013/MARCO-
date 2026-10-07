using UnityEngine;

namespace Marco.Prototype
{
    /// <summary>Local microphone-driven vision prototype. Samples never leave this client.</summary>
    public sealed class MicrophoneVisionController : MonoBehaviour
    {
        private const int SampleRate = 16000;
        private const int FrameSamples = 320; // 20 ms at 16 kHz
        private const int RequiredConsecutiveFrames = 3;
        private AudioClip microphoneClip;
        private string microphoneDevice;
        private readonly float[] sampleBuffer = new float[FrameSamples];
        private float nextFrameTime;
        private int consecutiveVoiceFrames;
        private float revealUntil;
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
        public bool IsCenterVisible => developmentFullVision || radius > 0.01f;

        private void Start()
        {
            if (Microphone.devices.Length == 0) { status = "마이크를 찾지 못했습니다 — F8 개발 시야로 화면을 확인할 수 있습니다."; return; }
            microphoneDevice = Microphone.devices[0]; microphoneClip = Microphone.Start(microphoneDevice, true, 1, SampleRate);
        }

        private void Update()
        {
            if (microphoneClip != null && !Input.GetKey(KeyCode.LeftAlt) && Time.unscaledTime >= nextFrameTime) ProcessFrame();
            if (Input.GetKey(KeyCode.LeftAlt)) { targetRadius = 0; radius = 0; consecutiveVoiceFrames = 0; status = "마이크 강제 뮤트 (Left Alt)"; }
            else if (Time.unscaledTime > revealUntil) targetRadius = 0;
            radius = Mathf.MoveTowards(radius, targetRadius, Time.unscaledDeltaTime * closeSpeed);
        }

        private void OnDestroy()
        {
            if (!string.IsNullOrEmpty(microphoneDevice) && Microphone.IsRecording(microphoneDevice)) Microphone.End(microphoneDevice);
            if (mask != null) Destroy(mask);
        }

        private void ProcessFrame()
        {
            nextFrameTime = Time.unscaledTime + 0.02f;
            var position = Microphone.GetPosition(microphoneDevice); if (position < FrameSamples && Time.unscaledTime < 0.1f) return;
            microphoneClip.GetData(sampleBuffer, (position - FrameSamples + microphoneClip.samples) % microphoneClip.samples);
            double squares = 0; for (var i = 0; i < sampleBuffer.Length; i++) squares += sampleBuffer[i] * sampleBuffer[i];
            var dbfs = 20f * Mathf.Log10(Mathf.Max(Mathf.Sqrt((float)(squares / sampleBuffer.Length)), 0.000001f));
            var level = dbfs >= -15f ? 3 : dbfs >= -30f ? 2 : dbfs > silenceDb ? 1 : 0;
            consecutiveVoiceFrames = level == 0 ? 0 : consecutiveVoiceFrames + 1; if (consecutiveVoiceFrames < RequiredConsecutiveFrames) return;
            targetRadius = Mathf.InverseLerp(silenceDb, Mathf.Max(silenceDb + 1f, fullVisionDb), dbfs) * maximumRadius;
            revealUntil = Time.unscaledTime + 0.06f;
            status = (level == 3 ? "고함" : level == 2 ? "대화" : "속삭임") + "  " + dbfs.ToString("F0") + " dBFS · 주변이 드러납니다";
        }

        public void SetDevelopmentFullVision(bool enabled) { developmentFullVision = enabled; }

        private void OnGUI()
        {
            GUI.depth = 100; // Darkness behind HUD (lower depths are drawn on top).
            if (!developmentFullVision)
            {
                UpdateMask();
                var old = GUI.color; GUI.color = Color.white;
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), mask);
                GUI.color = old;
            }
            GUI.depth = 0;
            GUI.Label(new Rect(35, Screen.height - 72, 900, 25), status);
        }

        private void UpdateMask()
        {
            float aspect = Screen.width / (float)Mathf.Max(1, Screen.height);
            if (mask == null) { mask = new Texture2D(256, 256, TextureFormat.RGBA32, false); mask.wrapMode = TextureWrapMode.Clamp; }
            if (Mathf.Abs(drawnRadius - radius) < 0.002f && drawnAspect == aspect) return;
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
            {
                float dx = ((x + 0.5f) / 256f - 0.5f) * aspect;
                float dy = (y + 0.5f) / 256f - 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = radius <= 0.001f ? 1f : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Mathf.Max(0, radius - 0.04f), radius, distance));
                maskPixels[y * 256 + x] = new Color32(0, 0, 0, (byte)(alpha * 255));
            }
            mask.SetPixels32(maskPixels); mask.Apply(false);
            drawnRadius = radius; drawnAspect = aspect;
        }
    }
}
