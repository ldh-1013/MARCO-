using System;
using System.Globalization;

namespace Marco.Prototype
{
    /// <summary>Compact PCM16 voice packets carried by the prototype room relay.</summary>
    internal static class ProximityVoiceProtocol
    {
        public const int SampleRate = 16000;
        public const int SamplesPerPacket = 640; // 40 ms
        public const int BytesPerPacket = SamplesPerPacket * 2;
        private const string Prefix = "MARCO/4 VOICE ";
        private const int MaximumLineLength = 1800;
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

        public static string Encode(int playerId, uint sequence, byte[] pcm16)
        {
            if (pcm16 == null || pcm16.Length != BytesPerPacket) return null;
            return Prefix + playerId.ToString(Culture) + " " + sequence.ToString(Culture) + " " + Convert.ToBase64String(pcm16);
        }

        public static bool TryDecode(string line, out int playerId, out uint sequence, out byte[] pcm16)
        {
            playerId = 0;
            sequence = 0;
            pcm16 = null;
            if (line == null || line.Length > MaximumLineLength || !line.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            string[] fields = line.Substring(Prefix.Length).Split(' ');
            if (fields.Length != 3
                || !int.TryParse(fields[0], NumberStyles.None, Culture, out playerId) || playerId < 0
                || !uint.TryParse(fields[1], NumberStyles.None, Culture, out sequence) || sequence == 0)
                return false;

            try
            {
                pcm16 = Convert.FromBase64String(fields[2]);
                if (pcm16.Length == BytesPerPacket) return true;
            }
            catch (FormatException) { }

            pcm16 = null;
            return false;
        }

        public static byte[] EncodePcm16(float[] samples)
        {
            if (samples == null || samples.Length != SamplesPerPacket) return null;
            var bytes = new byte[BytesPerPacket];
            for (int i = 0; i < samples.Length; i++)
            {
                short value = (short)Math.Round(Math.Max(-1f, Math.Min(1f, samples[i])) * short.MaxValue);
                bytes[i * 2] = (byte)value;
                bytes[i * 2 + 1] = (byte)(value >> 8);
            }
            return bytes;
        }
    }
}
