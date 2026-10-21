using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network
{
    // Shared IMGUI helpers for the test HUD and cheat panel. Everything is laid out on a 1920x1080
    // canvas and scaled to the screen through GUI.matrix, so sizes stay proportional at any resolution.
    public static class TestHudSkin
    {
        private const float ReferenceWidth = 1920, ReferenceHeight = 1080;
        public const float Margin = 24, Padding = 16;

        public static readonly Color PanelColor = new(0f, 0f, 0f, 0.6f);
        public static readonly Color BarBackColor = new(1f, 1f, 1f, 0.15f);

        // Screen-space (top-left origin) rects drawn this frame or the last one; PlayerMovement runs
        // before OnGUI, so it checks against the previous frame's layout.
        private static readonly List<Rect> blockers = new();
        private static int blockerFrame = -1;
        private static readonly Dictionary<(int, FontStyle, TextAnchor), GUIStyle> styles = new();
        private static readonly Dictionary<int, GUIStyle> buttonStyles = new();

        public static float Scale { get; private set; } = 1f;
        public static float Width => Screen.width / Scale;
        public static float Height => Screen.height / Scale;

        // Set while a panel needs a free cursor (cheat panel open), so clicks never relock it.
        public static bool HoldsCursor { get; set; }

        public static bool IsPointerOver
        {
            get
            {
                if (blockerFrame < Time.frameCount - 1) return false;
                var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                foreach (var rect in blockers)
                    if (rect.Contains(mouse)) return true;
                return false;
            }
        }

        // Call at the top of every OnGUI that draws on the reference canvas.
        public static void Begin()
        {
            Scale = Mathf.Min(Screen.width / ReferenceWidth, Screen.height / ReferenceHeight);
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
        }

        public static GUIStyle Text(int size, FontStyle fontStyle = FontStyle.Normal, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var key = (size, fontStyle, anchor);
            if (!styles.TryGetValue(key, out var style))
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fontStyle, alignment = anchor, wordWrap = false };
                style.normal.textColor = Color.white;
                styles[key] = style;
            }
            return style;
        }

        public static GUIStyle Button(int size)
        {
            if (!buttonStyles.TryGetValue(size, out var style))
            {
                style = new GUIStyle(GUI.skin.button) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
                style.padding.left = 16;
                buttonStyles[size] = style;
            }
            return style;
        }

        // Semi-transparent box; also marks the area as HUD so clicking it does not relock the cursor.
        public static void Panel(Rect rect)
        {
            Fill(rect, PanelColor);
            if (Event.current.type != EventType.Repaint) return;
            if (blockerFrame != Time.frameCount)
            {
                blockers.Clear();
                blockerFrame = Time.frameCount;
            }
            blockers.Add(new Rect(rect.x * Scale, rect.y * Scale, rect.width * Scale, rect.height * Scale));
        }

        public static void Bar(Rect rect, float progress, Color color)
        {
            Fill(rect, BarBackColor);
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(progress), rect.height), color);
        }

        public static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
