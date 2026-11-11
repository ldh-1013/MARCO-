using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEngine;

namespace Marco.Network.Voice
{
    // On the player prefab. Owner: microphone speech frames (only while speech is detected, and
    // never while Left Alt mutes) go to the server with their loudness grade. Server: forwards each
    // frame only to players who may hear it (VoiceRouting, 기획서 13.2 · 5.6 · 5.7). Listener: plays
    // the speaker's stream from this object. Playback is ported from Prototype ProximityVoiceChat.
    [RequireComponent(typeof(NetworkPlayer))]
    public sealed class PlayerVoice : NetworkBehaviour
    {
        private const int AudioQueueCapacity = VoiceCodec.SampleRate; // 1 s
        private const int AudioPrebufferSamples = VoiceCodec.SampleRate / 10; // 100 ms absorbs transport and frame scheduling jitter.

        [SerializeField] private VoiceRouting routing = new();
        // Mouth height above the player pivot.
        [SerializeField] private float mouthHeight = 0.6f;

        private readonly float[] audioQueue = new float[AudioQueueCapacity];
        private readonly object audioQueueLock = new();
        private int audioReadPosition;
        private int audioWritePosition;
        private int audioBufferedSamples;
        private bool audioPlaybackStarted;
        private NetworkPlayer player;
        private MicrophoneVisionController microphone;
        private AudioSource voiceSource;
        private AudioClip voiceClip;
        private AudioLowPassFilter voiceFilter;
        private float nextLocalPulse, nextServerPulse;
        private VoiceGrade lastLocalPulse, lastServerPulse;

        private static (float radius, float duration) PulseSpec(VoiceGrade grade) => grade switch
        {
            VoiceGrade.Whisper => (4f, .6f),
            VoiceGrade.Talk => (9f, 1.2f),
            _ => (22f, 2.5f)
        };

        private Vector3 VoiceOrigin()
        {
            Vector3 origin = transform.position + Vector3.up * mouthHeight;
            if (TryGetComponent<PlayerMovement>(out var movement)) origin.y = movement.WaterEyeHeight;
            return origin;
        }

        private void Awake() => player = GetComponent<NetworkPlayer>();

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;
            // The microphone sits on the owner's camera, which only the owner activates.
            microphone = GetComponentInChildren<MicrophoneVisionController>(true);
            if (microphone != null)
            {
                microphone.VoiceSamplesReady += OnVoiceSamplesReady;
            }
            else Debug.LogWarning("Local player has no microphone input for voice chat.", this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (microphone != null)
            {
                microphone.VoiceSamplesReady -= OnVoiceSamplesReady;
            }
            microphone = null;
        }

        private void OnVoiceSamplesReady(float[] samples, VoiceGrade grade)
        {
            Lobby.LobbyVoiceActivity.Publish(samples);
            var packet = VoiceCodec.Encode(samples);
            if (packet == null) return;
            var round = NetworkRound.Instance;
            if (round is { UsesPoolAudio: true } && VoiceRouting.ChannelOf(player) == VoiceChannel.Game &&
                (Time.time >= nextLocalPulse || grade > lastLocalPulse))
            {
                var (radius, duration) = PulseSpec(grade);
                nextLocalPulse = Time.time + duration;
                lastLocalPulse = grade;
                round.ShowSoundPulse(VoiceOrigin(), radius, duration, voiceFromSeeker: player.Role == PlayerRole.Seeker);
            }
            SendVoiceServer(packet, grade, Channel.Reliable);
        }

        [ServerRpc]
        private void SendVoiceServer(byte[] packet, VoiceGrade grade, Channel channel = Channel.Reliable)
        {
            if (packet == null || packet.Length != VoiceCodec.SamplesPerPacket) return;
            if ((byte)grade > (byte)VoiceGrade.Shout) return;
            var round = NetworkRound.Instance;
            bool inGameVoice = round is { UsesPoolAudio: true } && VoiceRouting.ChannelOf(player) == VoiceChannel.Game;
            var (baseRadius, duration) = PulseSpec(grade);
            Vector3 origin = VoiceOrigin();
            bool pulse = inGameVoice && (Time.time >= nextServerPulse || grade > lastServerPulse);
            if (pulse) { nextServerPulse = Time.time + duration; lastServerPulse = grade; }
            if (pulse) round.RecordAwardNoise(player.PlayerId, AwardNoise.Voice, baseRadius, duration); // 기획서 8.2
            foreach (var listener in NetworkPlayer.All)
            {
                if (listener == player) continue;
                if (inGameVoice)
                {
                    if (VoiceRouting.ChannelOf(listener) != VoiceChannel.Game && !listener.HasEchoBody) continue;
                    if (!round.TryHearPoolVoice(origin, listener, baseRadius, out var point, out float gain,
                        out bool blocked, out float radius)) continue;
                    float life = listener.HasEchoBody ? 3f : duration * (listener.Role == PlayerRole.Seeker ? 1.5f : 1f);
                    if (pulse && !blocked) VoicePulseTarget(listener.Owner, point, radius, life);
                    if (!listener.HasEchoBody) PlayVoiceTarget(listener.Owner, packet, radius, point, gain, blocked, true, Channel.Reliable);
                }
                else if (routing.TryGetHearing(player, listener, grade, out float radius))
                    PlayVoiceTarget(listener.Owner, packet, radius, origin, 1f, false, false, Channel.Reliable);
            }
        }

