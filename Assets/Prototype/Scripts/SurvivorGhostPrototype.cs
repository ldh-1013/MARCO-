using UnityEngine;

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
        private bool ghost;
        private bool localPlayer = true;
        public void SetLocalPlayer(bool value) { localPlayer = value; }
        private void Awake()
        {
            target = GetComponent<PrototypeTarget>(); controller = GetComponent<CharacterController>();
            Apply(false);
        }
        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (localPlayer && developmentKeys && Input.GetKeyDown(KeyCode.F6)) target.Tag();
            if (localPlayer && developmentKeys && Input.GetKeyDown(KeyCode.F7)) target.ResetHealth();
            if (localPlayer && developmentKeys && Input.GetKeyDown(KeyCode.F9)) { if (!target.Health.IsEcho) { target.Tag(); target.Tag(); } }
#endif
            if (ghost != target.Health.IsEcho) Apply(target.Health.IsEcho);
        }
        private void Apply(bool value)
        {
            ghost = value;
            var visual = GetComponent<CharacterVisualPrototype>();
            if (visual != null && visual.BodyRoot != null) survivorVisual = visual.BodyRoot.gameObject;
            if (survivorVisual != null) survivorVisual.SetActive(!ghost);
            if (ghostVisual != null) ghostVisual.SetActive(ghost);
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
    }
}
