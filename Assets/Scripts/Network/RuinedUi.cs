using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network
{
    // Source PNGs remain intact. Pixel rectangles address the chosen artwork without resampling sheets.
    public static class RuinedUi
    {
        public const string Root = "UI/Ruined/";
        public const string PaperAsset = "room_browser_panel_ruined_v3";
        private static readonly Dictionary<string, Texture2D> textures = new();
        private static readonly Dictionary<string, Texture2D> lightTextures = new();
        private static Texture2D radial;
        private static Color32[] ringPixels, maskedPixels;
        private static float[] ringAngles;
        private static int lastProgress = -1;
        public static readonly Color Ink = new(.12f, .115f, .10f);

        private readonly struct LabelRegion
        {
            public readonly bool Support;
            public readonly Rect Rect;
            public LabelRegion(bool support, float x, float y, float w, float h)
            { Support = support; Rect = new Rect(x, y, w, h); }
        }

        private static readonly Dictionary<string, LabelRegion> labels = new()
        {
            ["방 만들기"] = new(false, 90, 72, 480, 150), ["방 찾기"] = new(false, 744, 70, 400, 155),
            ["참가"] = new(false, 200, 280, 255, 150), ["코드 참가"] = new(false, 690, 280, 510, 155),
            ["새로고침"] = new(false, 88, 480, 465, 160), ["취소"] = new(false, 800, 480, 280, 160),
            ["게임 시작"] = new(false, 65, 680, 510, 155), ["방 나가기"] = new(false, 695, 680, 500, 160),
            ["코드 복사"] = new(false, 75, 875, 495, 150), ["IP 복사"] = new(false, 735, 875, 425, 150),
            ["계속하기"] = new(false, 82, 1070, 475, 150), ["로비로"] = new(false, 755, 1070, 385, 150),
            ["닉네임"] = new(true, 330, 45, 380, 140), ["방 코드"] = new(true, 1425, 45, 430, 140),
            ["공개 방 목록"] = new(true, 220, 220, 650, 145), ["참가자"] = new(true, 1450, 220, 390, 145),
            ["초대 IP"] = new(true, 350, 395, 395, 135), ["불러오는 중…"] = new(true, 1290, 395, 740, 140),
            ["방장의 시작을 기다리는 중"] = new(true, 60, 560, 985, 125),
            ["게임 나가기 · 로비로"] = new(true, 1200, 560, 920, 125)
        };

        public static Texture2D Texture(string name)
        {
            if (!textures.TryGetValue(name, out var texture))
            {
                texture = Resources.Load<Texture2D>(Root + name);
                textures[name] = texture;
            }
            return texture;
        }

        // Source coordinates use a top-left origin and original dimensions, even for downsampled imports.
        public static void Region(Rect destination, string name, Rect source, Vector2 originalSize, Color? tint = null, bool light = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            var texture = light ? LightTexture(name) : Texture(name);
            if (texture == null) return;
            var old = GUI.color;
            GUI.color = old * (tint ?? Color.white);
            GUI.DrawTextureWithTexCoords(destination, texture, new Rect(source.x / originalSize.x,
                1 - source.yMax / originalSize.y, source.width / originalSize.x, source.height / originalSize.y));
            GUI.color = old;
        }

        // Recolor only the ink; retain every transparent and worn pixel of the original lettering.
        public static Texture2D LightTexture(string name)
        {
            if (lightTextures.TryGetValue(name, out var result)) return result;
            var source = Texture(name);
            if (source == null || !source.isReadable) return null;
            var pixels = source.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                pixels[i] = new Color32((byte)(255 - p.r), (byte)(255 - p.g), (byte)(255 - p.b), p.a);
            }
            result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
            { name = name + " light ink", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            result.SetPixels32(pixels);
            result.Apply(false, false);
            lightTextures[name] = result;
            return result;
        }

        public static void Paper(Rect rect) => Region(rect, PaperAsset, new Rect(350, 705, 430, 48), new Vector2(1315, 1197));

        public static void Label(Rect rect, string text, int size = 24, bool light = false)
        {
            if (!labels.TryGetValue(text, out var label) ||
                (light && LightTexture(label.Support ? "lobby_support_labels_ruined_v3" : "lobby_labels_ruined_v3") == null))
            {
                var style = InkText(size);
                if (light) style.normal.textColor = new Color(.9f, .9f, .88f);
                GUI.Label(rect, text, style);
                return;
            }
            float height = Mathf.Min(rect.height, size * 1.15f);
            float width = height * label.Rect.width / label.Rect.height;
            if (width > rect.width) { height *= rect.width / width; width = rect.width; }
            var target = new Rect(rect.center.x - width / 2, rect.center.y - height / 2, width, height);
            Region(target, label.Support ? "lobby_support_labels_ruined_v3" : "lobby_labels_ruined_v3",
                label.Rect, label.Support ? new Vector2(2172, 724) : new Vector2(1254, 1254),
                new Color(1, 1, 1, GUI.enabled ? 1 : .4f), light);
        }

        public static GUIStyle InkText(int size, bool wrap = false)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = wrap, alignment = TextAnchor.MiddleLeft };
            style.normal.textColor = Ink;
            return style;
        }

        public static bool Button(Rect rect, string text, int size = 24, bool paper = false, bool light = false)
        {
            if (paper) Paper(rect);
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            bool hover = GUI.enabled && rect.Contains(Event.current.mousePosition);
            if (hover && !light) TestHudSkin.Fill(rect, new Color(.15f, .12f, .04f, .12f));
            var old = GUI.color;
            if (light && hover) GUI.color *= new Color(1, .86f, .56f);
            Label(rect, text, size, light);
            GUI.color = old;
            return clicked;
        }

        public static bool LayoutButton(string text, float height, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), GUI.skin.button, options);
            rect.height = height;
            return Button(rect, text, Mathf.Min(24, Mathf.RoundToInt(height - 8)), light: true);
        }

        public static void LayoutLabel(string text, float width)
        {
            var rect = GUILayoutUtility.GetRect(width, 28, GUILayout.Width(width), GUILayout.Height(28));
            Label(rect, text, 18, light: true);
        }

        public static void Health(Rect rect, int halves)
        {
            int y = halves >= 4 ? 170 : halves == 3 ? 336 : 502;
            Region(rect, "health_stamina_worn", new Rect(56, y, 1004, 64), new Vector2(2172, 724));
        }

        public static void Stamina(Rect rect, float fraction)
        {
            Region(rect, "stamina_yellow_ruined", new Rect(56, 175, 2060, 54), new Vector2(2172, 724));
            float fill = Mathf.Clamp01(fraction);
            if (fill <= 0) return;
            var inner = new Rect(rect.x + 2, rect.y + 2, (rect.width - 4) * fill, rect.height - 4);
            Region(inner, "stamina_yellow_ruined", new Rect(58, 342, 2056 * fill, 48), new Vector2(2172, 724));
        }

        public static void Reticle(Vector2 center, Color color, float size = 28)
            => Region(new Rect(center.x - size / 2, center.y - size / 2, size, size), "aim_reticle_ruined_v3",
                new Rect(386, 384, 482, 485), new Vector2(1254, 1254), color);

        public static void Item(Rect rect, int slot, bool owned, bool selected)
        {
            Region(rect, "slot_boxes_worn", selected ? new Rect(932, 27, 328, 303) : new Rect(446, 27, 328, 303),
                new Vector2(2172, 724));
            if (!owned) return;
            Rect source = slot switch
            {
                1 => new Rect(745, 120, 440, 505), 2 => new Rect(110, 95, 490, 415),
                3 => new Rect(118, 641, 490, 490), _ => new Rect(760, 605, 335, 550)
            };
            float scale = Mathf.Min((rect.width - 12) / source.width, (rect.height - 12) / source.height);
            var icon = new Rect(rect.center.x - source.width * scale / 2, rect.center.y - source.height * scale / 2,
                source.width * scale, source.height * scale);
            Region(icon, "item_icons_worn", source, new Vector2(1293, 1217));
        }

        public static void HealingRing(Rect rect, float fraction)
        {
            Region(rect, "healing_ring_track_ruined_v3", new Rect(155, 166, 935, 945), new Vector2(1254, 1254));
            if (Event.current.type != EventType.Repaint) return;
            var texture = ProgressTexture(fraction);
            if (texture != null) GUI.DrawTexture(rect, texture);
        }

        // Only one healing action is displayed locally. Reuse one small texture, updating at 1% steps.
        public static Texture2D ProgressTexture(float fraction)
        {
            const int size = 256;
            if (radial == null)
            {
                var source = Texture("healing_ring_fill_ruined_v3");
                if (source == null) return null;
                radial = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "Healing radial mask", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                ringPixels = new Color32[size * size]; maskedPixels = new Color32[size * size]; ringAngles = new float[size * size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float u = (x + .5f) / size, v = (y + .5f) / size;
                    ringPixels[i] = source.GetPixelBilinear((171 + u * 905) / 1254f, 1 - (185 + (1 - v) * 915) / 1254f);
                    ringAngles[i] = Mathf.Repeat(Mathf.Atan2(u - .5f, v - .5f) / (2 * Mathf.PI), 1);
                }
            }
            int progress = Mathf.RoundToInt(Mathf.Clamp01(fraction) * 100);
            if (progress != lastProgress)
            {
                for (int i = 0; i < ringPixels.Length; i++)
                    maskedPixels[i] = progress == 100 || (progress > 0 && ringAngles[i] < progress / 100f) ? ringPixels[i] : default;
                radial.SetPixels32(maskedPixels); radial.Apply(false, false); lastProgress = progress;
            }
            return radial;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            if (radial != null) Object.DestroyImmediate(radial);
            foreach (var texture in lightTextures.Values) if (texture != null) Object.DestroyImmediate(texture);
            lightTextures.Clear();
            radial = null; ringPixels = null; maskedPixels = null; ringAngles = null;
            textures.Clear(); lastProgress = -1;
        }
    }
}
