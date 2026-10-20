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
                MakeUnityChanMaterialsUrpCompatible(renderer);
            }
        }

        // The UnityChan package ships Built-in Render Pipeline shaders. They have
        // no URP pass, so URP can silently omit the survivor renderers. Replace
        // only this character instance's materials; shared map materials remain untouched.
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
                    var texture = source.GetTexture("_MainTex");
                    replacement.SetTexture("_BaseMap", texture);
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
                replacement.renderQueue = transparent ? (int)RenderQueue.Transparent : (hair ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry);
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

        private void OnDestroy()
        {
            foreach (var material in runtimeMaterials) if (material != null) Destroy(material);
            runtimeMaterials.Clear();
        }
    }
}
