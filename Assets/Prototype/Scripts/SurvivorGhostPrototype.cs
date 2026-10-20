using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace Marco.Prototype
{
    [RequireComponent(typeof(PrototypeTarget))]
    public sealed class SurvivorGhostPrototype : MonoBehaviour
    {
        [SerializeField] private GameObject survivorVisual = null;
        [SerializeField] private GameObject ghostVisual = null;
        [SerializeField] private bool developmentKeys = true;
        private PrototypeTarget target;
        private CharacterController controller;
        private FirstPersonBodyView bodyView;
        private Camera playerCamera;
        private bool ghost;
        private bool localPlayer = true;
        private readonly List<Material> runtimeGhostMaterials = new List<Material>();
        public bool IsGhost => ghost;
        public GameObject GhostVisual => ghostVisual;
        public void SetLocalPlayer(bool value) { localPlayer = value; }
        private void Awake()
        {
            target = GetComponent<PrototypeTarget>(); controller = GetComponent<CharacterController>();
            bodyView = GetComponent<FirstPersonBodyView>();
            playerCamera = GetComponentInChildren<Camera>(true);
            PrepareGhostMaterials();
            Apply(false);
        }

        private void PrepareGhostMaterials()
        {
            if (ghostVisual == null) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var renderer in ghostVisual.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    var source = shared[i];
                    if (source == null) continue;
                    var material = lit != null && lit.isSupported ? new Material(lit) : new Material(source);
                    material.name = source.name + " (visible ghost instance)";
                    // The source Shader Graph combines a dissolve alpha with a
                    // transparent surface. Use its texture/tint with URP Lit so
                    // the dissolve default cannot erase the in-game ghost.
                    Texture texture = source.HasProperty("_MainTexture") ? source.GetTexture("_MainTexture")
                        : source.HasProperty("_BaseMap") ? source.GetTexture("_BaseMap")
                        : source.HasProperty("_MainTex") ? source.GetTexture("_MainTex") : null;
                    if (texture != null && material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
                    Color tint = source.HasProperty("_Color") ? source.GetColor("_Color")
                        : source.HasProperty("_MainColor") ? source.GetColor("_MainColor") : new Color(.65f, .25f, 1f, 1f);
                    tint.a = .3f;
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
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
                    runtimeGhostMaterials.Add(material);
                    shared[i] = material;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = shared;
            }
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (localPlayer && developmentKeys && Input.GetKeyDown(KeyCode.F6)) target.Tag();
            if (localPlayer && developmentKeys && Input.GetKeyDown(KeyCode.F7)) target.ResetHealth();
            if (localPlayer && developmentKeys && Input.GetKeyDown(KeyCode.F9)) SetGhostForDevelopment(true);
#endif
            if (ghost != target.Health.IsEcho) Apply(target.Health.IsEcho);
        }

        private void LateUpdate()
        {
            // The owner has no first-person ghost rig. Move the camera outside
            // the active ghost mesh so the actual asset is visible in-game.
            if (ghost && localPlayer && playerCamera != null)
                playerCamera.transform.position = transform.position - transform.forward * 3.5f + Vector3.up * 1.35f;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void SetGhostForDevelopment(bool value)
        {
            if (target == null) target = GetComponent<PrototypeTarget>();
            if (target == null) return;
            target.ResetHealth();
            if (value) { target.Tag(); target.Tag(); }
            Apply(target.Health.IsEcho);
        }
#endif
        private void Apply(bool value)
        {
            ghost = value;
            var visual = GetComponent<CharacterVisualPrototype>();
            if (visual != null && visual.BodyRoot != null) survivorVisual = visual.BodyRoot.gameObject;
            // The alternate ghost visual is put on Ignore Raycast for a local
            // first-person survivor. Restore its normal layer while it is active,
            // then reapply first-person filtering when the survivor returns.
            if (visual != null) visual.SetFirstPerson(localPlayer && !ghost);
            if (bodyView != null && localPlayer) bodyView.enabled = !ghost;
            if (survivorVisual != null) survivorVisual.SetActive(!ghost);
            if (ghostVisual != null) ghostVisual.SetActive(ghost);
            if (ghost && ghostVisual != null)
                foreach (var renderer in ghostVisual.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.enabled = true;
                    // Keep the ghost in the camera's visible layer. Its cloned
                    // URP material is translucent so the owner can see through it.
                    renderer.gameObject.layer = gameObject.layer;
                }
            if (controller != null) controller.enabled = !ghost;
            foreach (var collider in GetComponentsInChildren<Collider>(true)) if (collider != controller) collider.enabled = !ghost;
        }
        private void OnGUI()
        {
            if (!localPlayer) return;
            GUI.depth = -2;
            GUI.Label(new Rect(35, 90, 600, 30), ghost ? "고스트 상태 · 충돌 없이 이동 · Space 상승 / Ctrl 하강" : "생존자 · 목숨 " + target.Health.Lives.ToString("F1"));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            GUI.Label(new Rect(35, 120, 750, 30), "F6 태그 테스트 · F7 초기화 · F9 즉시 고스트");
#endif
        }

        private void OnDestroy()
        {
            foreach (var material in runtimeGhostMaterials) if (material != null) Destroy(material);
            runtimeGhostMaterials.Clear();
        }
    }
}
