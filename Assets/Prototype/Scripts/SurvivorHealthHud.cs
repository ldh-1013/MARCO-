using UnityEngine;

namespace Marco.Prototype
{
    // Temporary local-only HUD; reflects existing lives instead of introducing hit points.
    [RequireComponent(typeof(PrototypeTarget))]
    public sealed class SurvivorHealthHud : MonoBehaviour
    {
        [SerializeField] private Vector2 panelSize = new Vector2(260, 84);
        [SerializeField] private Vector2 bottomRightMargin = new Vector2(28, 28);
        private PrototypeTarget target;
        public float FillAmount => target == null ? 1 : Mathf.Clamp01(target.Health.Lives / 2f);
        public bool IsVisible => isActiveAndEnabled && target != null && !target.Health.IsEcho;
        private void Awake() { target = GetComponent<PrototypeTarget>(); }

        private void OnGUI()
        {
            if (!IsVisible) return;
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
        }

        private string StateName => target.Health.IsWounded ? "상처" : target.Health.Lives < 2 ? "회복 중" : "정상";
        private static void Draw(Rect rect, Color color) { GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); }
    }
}
