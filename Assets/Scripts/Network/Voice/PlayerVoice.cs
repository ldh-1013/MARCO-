using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using Marco.Core.WinCondition;
using Marco.Network.Player;
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
        private const int AudioPrebufferSamples = VoiceCodec.SampleRate / 25; // 40 ms absorbs packet jitter.

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
            var packet = VoiceCodec.Encode(samples);
            if (packet != null) SendVoiceServer(packet, grade, Channel.Unreliable);
        }

        // Unreliable: a late voice frame is worse than a lost one.
        [ServerRpc]
        private void SendVoiceServer(byte[] packet, VoiceGrade grade, Channel channel = Channel.Unreliable)
        {
            if (packet == null || packet.Length != VoiceCodec.SamplesPerPacket) return;
            foreach (var listener in NetworkPlayer.All)
            {
                if (listener == player || !routing.TryGetHearing(player, listener, grade, out float radius)) continue;
                PlayVoiceTarget(listener.Owner, packet, radius, Channel.Unreliable);
            }
        }

        [TargetRpc]
        private void PlayVoiceTarget(NetworkConnection conn, byte[] packet, float radius, Channel channel = Channel.Unreliable)
        {
            if (IsOwner || packet == null) return;
            EnsureVoiceSource();
            // Proximity voice fades out over the radius the server allowed; channel-wide voice is flat.
            voiceSource.spatialBlend = radius > 0 ? 1f : 0f;
            if (radius > 0) voiceSource.maxDistance = radius;

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
                        audioPlaybackStarted = false;
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
