using UnityEngine;

namespace Marco.Network.Audio
{
    // Uses only the supplied recording. Trim quiet edges and overlap tail/head without gain normalization.
    internal static class LoopClip
    {
        public static AudioClip Prepare(AudioClip source, float overlapSeconds)
        {
            source.LoadAudioData();
            var data = new float[source.samples * source.channels];
            if (!source.GetData(data, 0)) return source;
            int block = Mathf.Max(1, source.frequency / 100);
            int blocks = source.samples / block;
            var rms = new float[blocks]; float loudest = 0;
            for (int b = 0; b < blocks; b++)
            {
                double sum = 0;
                for (int j = b * block * source.channels; j < (b + 1) * block * source.channels; j++) sum += data[j] * data[j];
                rms[b] = Mathf.Sqrt((float)(sum / (block * source.channels)));
                loudest = Mathf.Max(loudest, rms[b]);
            }
            if (loudest < .00001f) return source;
            int first = 0, last = blocks - 1;
            while (first < last && rms[first] < loudest * .04f) first++;
            while (last > first && rms[last] < loudest * .04f) last--;
            int start = Mathf.Max(0, first * block - block);
            int end = Mathf.Min(source.samples, (last + 2) * block);
            int count = end - start;
            int overlap = Mathf.Min(Mathf.RoundToInt(overlapSeconds * source.frequency), count / 4);
            if (overlap < 2) return source;
            int frames = count - overlap;
            var output = new float[frames * source.channels];
            int middle = count - overlap * 2;
            System.Array.Copy(data, (start + overlap) * source.channels, output, 0, middle * source.channels);
            for (int i = 0; i < overlap; i++)
            {
                float weight = i / (float)(overlap - 1);
                for (int ch = 0; ch < source.channels; ch++)
                    output[(middle + i) * source.channels + ch] =
                        data[(end - overlap + i) * source.channels + ch] * (1 - weight) +
                        data[(start + i) * source.channels + ch] * weight;
            }
            var clip = AudioClip.Create(source.name + " Loop", frames, source.channels, source.frequency, false);
            clip.SetData(output, 0);
            Debug.Log($"[MARCO][AudioLoop] {source.name}: {source.length:F3}s -> {clip.length:F3}s, edge trim {start/(float)source.frequency:F3}s/{(source.samples-end)/(float)source.frequency:F3}s, overlap {overlap/(float)source.frequency:F3}s");
            return clip;
        }
    }
}
