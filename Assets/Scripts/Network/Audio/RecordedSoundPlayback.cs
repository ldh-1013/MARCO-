using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Voice;
using UnityEngine;

namespace Marco.Network.Audio
{
    // Memory-only recording playback. Server routes/repositions each frame and owns attenuation.
    public sealed class RecordedSoundPlayback : MonoBehaviour
    {
        private sealed class Playback
        {
            public AudioClip Clip;
            public AudioSource Source;
            public AudioLowPassFilter Filter;
            public float LastReceived;
            public bool Muffled;
        }
        private readonly Dictionary<int, Playback> playing = new();
        private readonly List<int> expired = new();
        private readonly float[] samples = new float[VoiceCodec.SamplesPerPacket];

        public void Receive(int id, int frame, byte[] packet, Vector3 point, float gain, bool muffled)
        {
            if (packet == null || packet.Length != samples.Length || frame < 0 || frame >= SoundCanConfig.Frames) return;
            if (!playing.TryGetValue(id, out var playback))
            {
                var root = new GameObject("소리통 녹음 재생");
                root.transform.SetParent(transform, false);
                var source = root.AddComponent<AudioSource>();
                source.playOnAwake = false; source.spatialBlend = 1f; source.dopplerLevel = 0;
                source.rolloffMode = AudioRolloffMode.Custom;
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0, 1, 1));
                source.maxDistance = 200;
                playback = new Playback { Source = source, Filter = root.AddComponent<AudioLowPassFilter>(),
                    Clip = AudioClip.Create("SoundCan memory", SoundCanConfig.Bytes, 1, VoiceCodec.SampleRate, false) };
                source.clip = playback.Clip;
                source.time = frame * SoundCanConfig.FrameSeconds;
                source.PlayDelayed(.06f);
                playing.Add(id, playback);
            }
            // Boost quiet recordings before distance/wall attenuation. Soft saturation prevents hard clipping.
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (float)System.Math.Tanh(VoiceCodec.Decode(packet[i]) * SoundCanConfig.PlaybackGain);
            playback.Clip.SetData(samples, frame * samples.Length);
            playback.Source.transform.position = point;
            playback.Source.volume = Mathf.Clamp01(gain);
            playback.Muffled = muffled;
            RoundAudioPlayback.ApplyMuffle(playback.Filter, muffled);   // wall 1,000 Hz · own dive 650 Hz (11-14)
            playback.LastReceived = Time.time;
            // §17: a recorder frame reached this listener (it passed the server's hearing check) — ends the Seeker drone.
            // Only Runners own recorders, so this is never the Seeker's own sound.
            NetworkRound.Instance?.NotifyOtherSound();
        }

        public void Stop(int id)
        {
            if (id < 0)
            {
                foreach (var playback in playing.Values) Dispose(playback);
                playing.Clear(); return;
            }
            if (playing.Remove(id, out var item)) Dispose(item);
        }

        private static void Dispose(Playback playback)
        {
            if (playback.Source != null) { playback.Source.Stop(); Destroy(playback.Source.gameObject); }
            if (playback.Clip != null) Destroy(playback.Clip);
        }

        private void Update()
        {
            var round = NetworkRound.Instance;
            var local = NetworkPlayer.Local;
            if (round == null || round.Mode == RoundMode.NotStarted || round.Result != RoundResult.InProgress ||
                local == null || local.Status != PlayerStatus.Alive) { Stop(-1); return; }
            expired.Clear();
            foreach (var pair in playing)
            {
                RoundAudioPlayback.ApplyMuffle(pair.Value.Filter, pair.Value.Muffled);   // a dive can start or end mid-playback
                if (Time.time - pair.Value.LastReceived > .25f) expired.Add(pair.Key);
            }
            foreach (int id in expired) Stop(id);
        }

        private void OnDestroy() => Stop(-1);
    }
}
