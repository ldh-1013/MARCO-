using UnityEngine;

namespace Marco.Network.Voice
{
    [CreateAssetMenu(menuName = "MARCO/Voice Occlusion Settings", fileName = "VoiceOcclusionSettings")]
    public sealed class VoiceOcclusionSettings : ScriptableObject
    {
        private static VoiceOcclusionSettings cached;

        [Header("Master")]
        [SerializeField] private bool enabled = true;

        [Header("Occlusion query")]
        [SerializeField] private LayerMask environmentLayers = 1;
        [SerializeField, Range(0f, 1f)] private float defaultWallStrength = 0.7f;
        [SerializeField] private bool useVoiceOccluderStrengthWhenPresent = true;
        [SerializeField, Min(1f)] private float checksPerSecond = 15f;
        [SerializeField, Min(0f)] private float sideRayOffset = 0.3f;
        [SerializeField, Range(0f, 1f)] private float averageRayWeight = 0.5f;
        [SerializeField, Min(0.01f)] private float activeVoiceHoldSeconds = 0.15f;
        [SerializeField, Min(0f)] private float speakerMouthHeight = 0.6f;

        [Header("Muffle")]
        [SerializeField, Min(100f)] private float openCutoffHz = 22000f;
        [SerializeField, Min(100f)] private float closedCutoffHz = 600f;
        [SerializeField, Range(0f, 40f)] private float maximumAttenuationDb = 10f;

        [Header("Smoothing")]
        [SerializeField, Min(0.001f)] private float occludingTimeConstant = 0.08f;
        [SerializeField, Min(0.001f)] private float openingTimeConstant = 0.12f;

        public bool Enabled => enabled;
        public LayerMask EnvironmentLayers => environmentLayers;
        public float DefaultWallStrength => defaultWallStrength;
        public bool UseVoiceOccluderStrengthWhenPresent => useVoiceOccluderStrengthWhenPresent;
        public float ChecksPerSecond => checksPerSecond;
        public float SideRayOffset => sideRayOffset;
        public float AverageRayWeight => averageRayWeight;
        public float ActiveVoiceHoldSeconds => activeVoiceHoldSeconds;
        public float SpeakerMouthHeight => speakerMouthHeight;
        public float OpenCutoffHz => openCutoffHz;
        public float ClosedCutoffHz => closedCutoffHz;
        public float MaximumAttenuationDb => maximumAttenuationDb;
        public float OccludingTimeConstant => occludingTimeConstant;
        public float OpeningTimeConstant => openingTimeConstant;

        public static VoiceOcclusionSettings Load()
        {
            if (cached == null) cached = Resources.Load<VoiceOcclusionSettings>("VoiceOcclusionSettings");
            return cached;
        }

        public static bool IsEnabled
        {
            get
            {
                VoiceOcclusionSettings settings = Load();
                return settings != null && settings.Enabled;
            }
        }
    }
}
