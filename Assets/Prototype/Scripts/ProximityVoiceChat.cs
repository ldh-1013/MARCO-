using UnityEngine;

namespace Marco.Prototype
{
    /// <summary>Relays cleaned local speech and plays nearby players as 3D audio.</summary>
    [DisallowMultipleComponent]
    public sealed class ProximityVoiceChat : MonoBehaviour
    {
        private const int InputSamplesPerFrame = ProximityVoiceProtocol.SamplesPerPacket / 2 * 3;
        private const int AudioQueueCapacity = ProximityVoiceProtocol.SampleRate;

        private readonly float[] packetSamples = new float[ProximityVoiceProtocol.SamplesPerPacket];
        private readonly float[] downsampledFrame = new float[ProximityVoiceProtocol.SamplesPerPacket / 2];
        private readonly float[] audioQueue = new float[AudioQueueCapacity];
        private readonly object audioQueueLock = new();
        private int packetSampleCount;
        private int audioReadPosition;
        private int audioWritePosition;
        private uint sendSequence;
        private float lastVoiceFrameTime;
        private int playerId;
        private bool isLocal;
        private bool configured;
        private RoomSessionPrototype room;
        private MicrophoneVisionController microphone;
        private AudioSource voiceSource;
        private AudioClip voiceClip;

        public void Configure(int id, bool local)
        {
            playerId = id;
            isLocal = local;
            configured = true;
        }

        private void Start()
        {
            if (!configured) { enabled = false; return; }
            room = RoomSessionPrototype.Instance;
            if (room == null) { enabled = false; return; }
            room.VoiceFrameReceived += OnVoiceFrameReceived;

            if (isLocal)
            {
                microphone = GetComponentInChildren<MicrophoneVisionController>(true);
                if (microphone != null) microphone.VoiceSamplesReady += OnVoiceSamplesReady;
                else Debug.LogWarning("Local player has no microphone input for proximity voice.", this);
                return;
            }

            CreateSpatialVoiceSource();
        }

        private void OnVoiceSamplesReady(float[] denoisedSamples)
        {
            if (denoisedSamples == null || denoisedSamples.Length < InputSamplesPerFrame) return;
            if (Time.unscaledTime - lastVoiceFrameTime > 0.08f) packetSampleCount = 0;
            lastVoiceFrameTime = Time.unscaledTime;

            for (int i = 0; i < downsampledFrame.Length; i++)
            {
                int input = i * 3;
                float sum = 0f;
                for (int j = 0; j < 3; j++) sum += denoisedSamples[input + j];
                downsampledFrame[i] = sum / 3f;
            }

            for (int i = 0; i < downsampledFrame.Length; i++)
            {
                packetSamples[packetSampleCount++] = downsampledFrame[i];
                if (packetSampleCount != packetSamples.Length) continue;

                byte[] packet = ProximityVoiceProtocol.EncodePcm16(packetSamples);
                room.PublishVoiceFrame(playerId, ++sendSequence, packet);
                packetSampleCount = 0;
            }
        }

        private void OnVoiceFrameReceived(int senderId, byte[] pcm16)
        {
            if (isLocal || senderId != playerId || voiceSource == null || pcm16 == null) return;

            lock (audioQueueLock)
            {
                for (int i = 0; i + 1 < pcm16.Length; i += 2)
                {
                    short value = (short)(pcm16[i] | (pcm16[i + 1] << 8));
                    int nextWrite = (audioWritePosition + 1) % audioQueue.Length;
                    if (nextWrite == audioReadPosition)
                        audioReadPosition = (audioReadPosition + 1) % audioQueue.Length;
                    audioQueue[audioWritePosition] = value / 32768f;
                    audioWritePosition = nextWrite;
                }
            }
        }

        private void CreateSpatialVoiceSource()
        {
            var audioObject = new GameObject("Proximity Voice Audio");
            audioObject.transform.SetParent(transform, false);
            audioObject.transform.localPosition = Vector3.up * 1.6f;

            voiceSource = audioObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.loop = true;
            voiceSource.spatialBlend = 1f;
            voiceSource.dopplerLevel = 0f;
            voiceSource.rolloffMode = AudioRolloffMode.Linear;
            voiceSource.minDistance = 0.25f;
            voiceSource.maxDistance = room.ProximityVoiceRadius;
            voiceSource.volume = 1f;

            voiceClip = AudioClip.Create("Proximity Voice Stream", ProximityVoiceProtocol.SampleRate,
                1, ProximityVoiceProtocol.SampleRate, true, ReadVoiceAudio);
            voiceSource.clip = voiceClip;
            voiceSource.Play();
        }

        private void ReadVoiceAudio(float[] output)
        {
            lock (audioQueueLock)
            {
                for (int i = 0; i < output.Length; i++)
                {
                    if (audioReadPosition == audioWritePosition) output[i] = 0f;
                    else
                    {
                        output[i] = audioQueue[audioReadPosition];
                        audioReadPosition = (audioReadPosition + 1) % audioQueue.Length;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (room != null) room.VoiceFrameReceived -= OnVoiceFrameReceived;
            if (microphone != null) microphone.VoiceSamplesReady -= OnVoiceSamplesReady;
            if (voiceSource != null) voiceSource.Stop();
            if (voiceClip != null)
            {
                if (Application.isPlaying) Destroy(voiceClip);
                else DestroyImmediate(voiceClip);
            }
            if (voiceSource != null)
            {
                if (Application.isPlaying) Destroy(voiceSource.gameObject);
                else DestroyImmediate(voiceSource.gameObject);
            }
        }
    }
}
