using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Marco.Network.Lobby
{
    // 표시 전용. Editable references in the dedicated lobby scene.
    public sealed class LobbyCctvStage : MonoBehaviour
    {
        public Camera feedCamera;
        public GameObject actorRoot;
        public AnimationClip idleClip;
        [Header("CCTV voice reveal (presentation only)")]
        [SerializeField] private Vector2 revealRadius = new(3, 24);
        [SerializeField, Min(0)] private float revealIntensity = 2.5f;
        [SerializeField, Min(.1f)] private float revealFalloff = 1.7f;
        private Renderer[] stageRenderers;
        private MaterialPropertyBlock properties;
        private PlayableGraph idle;
        private float lastLevel = -1;
        private Vector3 lastOrigin;

        private void Awake()
        {
            if (!Application.isPlaying) return;
            // Keep source coordinates convenient for editing; isolate the display at runtime.
            transform.position += new Vector3(0, -10000, 0);
            if (feedCamera != null) feedCamera.enabled = false;
            var animator = actorRoot != null ? actorRoot.GetComponentInChildren<Animator>() : null;
            if (animator != null && idleClip != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                idle = PlayableGraph.Create("CCTV idle");
                idle.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
                var clip = AnimationClipPlayable.Create(idle, idleClip);
                AnimationPlayableOutput.Create(idle, "Idle", animator).SetSourcePlayable(clip);
                idle.Play();
            }
            SetSpeech(0);
        }

        public void SetSpeech(float level)
        {
            if (actorRoot == null) return;
            stageRenderers ??= GetComponentsInChildren<Renderer>(true);
            properties ??= new MaterialPropertyBlock();
            if (feedCamera != null) feedCamera.backgroundColor = Color.black;
            level = Mathf.Clamp01(level);
            if (level < .001f) level = 0;
            Vector3 origin = actorRoot.transform.position + Vector3.up * .8f;
            if (Mathf.Approximately(lastLevel, level) && lastOrigin == origin) return;
            lastLevel = level;
            lastOrigin = origin;
            foreach (var renderer in stageRenderers)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(properties);
                // These properties are local to the lobby copy. Never touch the game's
                // microphone-light globals, so a lobby preview cannot reveal the real map.
                properties.SetFloat("_Brightness", 1);
                properties.SetVector("_RevealOrigin", origin);
                properties.SetFloat("_RevealRadius", level > 0 ? Mathf.Lerp(revealRadius.x, revealRadius.y, level) : 0);
                properties.SetFloat("_RevealIntensity", level * revealIntensity);
                properties.SetFloat("_RevealFalloff", revealFalloff);
                renderer.SetPropertyBlock(properties);
            }
        }

        private void OnEnable() { if (idle.IsValid()) idle.Play(); }
        private void OnDisable() { if (idle.IsValid()) idle.Stop(); }
        private void OnDestroy() { if (idle.IsValid()) idle.Destroy(); }
    }
}
