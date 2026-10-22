using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Prototype
{
    [DefaultExecutionOrder(-800)]
    public sealed class CharacterVisualPrototype : MonoBehaviour
    {
        [Tooltip("Registered asset name under Resources/CharacterAssets. Change this one field to replace the body.")]
        [SerializeField] private string characterAssetName = "";
        [SerializeField] private Transform[] visuals = new Transform[0];
        [SerializeField] private bool hideFromFirstPerson = true;
        private readonly Dictionary<Renderer, int> originalLayers = new Dictionary<Renderer, int>();
        private readonly List<Material> runtimeMaterials = new List<Material>();
        private static Material marcoMinusCharacterTemplate;
        private bool initialized, failed;
        public string AssetName => characterAssetName;
        public CharacterAssetDefinition Definition { get; private set; }
        public bool IsFirstPerson => hideFromFirstPerson;
        public Transform BodyRoot => failed || visuals.Length == 0 ? null : visuals[0];
        public Animator BodyAnimator => BodyRoot == null ? null : BodyRoot.GetComponentInChildren<Animator>(true);
        public Transform ResolveBone(HumanBodyBones bone) => Definition == null ? null : Definition.Resolve(BodyRoot, BodyAnimator, bone);
        public void SetAssetNameBeforeInitialization(string name)
        {
            if (initialized) { Debug.LogError("Configure network asset before character Awake.", this); return; }
            characterAssetName = name;
        }

        public void SetFirstPerson(bool value)
        {
            hideFromFirstPerson = value;
            foreach (var pair in originalLayers)
            {
                if (pair.Key == null) continue;
                bool body = BodyRoot != null && pair.Key.transform.IsChildOf(BodyRoot);
                // Keep the actual body visible. Only the alternate ghost hides from its owner.
                pair.Key.gameObject.layer = value && !body ? 2 : pair.Value;
            }
        }
        public void SetBodyVisible(bool value) { if (BodyRoot != null) BodyRoot.gameObject.SetActive(value); }
        private void Awake() { Initialize(); }
        public void Initialize()
        {
            if (initialized) return; initialized = true;
            if (!string.IsNullOrEmpty(characterAssetName))
            {
                Definition = CharacterAssetDefinition.Find(characterAssetName);
                if (Definition == null || Definition.visualPrefab == null)
                {
                    failed = true;
                    foreach (var visual in visuals) if (visual != null) visual.gameObject.SetActive(false);
                    Debug.LogError("Unregistered character asset: " + characterAssetName + ". Register its prefab first.", this);
                    return;
                }
                // Prepare an inactive visual anchor before allowing imported components to run.
                var anchor = new GameObject("Character Asset: " + characterAssetName);
                anchor.SetActive(false); anchor.transform.SetParent(transform, false);
                anchor.transform.localPosition = Definition.modelOffset;
                anchor.transform.localScale = Definition.modelScale;
                var model = Instantiate(Definition.visualPrefab, anchor.transform, false);
                model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                Prepare(anchor.transform);
                if (visuals.Length == 0) visuals = new Transform[1];
                if (visuals[0] != null) { visuals[0].gameObject.SetActive(false); Destroy(visuals[0].gameObject); }
                visuals[0] = anchor.transform; anchor.SetActive(true);
            }
            foreach (var visual in visuals) if (visual != null) Prepare(visual);
            SetFirstPerson(hideFromFirstPerson);
            if (Definition != null && GetComponent<CharacterMotionPresenter>() == null)
                gameObject.AddComponent<CharacterMotionPresenter>();
        }
        private void Prepare(Transform visual)
        {
            foreach (var script in visual.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in visual.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
            foreach (var camera in visual.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var audio in visual.GetComponentsInChildren<AudioListener>(true)) audio.enabled = false;
            foreach (var animator in visual.GetComponentsInChildren<Animator>(true)) animator.applyRootMotion = false;
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (!originalLayers.ContainsKey(renderer)) originalLayers.Add(renderer, renderer.gameObject.layer);
                MakeCharacterMaterialsMarcoMinusCompatible(renderer);
            }
        }

        // Player bodies are rendered in first person, so their surfaces must use
        // the same microphone-driven world attenuation as the surrounding map.
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
                        Debug.LogError("Missing Resources/CharacterAssets/MarcoMinusCharacter; first-person body cannot follow MARCO- lighting.", this);
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

        private void OnDestroy()
        {
            foreach (var material in runtimeMaterials) if (material != null) Destroy(material);
            runtimeMaterials.Clear();
        }
    }
}
