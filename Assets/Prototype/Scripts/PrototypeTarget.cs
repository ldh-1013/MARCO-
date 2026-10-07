using UnityEngine;

namespace Marco.Prototype
{
    public sealed class PrototypeTarget : MonoBehaviour
    {
        [SerializeField] private bool isSurvivor = true;
        [SerializeField] private bool underwater = false;
        [SerializeField] private bool showDebugState = true;
        [SerializeField] private float moanInterval = 6f;
        private readonly SurvivorHealth health = new SurvivorHealth();
        private float nextMoan;
        private float flashUntil;
        private float moanUntil;
        private Renderer[] targetRenderers;
        private MaterialPropertyBlock properties;
        public bool IsTagged => health.Lives < 2f;
        public bool CanBeTagged => isSurvivor && !health.IsEcho;
        public SurvivorHealth Health => health;
        public float MoanRadius => underwater ? 4.5f : 9f;
        public float MoanDuration => 1.2f;
        public float LastHitTime { get; private set; } = -1;

        private void Awake() { targetRenderers = GetComponentsInChildren<Renderer>(true); properties = new MaterialPropertyBlock(); }
        public bool Tag()
        {
            if (!CanBeTagged || !health.Hit()) return false;
            LastHitTime = Time.time; nextMoan = Time.time + moanInterval; flashUntil = Time.time + 0.2f;
            Debug.Log("[Tag] " + name + " lives=" + health.Lives + " state=" + StateName, this);
            return true;
        }
        public void Treat() { health.Treat(); }
        [ContextMenu("Debug/Apply bandage +0.5 (effect only)")]
        private void DebugBandage() { health.Bandage(); }
        [ContextMenu("Debug/Reset target")]
        private void ResetTarget() { health.Reset(); flashUntil = 0; moanUntil = 0; }
        public void ResetHealth() { ResetTarget(); }
        private string StateName => health.IsEcho ? "메아리" : health.IsWounded ? "상처" : health.Lives == 1.5f ? "회복 중" : "정상";
        private void Update()
        {
            if (health.IsWounded && Time.time >= nextMoan)
            {
                nextMoan = Time.time + moanInterval; moanUntil = Time.time + MoanDuration;
                Debug.Log("[Moan] " + name + " radius=" + MoanRadius + " hunterRadius=" + MoanRadius * 1.2f + " duration=1.2", this);
            }
            foreach (var targetRenderer in targetRenderers)
            {
                if (targetRenderer == null || !targetRenderer.enabled) continue;
                Color color = Time.time < flashUntil ? Color.white : health.IsEcho ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.9f, 0.9f, 0.9f);
                properties.SetColor("_Color", color); properties.SetColor("_BaseColor", color); targetRenderer.SetPropertyBlock(properties);
            }
        }
        private void OnGUI()
        {
            if (!showDebugState) return;
            var camera = Camera.main; if (camera == null) return;
            Vector3 p = camera.WorldToScreenPoint(transform.position + Vector3.up * 1.2f);
            if (p.z <= 0) return;
            GUI.depth = -1;
            GUI.Box(new Rect(p.x - 140, Screen.height - p.y, 280, 65), name + "\n" + StateName + " · 목숨 " + health.Lives.ToString("F1") + (Time.time < moanUntil && health.IsWounded ? "\n신음 (디버그 신호)" : ""));
        }
    }
}
