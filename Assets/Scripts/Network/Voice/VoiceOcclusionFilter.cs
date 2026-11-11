using UnityEngine;

namespace Marco.Network.Voice
{
    /// <summary>In-place two-pole low-pass and gain for one remote voice source.</summary>
    [DisallowMultipleComponent]
    public sealed class VoiceOcclusionFilter : MonoBehaviour
    {
        private const int MaximumChannels = 8;
        private const float VoiceSignalThreshold = 0.0001f;

        private readonly float[] firstPoleState = new float[MaximumChannels];
        private readonly float[] secondPoleState = new float[MaximumChannels];
        private volatile float targetCutoffHz = 22000f;
        private volatile float targetGain = 1f;
        private volatile float targetOcclusion;
        private volatile bool hasVoiceSignal;
        private float currentOcclusion;
        private float currentCutoffHz = 22000f;
        private float currentGain = 1f;
        private float openCutoffHz = 22000f;
        private float sampleRate = 48000f;
        private float occludingTimeConstant = 0.08f;
        private float openingTimeConstant = 0.12f;

        public bool HasVoiceSignal => hasVoiceSignal;
        public float TargetOcclusion => targetOcclusion;

        public void Configure(VoiceOcclusionSettings settings, int dspSampleRate)
        {
            if (settings == null) return;
            sampleRate = dspSampleRate > 0 ? dspSampleRate : 48000f;
            openCutoffHz = Mathf.Max(100f, settings.OpenCutoffHz);
            currentCutoffHz = openCutoffHz;
            targetCutoffHz = openCutoffHz;
            targetGain = 1f;
            targetOcclusion = 0f;
            occludingTimeConstant = Mathf.Max(0.001f, settings.OccludingTimeConstant);
            openingTimeConstant = Mathf.Max(0.001f, settings.OpeningTimeConstant);
        }

        public void SetTargets(float occlusion, float cutoffHz, float gain)
        {
            targetOcclusion = Mathf.Clamp01(occlusion);
            targetCutoffHz = Mathf.Max(100f, cutoffHz);
            targetGain = Mathf.Clamp01(gain);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (data == null || channels <= 0) return;
            int frameCount = data.Length / channels;
            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float magnitude = Mathf.Abs(data[i]);
                if (magnitude > peak) peak = magnitude;
            }
            hasVoiceSignal = peak > VoiceSignalThreshold;

            float wantedCutoff = targetCutoffHz;
            float wantedGain = targetGain;
            float wantedOcclusion = targetOcclusion;
            float openHz = openCutoffHz;
            float blockSeconds = frameCount / sampleRate;
            float tau = wantedOcclusion > currentOcclusion ? occludingTimeConstant : openingTimeConstant;
            float blend = 1f - Mathf.Exp(-blockSeconds / Mathf.Max(0.001f, tau));
            currentOcclusion += (wantedOcclusion - currentOcclusion) * blend;

            if (wantedCutoff >= openHz - 0.5f && wantedGain >= 0.99999f &&
                currentCutoffHz >= openHz - 1f && currentGain >= 0.9999f)
            {
                currentCutoffHz = openHz;
                currentGain = 1f;
                currentOcclusion = 0f;
                if (peak <= VoiceSignalThreshold)
                {
                    for (int i = 0; i < MaximumChannels; i++)
                    {
                        firstPoleState[i] = 0f;
                        secondPoleState[i] = 0f;
                    }
                }
                return;
            }

            float currentLog = Mathf.Log(Mathf.Max(1f, currentCutoffHz));
            float targetLog = Mathf.Log(Mathf.Max(1f, wantedCutoff));
            currentCutoffHz = Mathf.Exp(currentLog + (targetLog - currentLog) * blend);
            currentGain += (wantedGain - currentGain) * blend;

            float coefficient = 1f - Mathf.Exp(-2f * Mathf.PI * currentCutoffHz / sampleRate);
            int processedChannels = Mathf.Min(channels, MaximumChannels);
            for (int frame = 0, index = 0; frame < frameCount; frame++)
            {
                for (int channel = 0; channel < channels; channel++, index++)
                {
                    if (channel >= processedChannels) continue;
                    float sample = data[index];
                    float first = firstPoleState[channel] + coefficient * (sample - firstPoleState[channel]);
                    firstPoleState[channel] = first;
                    float second = secondPoleState[channel] + coefficient * (first - secondPoleState[channel]);
                    secondPoleState[channel] = second;
                    data[index] = second * currentGain;
                }
            }
        }
    }
}
