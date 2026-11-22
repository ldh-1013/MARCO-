using System;
using UnityEngine;

namespace Marco.Network.Lobby
{
    // 표시 전용. Only locally captured or server-authorized audible voice reaches this signal.
    public static class LobbyVoiceActivity
    {
        public static event Action<float> Heard;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Heard = null;

        public static void Publish(float[] samples)
        {
            if (samples == null || samples.Length == 0) return;
            double sum = 0;
            foreach (float sample in samples) sum += sample * sample;
            PublishRms(Mathf.Sqrt((float)(sum / samples.Length)));
        }

        public static void PublishPacket(byte[] packet)
        {
            if (packet == null || packet.Length != Voice.VoiceCodec.SamplesPerPacket) return;
            double sum = 0;
            foreach (byte sample in packet) { float value = Voice.VoiceCodec.Decode(sample); sum += value * value; }
            PublishRms(Mathf.Sqrt((float)(sum / packet.Length)));
        }

        private static void PublishRms(float rms)
        {
            float db = 20 * Mathf.Log10(Mathf.Max(rms, .000001f));
            if (db > -50) Heard?.Invoke(Mathf.Lerp(.25f, 1, Mathf.InverseLerp(-50, -15, db)));
        }
    }
}
