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

        private static Material marcoMinusCharacterTemplate;

        private readonly List<Material> runtimeMaterials = new();
        private GameObject model;
        private bool ghost;
        private bool seekerModel;

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
            bool isSeeker = role == PlayerRole.Seeker;
            if (wanted != Definition || asGhost != ghost || isSeeker != seekerModel || (wanted != null && model == null))
                Build(wanted, asGhost, isSeeker);
            if (model != null) model.SetActive(visible);
            return model != null;
        }

        private void Build(CharacterAsset asset, bool asGhost, bool isSeeker)
        {
            Clear();
            Definition = asset;
            ghost = asGhost;
            seekerModel = isSeeker;
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
                if (isSeeker) HideSeekerBall(instance.transform);
                Prepare(model.transform);
                model.SetActive(true);
                BodyAnimator = model.GetComponentInChildren<Animator>(true);
            }
            else if (asset != null) Debug.LogError($"Character asset {asset.name} has no visual prefab.", this);
            Rebuilt?.Invoke();
        }

        // The imported hunter model has a separate renderer/material for the hand-held ball.
        // Hide only that accessory on the network hunter instance and keep the rest of the mesh.
        private static void HideSeekerBall(Transform visual)
        {
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !material.name.Contains("Ball_mat")) continue;
                    renderer.enabled = false;
                    break;
                }
            }
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
                MakeCharacterMaterialsMarcoMinusCompatible(renderer);
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

        // UnityChan (Built-in, no URP pass) and URP Lit bodies would stay dark in MARCO-, since its
        // lights are off. Swap in the MarcoMinusCharacter shader, which follows the microphone light.
        // Replace only this instance's materials; shared assets stay untouched.
        private void MakeCharacterMaterialsMarcoMinusCompatible(Renderer renderer)
        {
            if (GraphicsSettings.currentRenderPipeline == null) return;
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (source == null || source.shader == null) continue;
                string shaderName = source.shader.name;
                bool unityChan = shaderName.StartsWith("UnityChan/");
                bool urpLit = shaderName == "Universal Render Pipeline/Lit";
                if (!unityChan && !urpLit) continue;

                if (marcoMinusCharacterTemplate == null)
                {
                    marcoMinusCharacterTemplate = Resources.Load<Material>("CharacterAssets/MarcoMinusCharacter");
                    if (marcoMinusCharacterTemplate == null || marcoMinusCharacterTemplate.shader == null)
                    {
                        Debug.LogError("Missing Resources/CharacterAssets/MarcoMinusCharacter; character body cannot follow MARCO- lighting.", this);
                        return;
                    }
                }

                var replacement = new Material(marcoMinusCharacterTemplate) { name = source.name + " (MARCO- runtime)" };
                string textureProperty = source.HasProperty("_BaseMap") ? "_BaseMap" : source.HasProperty("_MainTex") ? "_MainTex" : null;
                if (textureProperty != null)
                {
                    replacement.SetTexture("_BaseMap", source.GetTexture(textureProperty));
                    replacement.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
                    replacement.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
                }
                string colorProperty = source.HasProperty("_BaseColor") ? "_BaseColor" : source.HasProperty("_Color") ? "_Color" : null;
                if (colorProperty != null) replacement.SetColor("_BaseColor", source.GetColor(colorProperty));

                bool hair = shaderName.Contains("Hair");
                bool transparent = shaderName.Contains("Transparent")
                    || (source.HasProperty("_Surface") && source.GetFloat("_Surface") > 0.5f);
                bool alphaClip = hair || (source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f);
                float cutoff = source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.25f;
                float cull = source.HasProperty("_Cull") ? source.GetFloat("_Cull") : (hair || shaderName.Contains("Double-sided") ? 0f : 2f);
                replacement.SetFloat("_Surface", transparent ? 1f : 0f);
                replacement.SetFloat("_AlphaClip", alphaClip ? 1f : 0f);
                replacement.SetFloat("_Cutoff", cutoff);
                replacement.SetFloat("_Cull", cull);
                replacement.SetFloat("_SrcBlend", transparent ? (float)BlendMode.SrcAlpha : (float)BlendMode.One);
                replacement.SetFloat("_DstBlend", transparent ? (float)BlendMode.OneMinusSrcAlpha : (float)BlendMode.Zero);
                replacement.SetFloat("_ZWrite", transparent ? 0f : 1f);
                replacement.renderQueue = transparent ? (int)RenderQueue.Transparent : (hair ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry);
                if (transparent) replacement.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                else replacement.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (alphaClip) replacement.EnableKeyword("_ALPHATEST_ON");
                else replacement.DisableKeyword("_ALPHATEST_ON");
                replacement.SetOverrideTag("RenderType", transparent ? "Transparent" : alphaClip ? "TransparentCutout" : "Opaque");
                runtimeMaterials.Add(replacement);
                materials[i] = replacement;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }

        private void OnDestroy() => Clear();
    }
}
