using UnityEngine;

namespace Marco.Prototype
{
    // Temporary local-only HUD; reflects existing lives instead of introducing hit points.
    [RequireComponent(typeof(PrototypeTarget))]
    public sealed class SurvivorHealthHud : MonoBehaviour
    {
        [SerializeField] private Vector2 panelSize = new Vector2(260, 84);
        [SerializeField] private Vector2 bottomRightMargin = new Vector2(28, 28);
        [Header("Injury screen edges")]
        [SerializeField, Range(0.05f, 0.4f)] private float injuryEdgeWidth = 0.22f;
        [SerializeField, Range(0, 1)] private float woundedOpacity = 0.55f;
        [SerializeField, Range(0, 1)] private float recoveringOpacity = 0.32f;
        [SerializeField, Min(0.01f)] private float hitFlashDuration = 0.45f;
        private Texture2D injuryVignette;
        private float drawnEdgeWidth = -1;
        private PrototypeTarget target;
        public float FillAmount => target == null ? 1 : Mathf.Clamp01(target.Health.Lives / 2f);
        public bool IsVisible => isActiveAndEnabled && target != null && !target.Health.IsEcho;
        public bool IsInjuryVisible => IsVisible && target.Health.Lives < 2f;
        public Color InjuryTint => target != null && target.Health.Lives <= 1f
            ? new Color(0.9f, 0.08f, 0.025f) : new Color(1f, 0.38f, 0.045f);
        public float InjuryOpacity
        {
            get
            {
                if (!IsInjuryVisible) return 0;
                bool wounded = target.Health.Lives <= 1f;
                float pulse = 1f + Mathf.Sin(Time.time * Mathf.PI * 2f * (wounded ? 1.1f : 0.8f)) * (wounded ? 0.08f : 0.04f);
                float sinceHit = Time.time - target.LastHitTime;
                float flash = target.LastHitTime >= 0 && sinceHit >= 0
                    ? 0.22f * (1f - Mathf.Clamp01(sinceHit / Mathf.Max(0.01f, hitFlashDuration))) : 0;
                return Mathf.Clamp01((wounded ? woundedOpacity : recoveringOpacity) * pulse + flash);
            }
        }
        private void Awake() { target = GetComponent<PrototypeTarget>(); }

        private void OnGUI()
        {
            if (!IsVisible) return;
            int oldDepth = GUI.depth;
            DrawInjuryOverlay();
            GUI.depth = -5;
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1000f, Screen.height / 700f), 0.5f, 2f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float x = Screen.width / scale - panelSize.x - bottomRightMargin.x;
            float y = Screen.height / scale - panelSize.y - bottomRightMargin.y;
            Draw(new Rect(x, y, panelSize.x, panelSize.y), new Color(0.03f, 0.04f, 0.05f, 0.88f));
            var title = new GUIStyle(GUI.skin.label) { fontSize = 16 };
            var value = new GUIStyle(title) { alignment = TextAnchor.UpperRight };
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 14, y + 10, 140, 24), "목숨 · " + StateName, title);
            GUI.Label(new Rect(x + 155, y + 10, 90, 24), target.Health.Lives.ToString("F1") + " / 2.0", value);
            var bar = new Rect(x + 14, y + 42, panelSize.x - 28, 18);
            Draw(bar, new Color(0.16f, 0.18f, 0.2f));
            Draw(new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * FillAmount, bar.height - 4),
                target.Health.IsWounded ? new Color(1f, 0.23f, 0.3f) : new Color(0.88f, 0.9f, 0.92f));
            for (int i = 1; i < 4; i++) Draw(new Rect(bar.x + bar.width * i / 4 - 1, bar.y, 2, bar.height), new Color(0.03f, 0.04f, 0.05f));
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            GUI.depth = oldDepth;
        }

        private void DrawInjuryOverlay()
        {
            if (!IsInjuryVisible || Event.current.type != EventType.Repaint) return;
            if (injuryVignette == null || !Mathf.Approximately(drawnEdgeWidth, injuryEdgeWidth)) CreateInjuryVignette();
            Color oldColor = GUI.color;
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.depth = -1; // Above the darkness mask, below health/text HUDs.
            GUI.matrix = Matrix4x4.identity;
            Color tint = InjuryTint;
            tint.a = InjuryOpacity;
            GUI.color = tint;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), injuryVignette);
            GUI.color = oldColor;
            GUI.matrix = oldMatrix;
        }

        private void CreateInjuryVignette()
        {
            const int size = 256;
            if (injuryVignette == null)
                injuryVignette = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "Survivor injury screen edges",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            var pixels = new Color32[size * size];
            float width = Mathf.Clamp(injuryEdgeWidth, 0.05f, 0.4f);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float edgeX = Mathf.Min(x, size - 1 - x) / (float)(size - 1);
                float edgeY = Mathf.Min(y, size - 1 - y) / (float)(size - 1);
                float horizontal = 1f - Mathf.SmoothStep(0, 1, edgeX / width);
                float vertical = 1f - Mathf.SmoothStep(0, 1, edgeY / width);
                // Soft edges and stronger corners; the centre stays fully transparent.
                float alpha = Mathf.Clamp01(Mathf.Max(horizontal, vertical) * 0.8f + Mathf.Min(horizontal, vertical) * 0.2f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
            injuryVignette.SetPixels32(pixels);
            injuryVignette.Apply(false, false);
            drawnEdgeWidth = injuryEdgeWidth;
        }

        private void OnDestroy()
        {
            if (injuryVignette == null) return;
            if (Application.isPlaying) Destroy(injuryVignette);
            else DestroyImmediate(injuryVignette);
        }

        private string StateName => target.Health.IsWounded ? "상처" : target.Health.Lives < 2 ? "회복 중" : "정상";
        private static void Draw(Rect rect, Color color) { GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); }
    }
}
