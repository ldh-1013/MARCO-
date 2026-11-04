using UnityEngine;

namespace Marco.Network.Audio
{
    // Uses only the supplied recording. Trim quiet edges and overlap tail/head without gain normalization.
    internal static class LoopClip
    {
        // The supplied drone is a rising/falling effect, rather than a steady ambience.
        // Keep its sustained body and use an equal-power splice, leaving the source asset untouched.
        public static AudioClip PrepareDrone(AudioClip source)
        {
            source.LoadAudioData();
            var data = new float[source.samples * source.channels];
            if (!source.GetData(data, 0)) return source;
            int block = Mathf.Max(1, source.frequency / 5);
            int blocks = source.samples / block;
            if (blocks < 10) return Prepare(source, .75f);
            var rms = new float[blocks];
            float loudest = 0, sourcePeak = 0;
            foreach (float value in data) sourcePeak = Mathf.Max(sourcePeak, Mathf.Abs(value));
            for (int b=0; b<blocks; b++)
            {
                double power=0;
                for (int j=b*block*source.channels; j<(b+1)*block*source.channels; j++) power+=data[j]*data[j];
                rms[b]=Mathf.Sqrt((float)(power/(block*source.channels)));
                loudest=Mathf.Max(loudest,rms[b]);
            }
            if (loudest < .00001f) return source;
            int first=0,last=blocks-1;
            while(first<last && rms[first]<loudest*.5f)first++;
            while(last>first && rms[last]<loudest*.5f)last--;
            int start=first*block,end=(last+1)*block,count=end-start;
            if(count<source.frequency*2)return Prepare(source,.75f);
            int overlap=Mathf.Min(Mathf.RoundToInt(source.frequency*1.1f),count/3);
            int frames=count-overlap,middle=count-2*overlap;
            var output=new float[frames*source.channels];
            System.Array.Copy(data,(start+overlap)*source.channels,output,0,middle*source.channels);
            for(int i=0;i<overlap;i++)
            {
                float angle=i/(float)(overlap-1)*Mathf.PI*.5f;
                float tail=Mathf.Cos(angle),head=Mathf.Sin(angle);
                for(int ch=0;ch<source.channels;ch++)
                    output[(middle+i)*source.channels+ch]=data[(end-overlap+i)*source.channels+ch]*tail+data[(start+i)*source.channels+ch]*head;
            }
            float peak=0;foreach(float value in output)peak=Mathf.Max(peak,Mathf.Abs(value));
            // Equal-power mixing can add correlated peaks: only attenuate if needed, never amplify the clip.
            if(peak>sourcePeak)for(int i=0;i<output.Length;i++)output[i]*=sourcePeak/peak;
            // Filter the loop itself so playback and its splice have the same spectral balance.
            // The 24th-order 70 Hz low-pass gives at least 24 dB rejection from 80 Hz.
            FilterLoop(output, frames, source.channels, source.frequency, 40, 4, true);
            FilterLoop(output, frames, source.channels, source.frequency, 70, 24, false);
            var clip=AudioClip.Create(source.name+" Sustain Loop",frames,source.channels,source.frequency,false);
            clip.SetData(output,0);
            Debug.Log($"[MARCO][DroneLoop] sustained {start/(float)source.frequency:F2}–{end/(float)source.frequency:F2}s, equal-power overlap {overlap/(float)source.frequency:F2}s, loop {clip.length:F2}s");
            return clip;
        }

        private static void FilterLoop(float[] samples, int frames, int channels, int sampleRate,
            double cutoff, int order, bool highPass)
        {
            double angle = 2 * System.Math.PI * cutoff / sampleRate;
            double cosine = System.Math.Cos(angle), sine = System.Math.Sin(angle);
            int warmup = sampleRate;
            for (int stage = 0; stage < order / 2; stage++)
            {
                double q = .5 / System.Math.Sin(System.Math.PI * (stage + .5) / order);
                double alpha = sine / (2 * q), a0 = 1 + alpha;
                double b0 = (highPass ? 1 + cosine : 1 - cosine) / (2 * a0);
                double b1 = (highPass ? -2 : 2) * b0;
                double a1 = -2 * cosine / a0, a2 = (1 - alpha) / a0;
                for (int channel = 0; channel < channels; channel++)
                {
                    double delay1 = 0, delay2 = 0;
                    for (int i = -warmup; i < frames; i++)
                    {
                        int index = ((i % frames + frames) % frames) * channels + channel;
                        double input = samples[index];
                        double output = b0 * input + delay1;
                        delay1 = b1 * input - a1 * output + delay2;
                        delay2 = b0 * input - a2 * output;
                        if (i >= 0) samples[index] = (float)output;
                    }
                }
            }
        }

        public static AudioClip TrimCue(AudioClip source, float seconds)
        {
            source.LoadAudioData();
            var pcm = new float[source.samples * source.channels];
            if (!source.GetData(pcm, 0)) return source;
            int block = Mathf.Max(1, source.frequency / 100);
            int count = source.samples / block;
            if (count == 0) return source;
            var energy = new float[count];
            float peak = 0f;
            for (int i = 0; i < count; i++)
            {
                double sum = 0;
                int first = i * block * source.channels;
                int end = first + block * source.channels;
                for (int j = first; j < end; j++) sum += pcm[j] * pcm[j];
                energy[i] = Mathf.Sqrt((float)(sum / (end - first)));
                peak = Mathf.Max(peak, energy[i]);
            }
            if (peak < .0001f) return source;
            int onset = System.Array.FindIndex(energy, value => value >= peak * .3f);
            int start = Mathf.Max(0, (onset - 2) * block);
            int frames = Mathf.Min(source.samples - start, Mathf.RoundToInt(seconds * source.frequency));
            var cue = new float[frames * source.channels];
            System.Array.Copy(pcm, start * source.channels, cue, 0, cue.Length);
            for (int frame = 0; frame < frames; frame++)
            {
                float fade = Mathf.Min(1f, Mathf.Min(frame / (block * .5f), (frames - 1 - frame) / (block * 4f)));
                for (int ch = 0; ch < source.channels; ch++) cue[frame * source.channels + ch] *= fade;
            }
            var clip = AudioClip.Create(source.name + " Cue", frames, source.channels, source.frequency, false);
            clip.SetData(cue, 0);
            return clip;
        }

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
