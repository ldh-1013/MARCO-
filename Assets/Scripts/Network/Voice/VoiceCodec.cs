using System;

namespace Marco.Network.Voice
{
    // 20 ms microphone frames (48 kHz float) ↔ 16 kHz μ-law packets of 320 bytes, half the size of
    // the prototype's PCM16 (ProximityVoiceProtocol) and well under one UDP packet.
    public static class VoiceCodec
    {
        public const int SampleRate = 16000;
        public const int InputSamplesPerPacket = 960; // MicrophoneVisionController frame: 20 ms at 48 kHz
        public const int SamplesPerPacket = 320;      // 20 ms at 16 kHz
        private const int Downsample = InputSamplesPerPacket / SamplesPerPacket;
        private const int MuLawBias = 0x84;
        private const int MuLawClip = 32635;

        // Returns null when the frame is not one 48 kHz microphone frame.
        public static byte[] Encode(float[] input)
        {
            if (input == null || input.Length != InputSamplesPerPacket) return null;
            var packet = new byte[SamplesPerPacket];
            for (int i = 0; i < SamplesPerPacket; i++)
            {
                float sum = 0f;
                for (int j = 0; j < Downsample; j++) sum += input[i * Downsample + j];
                float sample = Math.Max(-1f, Math.Min(1f, sum / Downsample));
                packet[i] = LinearToMuLaw((int)Math.Round(sample * short.MaxValue));
            }
            return packet;
        }

        public static float Decode(byte value) => MuLawToLinear(value) / 32768f;

        // G.711 μ-law.
        private static byte LinearToMuLaw(int pcm)
        {
            int sign = pcm < 0 ? 0x80 : 0;
            int magnitude = Math.Min(Math.Abs(pcm), MuLawClip) + MuLawBias;
            int exponent = 7;
            for (int mask = 0x4000; (magnitude & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
            int mantissa = (magnitude >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        private static int MuLawToLinear(byte value)
        {
            int mu = ~value & 0xFF;
            int exponent = (mu >> 4) & 0x07;
            int magnitude = (((mu & 0x0F) << 3) + MuLawBias << exponent) - MuLawBias;
            return (mu & 0x80) != 0 ? -magnitude : magnitude;
        }
    }
}
