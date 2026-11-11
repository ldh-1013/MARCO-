using UnityEngine;

namespace Marco.Network.Voice
{
    /// <summary>Optional per-collider voice obstruction strength. Add manually where needed.</summary>
    public sealed class VoiceOccluder : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float strength = 0.7f;
        public float Strength => Mathf.Clamp01(strength);
    }
}
