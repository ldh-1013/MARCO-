using System;
using System.Collections.Generic;
using Marco.Core.WinCondition;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Character
{
    // Builds the role's registered character model under the player, replacing the capsule.
    // Ported from Prototype CharacterVisualPrototype; rebuilt whenever the role changes,
    // because the Seeker is only drawn at round start. An Echo gets the same model with see-through
    // ghost materials (Prototype SurvivorGhostPrototype).
    public sealed class CharacterBody : MonoBehaviour
    {
        [SerializeField] private CharacterAsset seekerAsset;
        [SerializeField] private CharacterAsset runnerAsset;
        [SerializeField] private Color ghostTint = new(0.65f, 0.25f, 1f, 0.3f);

        private readonly List<Material> runtimeMaterials = new();
        private GameObject model;
        private bool ghost;

        public CharacterAsset Definition { get; private set; }
        public Transform BodyRoot => model == null ? null : model.transform;
        public Animator BodyAnimator { get; private set; }

        // Fired after the model is replaced (or removed) so motion and first-person view can rebind.
        public event Action Rebuilt;

        public Transform ResolveBone(HumanBodyBones bone) =>
            Definition == null ? null : Definition.Resolve(BodyRoot, BodyAnimator, bone);

        // Returns false when the role has no registered model, so the caller keeps the capsule.
        public bool Show(PlayerRole role, bool visible, bool asGhost)
        {
            var wanted = role == PlayerRole.Seeker ? seekerAsset : runnerAsset;
            if (wanted != Definition || asGhost != ghost || (wanted != null && model == null)) Build(wanted, asGhost);
            if (model != null) model.SetActive(visible);
            return model != null;
        }

        private void Build(CharacterAsset asset, bool asGhost)
        {
            Clear();
            Definition = asset;
            ghost = asGhost;
            if (asset != null && asset.visualPrefab != null)
            {
                // Prepare an inactive anchor so the imported demo scripts never run.
                model = new GameObject("Character: " + asset.name);
                model.SetActive(false);
                model.transform.SetParent(transform, false);
                model.transform.localPosition = asset.modelOffset;
                model.transform.localScale = asset.modelScale;
                var instance = Instantiate(asset.visualPrefab, model.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                Prepare(model.transform);
                model.SetActive(true);
                BodyAnimator = model.GetComponentInChildren<Animator>(true);
            }
            else if (asset != null) Debug.LogError($"Character asset {asset.name} has no visual prefab.", this);
            Rebuilt?.Invoke();
        }

        private void Clear()
        {
            if (model != null) { model.SetActive(false); Destroy(model); }
            model = null;
            BodyAnimator = null;
            Definition = null;
            foreach (var material in runtimeMaterials) if (material != null) Destroy(material);
            runtimeMaterials.Clear();
        }

        // Imported colliders would fight the CharacterController, and their cameras/listeners the owner's.
        private void Prepare(Transform visual)
        {
            foreach (var script in visual.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
            foreach (var camera in visual.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var listener in visual.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var animator in visual.GetComponentsInChildren<Animator>(true)) animator.applyRootMotion = false;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                MakeUnityChanMaterialsUrpCompatible(renderer);
                if (ghost) MakeGhostMaterials(renderer);
            }
        }

        // Translucent URP Lit copies that keep each material's texture, tinted with ghostTint.
        private void MakeGhostMaterials(Renderer renderer)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null) continue;
                var material = lit != null && lit.isSupported ? new Material(lit) : new Material(source);
                material.name = source.name + " (ghost)";
                Texture texture = source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap")
                    : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
                if (texture != null && material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", ghostTint);
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.SetFloat("_Cull", 0f);
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.DisableKeyword("_ALPHATEST_ON");
                runtimeMaterials.Add(material);
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        // The UnityChan package ships Built-in Render Pipeline shaders with no URP pass.
        // Replace only this instance's materials; shared assets stay untouched.
        private void MakeUnityChanMaterialsUrpCompatible(Renderer renderer)
        {
            if (GraphicsSettings.currentRenderPipeline == null) return;
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null || source.shader == null || !source.shader.name.StartsWith("UnityChan/")) continue;
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit == null || !lit.isSupported) continue;
                var replacement = new Material(lit) { name = source.name + " (URP runtime)" };
                if (source.HasProperty("_MainTex"))
                {
                    replacement.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                    replacement.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
                    replacement.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
                }
                if (source.HasProperty("_Color")) replacement.SetColor("_BaseColor", source.GetColor("_Color"));
                bool transparent = source.shader.name.Contains("Transparent");
                bool hair = source.shader.name.Contains("Hair");
                replacement.SetFloat("_Surface", transparent ? 1f : 0f);
                replacement.SetFloat("_AlphaClip", hair ? 1f : 0f);
                replacement.SetFloat("_Cutoff", 0.25f);
                replacement.SetFloat("_Cull", source.shader.name.Contains("Double-sided") || hair ? 0f : 2f);
                replacement.SetFloat("_ZWrite", transparent ? 0f : 1f);
                replacement.renderQueue = transparent ? (int)RenderQueue.Transparent : hair ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
                if (transparent) replacement.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                else replacement.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (hair) replacement.EnableKeyword("_ALPHATEST_ON");
                else replacement.DisableKeyword("_ALPHATEST_ON");
                replacement.SetOverrideTag("RenderType", transparent ? "Transparent" : hair ? "TransparentCutout" : "Opaque");
                runtimeMaterials.Add(replacement);
                materials[i] = replacement;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }

        private void OnDestroy() => Clear();
    }
}
