using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network.Voice
{
    /// <summary>Runtime-only discovery and attachment for remote PlayerVoice audio children.</summary>
    public sealed class VoiceOcclusionRuntime : MonoBehaviour
    {
        private const float ScanIntervalSeconds = 0.1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const KeyCode DebugKey = KeyCode.F11;
        private static bool showDebug;
        private static int lastToggleFrame = -1;
#endif
        private static VoiceOcclusionRuntime instance;
        private readonly List<VoiceOcclusionReceiver> receivers = new(8);
        private VoiceOcclusionSettings settings;
        private float nextScanTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            VoiceOcclusionSettings configuration = VoiceOcclusionSettings.Load();
            if (configuration == null || !configuration.Enabled || instance != null) return;
            var root = new GameObject("Voice Occlusion Runtime");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<VoiceOcclusionRuntime>();
            instance.settings = configuration;
        }

        private void Update()
        {
            if (settings == null || !settings.Enabled)
            {
                RemoveAllReceivers();
                instance = null;
                Destroy(gameObject);
                return;
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (lastToggleFrame != Time.frameCount && Input.GetKeyDown(DebugKey))
            {
                lastToggleFrame = Time.frameCount;
                showDebug = !showDebug;
            }
#endif
            if (Time.unscaledTime < nextScanTime) return;
            nextScanTime = Time.unscaledTime + ScanIntervalSeconds;
            DiscoverVoiceSources();
        }

        private void DiscoverVoiceSources()
        {
            AudioSource[] sources = FindObjectsByType<AudioSource>();
            AudioListener listener = FindAnyObjectByType<AudioListener>();
            Transform localRoot = listener != null ? listener.transform.root : null;
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = sources[i];
                if (source == null || source.name != "Voice Audio" || source.clip == null ||
                    source.clip.name != "Voice Stream" || source.transform.parent == null)
                    continue;
                if (localRoot != null && source.transform.root == localRoot) continue;
                if (source.GetComponent<VoiceOcclusionFilter>() != null) continue;

                // Add the in-place DSP after existing components on the source. This PlayerVoice
                // source has no PCM injection script; its AudioClip callback supplies the stream.
                VoiceOcclusionFilter filter = source.gameObject.AddComponent<VoiceOcclusionFilter>();
                filter.Configure(settings, AudioSettings.outputSampleRate);
                VoiceOcclusionReceiver receiver = source.gameObject.AddComponent<VoiceOcclusionReceiver>();
                receiver.Initialize(settings, filter, source.transform.parent);
                receivers.Add(receiver);
            }
            receivers.RemoveAll(receiver => receiver == null);
        }

        private void RemoveAllReceivers()
        {
            for (int i = receivers.Count - 1; i >= 0; i--)
            {
                VoiceOcclusionReceiver receiver = receivers[i];
                if (receiver == null) continue;
                GameObject sourceObject = receiver.gameObject;
                VoiceOcclusionFilter filter = sourceObject.GetComponent<VoiceOcclusionFilter>();
                if (filter != null) Destroy(filter);
                Destroy(receiver);
            }
            receivers.Clear();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (!showDebug) return;
            int row = 0;
            GUI.Box(new Rect(10, 100, 470, 28 + receivers.Count * 22), "Voice occlusion · F11 hide");
            for (int i = 0; i < receivers.Count; i++)
            {
                VoiceOcclusionReceiver receiver = receivers[i];
                if (receiver == null || !receiver.DebugActive) continue;
                GUI.Label(new Rect(20, 125 + row++ * 22, 450, 20),
                    $"{receiver.DebugSpeakerName} {receiver.DebugDistance:0.0}m · O {receiver.DebugOcclusion:0.00} · " +
                    $"{receiver.DebugCutoffHz:0}Hz · {receiver.DebugGainDb:0.0}dB");
            }
        }
#endif

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
