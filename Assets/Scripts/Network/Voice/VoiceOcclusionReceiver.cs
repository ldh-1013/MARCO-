using UnityEngine;

namespace Marco.Network.Voice
{
    /// <summary>Samples local listener-to-speaker obstruction for one remote voice source.</summary>
    [DisallowMultipleComponent]
    public sealed class VoiceOcclusionReceiver : MonoBehaviour
    {
        private static int initializationCounter;
        private readonly RaycastHit[] hitBuffer = new RaycastHit[32];
        private readonly float[] rayOcclusion = new float[3];
        private readonly Vector3[] rayTargets = new Vector3[3];
        private VoiceOcclusionSettings settings;
        private VoiceOcclusionFilter filter;
        private Transform speakerRoot;
        private AudioListener listener;
        private float nextRaycastTime;
        private float nextListenerSearchTime;
        private float lastVoiceSignalTime = float.NegativeInfinity;
        private float occlusion;
        private float lastCutoffHz;
        private float lastGainDb;
        private bool hasRayData;

        public void Initialize(VoiceOcclusionSettings configuration, VoiceOcclusionFilter dsp, Transform speaker)
        {
            settings = configuration;
            filter = dsp;
            speakerRoot = speaker;
            lastCutoffHz = settings != null ? settings.OpenCutoffHz : 22000f;
            ResolveListener();
            int phase = initializationCounter++ & 7;
            nextRaycastTime = Time.unscaledTime + phase / (8f * ChecksPerSecond);
        }

        private float ChecksPerSecond => settings != null ? Mathf.Max(1f, settings.ChecksPerSecond) : 15f;

        private void Update()
        {
            if (filter == null || speakerRoot == null) return;
            if ((listener == null || !listener.isActiveAndEnabled) && Time.unscaledTime >= nextListenerSearchTime)
            {
                nextListenerSearchTime = Time.unscaledTime + 0.5f;
                ResolveListener();
            }
            if (filter.HasVoiceSignal) lastVoiceSignalTime = Time.unscaledTime;
            bool voiceActive = Time.unscaledTime - lastVoiceSignalTime <= ActiveVoiceHoldSeconds;
            if (listener == null || !voiceActive)
            {
                SmoothToward(0f);
                return;
            }

            if (Time.unscaledTime >= nextRaycastTime)
            {
                nextRaycastTime = Time.unscaledTime + 1f / ChecksPerSecond;
                SampleOcclusion();
            }
            SmoothToward(hasRayData ? CalculateCombinedOcclusion() : 0f);
        }

        private void SampleOcclusion()
        {
            Vector3 origin = listener.transform.position;
            Vector3 target = speakerRoot.position + Vector3.up * SpeakerMouthHeight;
            Vector3 side = listener.transform.right * SideRayOffset;
            rayTargets[0] = target;
            rayTargets[1] = target - side;
            rayTargets[2] = target + side;
            for (int i = 0; i < rayTargets.Length; i++) rayOcclusion[i] = RayOcclusion(origin, rayTargets[i]);
            hasRayData = true;
        }

        private float RayOcclusion(Vector3 origin, Vector3 target)
        {
            Vector3 delta = target - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f) return 0f;
            int count = Physics.RaycastNonAlloc(origin, delta / distance, hitBuffer, distance,
                EnvironmentLayers, QueryTriggerInteraction.Ignore);
            if (count >= hitBuffer.Length) return 1f;

            float transmission = 1f;
            Transform listenerRoot = listener.transform.root;
            Transform speakerTransformRoot = speakerRoot.root;
            for (int i = 0; i < count; i++)
            {
                Collider hit = hitBuffer[i].collider;
                if (hit == null) continue;
                Transform hitTransform = hit.transform;
                if (hitTransform == listenerRoot || hitTransform.IsChildOf(listenerRoot) ||
                    hitTransform == speakerTransformRoot || hitTransform.IsChildOf(speakerTransformRoot) ||
                    hit.GetComponentInParent<CharacterController>() != null)
                    continue;

                VoiceOccluder custom = UseCustomStrength ? hit.GetComponentInParent<VoiceOccluder>() : null;
                float strength = custom != null ? custom.Strength : DefaultWallStrength;
                transmission *= 1f - Mathf.Clamp01(strength);
            }
            return 1f - transmission;
        }

        private float CalculateCombinedOcclusion()
        {
            float average = (rayOcclusion[0] + rayOcclusion[1] + rayOcclusion[2]) / 3f;
            float mostOpen = Mathf.Min(rayOcclusion[0], Mathf.Min(rayOcclusion[1], rayOcclusion[2]));
            return Mathf.Lerp(mostOpen, average, AverageRayWeight);
        }

        private void SmoothToward(float target)
        {
            float tau = target > occlusion ? OccludingTimeConstant : OpeningTimeConstant;
            float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.001f, tau));
            occlusion += (target - occlusion) * blend;

            float open = OpenCutoffHz;
            float closed = Mathf.Min(ClosedCutoffHz, open);
            lastCutoffHz = open * Mathf.Pow(closed / open, occlusion);
            lastGainDb = -MaximumAttenuationDb * occlusion;
            if (filter != null)
                filter.SetTargets(occlusion, lastCutoffHz, Mathf.Pow(10f, lastGainDb / 20f));
        }

        private void ResolveListener()
        {
            AudioListener found = FindAnyObjectByType<AudioListener>();
            if (found != null && found.isActiveAndEnabled && found.transform.root != speakerRoot.root)
                listener = found;
            else listener = null;
        }

        private LayerMask EnvironmentLayers => settings != null ? settings.EnvironmentLayers : Physics.DefaultRaycastLayers;
        private float DefaultWallStrength => settings != null ? settings.DefaultWallStrength : 0.7f;
        private bool UseCustomStrength => settings != null && settings.UseVoiceOccluderStrengthWhenPresent;
        private float SideRayOffset => settings != null ? settings.SideRayOffset : 0.3f;
        private float AverageRayWeight => settings != null ? settings.AverageRayWeight : 0.5f;
        private float SpeakerMouthHeight => settings != null ? settings.SpeakerMouthHeight : 0.6f;
        private float ActiveVoiceHoldSeconds => settings != null ? settings.ActiveVoiceHoldSeconds : 0.15f;
        private float OpenCutoffHz => settings != null ? settings.OpenCutoffHz : 22000f;
        private float ClosedCutoffHz => settings != null ? settings.ClosedCutoffHz : 600f;
        private float MaximumAttenuationDb => settings != null ? settings.MaximumAttenuationDb : 10f;
        private float OccludingTimeConstant => settings != null ? settings.OccludingTimeConstant : 0.08f;
        private float OpeningTimeConstant => settings != null ? settings.OpeningTimeConstant : 0.12f;

        private void OnDrawGizmos()
        {
            if (!hasRayData || listener == null) return;
            Vector3 origin = listener.transform.position;
            for (int i = 0; i < rayTargets.Length; i++)
            {
                Gizmos.color = rayOcclusion[i] > 0.001f ? Color.red : Color.green;
                Gizmos.DrawLine(origin, rayTargets[i]);
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public string DebugSpeakerName => speakerRoot != null
            ? $"{speakerRoot.name} [{speakerRoot.GetSiblingIndex()}]" : "unknown";
        public float DebugOcclusion => occlusion;
        public float DebugCutoffHz => lastCutoffHz;
        public float DebugGainDb => lastGainDb;
        public float DebugDistance => listener != null && speakerRoot != null
            ? Vector3.Distance(listener.transform.position, speakerRoot.position) : 0f;
        public bool DebugActive => filter != null && filter.HasVoiceSignal;
#endif
    }
}