        [TargetRpc]
        private void VoicePulseTarget(NetworkConnection conn, Vector3 point, float radius, float duration)
        {
            NetworkRound.Instance?.ShowSoundPulse(point, radius, duration, voiceFromSeeker: player.Role == PlayerRole.Seeker);
            NetworkRound.Instance?.NotifyOtherSound();
        }

        [TargetRpc]
        private void PlayVoiceTarget(NetworkConnection conn, byte[] packet, float radius, Vector3 point,
            float gain, bool blocked, bool serverGain, Channel channel = Channel.Reliable)
        {
            if (IsOwner || packet == null) return;
            Lobby.LobbyVoiceActivity.PublishPacket(packet);
            NetworkRound.Instance?.NotifyOtherSound();
            EnsureVoiceSource();
            // Proximity voice fades out over the radius the server allowed; channel-wide voice is flat.
            voiceSource.spatialBlend = radius > 0 ? 1f : 0f;
            if (radius > 0) voiceSource.maxDistance = radius;
            voiceSource.transform.position = point;
            voiceSource.volume = gain;
            voiceSource.rolloffMode = serverGain ? AudioRolloffMode.Custom : AudioRolloffMode.Linear;
            // Keep the legacy server-side filter when local occlusion is disabled. When the
            // receiver DSP is active it owns wall filtering, avoiding two low-pass filters in series.
            voiceFilter.enabled = blocked && !VoiceOcclusionSettings.IsEnabled;

            lock (audioQueueLock)
            {
                foreach (byte value in packet)
                {
                    int nextWrite = (audioWritePosition + 1) % audioQueue.Length;
                    if (nextWrite == audioReadPosition)
                    {
                        audioReadPosition = (audioReadPosition + 1) % audioQueue.Length;
                        audioBufferedSamples--;
                    }
                    audioQueue[audioWritePosition] = VoiceCodec.Decode(value);
                    audioWritePosition = nextWrite;
                    audioBufferedSamples++;
                }
            }
        }

        private void EnsureVoiceSource()
        {
            if (voiceSource != null) return;
            var audioObject = new GameObject("Voice Audio");
            audioObject.transform.SetParent(transform, false);
            audioObject.transform.localPosition = Vector3.up * mouthHeight;

            voiceSource = audioObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.loop = true;
            voiceSource.dopplerLevel = 0f;
            voiceSource.rolloffMode = AudioRolloffMode.Linear;
            voiceSource.minDistance = 0.25f;
            voiceSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
            voiceFilter = audioObject.AddComponent<AudioLowPassFilter>();
            voiceFilter.cutoffFrequency = 1000f;
            voiceFilter.enabled = false;
            voiceClip = AudioClip.Create("Voice Stream", VoiceCodec.SampleRate, 1, VoiceCodec.SampleRate, true, ReadVoiceAudio);
            voiceSource.clip = voiceClip;
            voiceSource.Play();
        }

        // Audio thread: drain the queue, silence when it runs dry.
        private void ReadVoiceAudio(float[] output)
        {
            lock (audioQueueLock)
            {
                if (!audioPlaybackStarted)
                {
                    if (audioBufferedSamples < AudioPrebufferSamples)
                    {
                        Array.Clear(output, 0, output.Length);
                        return;
                    }
                    audioPlaybackStarted = true;
                }

                for (int i = 0; i < output.Length; i++)
                {
                    if (audioBufferedSamples == 0)
                    {
                        output[i] = 0f;
                    }
                    else
                    {
                        output[i] = audioQueue[audioReadPosition];
                        audioReadPosition = (audioReadPosition + 1) % audioQueue.Length;
                        audioBufferedSamples--;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (microphone != null)
                microphone.VoiceSamplesReady -= OnVoiceSamplesReady;
            if (voiceSource != null) voiceSource.Stop();
            if (voiceClip != null) Destroy(voiceClip);
        }
    }
}
