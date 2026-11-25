using UnityEngine;

namespace Marco.Network.Voice
{
    // D1: decides whether a microphone frame is speech before it is sent or drawn as a ring (기획서 §5.2).
    // Applied only to voice sending and the own voice ring — the voice light keeps its own −50 dBFS check.
    //   ③ calibration: a slowly tracked personal noise floor (a quiet-frame baseline instead of the lobby
    //      prompt, which needs UI); speech must be MarginDb above it and above −50 dBFS
    //   ④ band-pass 80 – 3,400 Hz before measuring, so fans, hum and hiss count less
    //   ⑦ debounce: speech must last 0.15s before the gate opens; it closes after HangSeconds of silence
    // Off by default (RippleServerFeatures.VoiceActivityGate) until a voice regression pass.
    public sealed class VoiceActivityGate
    {
        public const float SampleRate = 48000f;             // MicrophoneVisionController frames (VoiceCodec input)
        public const float LowCut = 80f, HighCut = 3400f;    // §5.2 ④
        public const float SilenceDb = -50f;                 // §5.2 / 5.1 whisper floor
        public const float MarginDb = 8f;                    // 연출 튜닝값: speech above the personal noise floor
        public const float OpenAfter = .15f;                 // §5.2 ⑦
        public const float HangSeconds = .25f;               // 연출 튜닝값: keep word endings
        public const float FloorRise = 2f, FloorFall = 12f;  // 연출 튜닝값: dB per second the floor follows the room (up slowly, down fast)

        private readonly Biquad highPass = Biquad.HighPass(LowCut, SampleRate);
        private readonly Biquad lowPass = Biquad.LowPass(HighCut, SampleRate);
        private float noiseFloor = -60f, voicedSince = -1f, lastVoiced = float.NegativeInfinity, lastTime = -1f;
        private bool open;
        public float LastLevelDb { get; private set; }
        public bool IsOpen => open;

        public bool Accept(float[] samples, float now)
        {
            if (samples == null || samples.Length == 0) return false;
            double squares = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float filtered = lowPass.Step(highPass.Step(samples[i]));
                squares += filtered * filtered;
            }
            float level = 20f * Mathf.Log10(Mathf.Max(Mathf.Sqrt((float)(squares / samples.Length)), 1e-6f));
            LastLevelDb = level;
            float dt = lastTime < 0 ? 0f : Mathf.Clamp(now - lastTime, 0f, .5f);
            lastTime = now;

            bool voiced = level >= Mathf.Max(SilenceDb, noiseFloor + MarginDb);
            // The floor follows the room on every frame: down fast to quieter rooms, up slowly, so steady noise
            // (fan, hum) becomes floor within seconds while speech, which comes and goes, stays above it.
            noiseFloor = level < noiseFloor ? Mathf.Max(level, noiseFloor - FloorFall * dt) : Mathf.Min(level, noiseFloor + FloorRise * dt);
            if (!voiced) voicedSince = -1f;
            else
            {
                if (voicedSince < 0) voicedSince = now;
                lastVoiced = now;
            }
            if (!open && voiced && now - voicedSince >= OpenAfter) open = true;
            else if (open && now - lastVoiced > HangSeconds) open = false;
            return open;
        }

        // RBJ cookbook second-order section, Q = 1/√2.
        private sealed class Biquad
        {
            private readonly float b0, b1, b2, a1, a2;
            private float x1, x2, y1, y2;
            private Biquad(float b0, float b1, float b2, float a0, float a1, float a2)
            {
                this.b0 = b0 / a0; this.b1 = b1 / a0; this.b2 = b2 / a0; this.a1 = a1 / a0; this.a2 = a2 / a0;
            }
            public static Biquad HighPass(float frequency, float rate)
            {
                float w = 2f * Mathf.PI * frequency / rate, c = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * .70710678f);
                return new Biquad((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha);
            }
            public static Biquad LowPass(float frequency, float rate)
            {
                float w = 2f * Mathf.PI * frequency / rate, c = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * .70710678f);
                return new Biquad((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha);
            }
            public float Step(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }
    }
}
