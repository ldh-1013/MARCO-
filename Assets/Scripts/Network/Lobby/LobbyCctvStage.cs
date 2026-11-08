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
        private Renderer[] actorRenderers;
        private MaterialPropertyBlock properties;
        private PlayableGraph idle;

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
            actorRenderers ??= actorRoot.GetComponentsInChildren<Renderer>(true);
            properties ??= new MaterialPropertyBlock();
            foreach (var renderer in actorRenderers)
            {
                renderer.GetPropertyBlock(properties);
                properties.SetFloat("_Brightness", Mathf.Lerp(.12f, 1.65f, Mathf.Clamp01(level)));
                renderer.SetPropertyBlock(properties);
            }
        }

        private void OnEnable() { if (idle.IsValid()) idle.Play(); }
        private void OnDisable() { if (idle.IsValid()) idle.Stop(); }
        private void OnDestroy() { if (idle.IsValid()) idle.Destroy(); }
    }
}
