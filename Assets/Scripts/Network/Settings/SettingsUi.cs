using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Marco.Network.Settings
{
    // Code-built uGUI pieces for the settings window: flat rectangles and text only (no images, no new assets).
    // Colours are the 16.2 palette (black, environment #E8E8E8, amber #FFB84D for the active / key highlight).
    internal static class SettingsUi
    {
        public static readonly Color Black = new Color32(0x00, 0x00, 0x00, 0xFF);
        public static readonly Color Light = new Color32(0xE8, 0xE8, 0xE8, 0xFF);
        public static readonly Color Amber = new Color32(0xFF, 0xB8, 0x4D, 0xFF);
        public static Color LightA(float a) => new Color(Light.r, Light.g, Light.b, a);
        public static Color BlackA(float a) => new Color(0f, 0f, 0f, a);
        public static Color AmberA(float a) => new Color(Amber.r, Amber.g, Amber.b, a);

        private static Font font;
        // The same OS Korean font the result podium uses (ResultPodiumStage.StageFont); no font asset is added.
        public static Font Font => font != null ? font
            : font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 32);

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = position; rt.sizeDelta = size;
            return rt;
        }

        // Top-left based placement inside a parent (x right, y down), the way the panels are laid out.
        public static RectTransform At(Transform parent, string name, float x, float y, float w, float h) =>
            Rect(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));

        public static RectTransform Stretch(Transform parent, string name)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            return rt;
        }

        public static Image Fill(RectTransform rt, Color color, bool raycast = false)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Text Label(Transform parent, string name, float x, float y, float w, float h, string text, int size,
            Color color, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = At(parent, name, x, y, w, h);
            var label = rt.gameObject.AddComponent<Text>();
            label.font = Font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = anchor;
            label.fontStyle = style;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public static Button Button(Transform parent, string name, float x, float y, float w, float h, string text, int size, Action onClick)
        {
            var rt = At(parent, name, x, y, w, h);
            var image = Fill(rt, LightA(.24f), true);
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = LightA(.45f);
            outline.effectDistance = new Vector2(1, -1);
            var button = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, .5f); colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(1f, 1f, 1f, .8f); colors.selectedColor = new Color(1f, 1f, 1f, .5f);
            colors.disabledColor = new Color(1f, 1f, 1f, .15f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            Label(rt, "Text", 0, 0, w, h, text, size, Light, TextAnchor.MiddleCenter);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        public static Slider Slider(Transform parent, string name, float x, float y, float w, float h, float min, float max,
            bool whole, Action<float> changed)
        {
            var root = At(parent, name, x, y, w, h);
            var background = Stretch(root, "Background");
            background.offsetMin = new Vector2(0, h * .5f - 3); background.offsetMax = new Vector2(0, -(h * .5f - 3));
            Fill(background, LightA(.18f));
            var fillArea = Stretch(root, "Fill Area");
            fillArea.offsetMin = new Vector2(0, h * .5f - 3); fillArea.offsetMax = new Vector2(0, -(h * .5f - 3));
            var fill = Stretch(fillArea, "Fill");
            Fill(fill, AmberA(.85f));
            var handleArea = Stretch(root, "Handle Slide Area");
            handleArea.offsetMin = new Vector2(8, 0); handleArea.offsetMax = new Vector2(-8, 0);
            var handle = Rect(handleArea, "Handle", new Vector2(0, 0), new Vector2(0, 1), new Vector2(.5f, .5f), Vector2.zero, new Vector2(16, 0));
            var handleImage = Fill(handle, Light, true);
            var slider = root.gameObject.AddComponent<UnityEngine.UI.Slider>();
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handleImage;
            slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            slider.minValue = min; slider.maxValue = max; slider.wholeNumbers = whole;
            if (changed != null) slider.onValueChanged.AddListener(v => changed(v));
            return slider;
        }

        public static Toggle Toggle(Transform parent, string name, float x, float y, float size, Action<bool> changed)
        {
            var root = At(parent, name, x, y, size, size);
            var box = Fill(root, BlackA(.6f), true);
            var outline = root.gameObject.AddComponent<Outline>();
            outline.effectColor = LightA(.6f); outline.effectDistance = new Vector2(1, -1);
            var mark = Stretch(root, "Checkmark");
            mark.offsetMin = new Vector2(6, 6); mark.offsetMax = new Vector2(-6, -6);
            var markImage = Fill(mark, Amber);
            var toggle = root.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            toggle.targetGraphic = box; toggle.graphic = markImage;
            if (changed != null) toggle.onValueChanged.AddListener(v => changed(v));
            return toggle;
        }

        // "<  value  >": a code-built choice list (no dropdown template asset).
        public static Text Stepper(Transform parent, string name, float x, float y, float w, float h, Action<int> step)
        {
            var root = At(parent, name, x, y, w, h);
            Button(root, "Previous", 0, 0, h, h, "<", 24, () => step(-1));
            Button(root, "Next", w - h, 0, h, h, ">", 24, () => step(1));
            return Label(root, "Value", h + 8, 0, w - 2 * h - 16, h, "", 22, Light, TextAnchor.MiddleCenter);
        }
    }

    // Shows the shared tooltip while the pointer is over the ⓘ mark.
    internal sealed class SettingsTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public string Message;
        public Action<string, RectTransform> Show;
        public Action Hide;

        public void OnPointerEnter(PointerEventData eventData) => Show?.Invoke(Message, (RectTransform)transform);
        public void OnPointerExit(PointerEventData eventData) => Hide?.Invoke();
        private void OnDisable() => Hide?.Invoke();
    }
}
