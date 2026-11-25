using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network
{
    // Top-left pixel bounds in interface_art. Padding between regions prevents bilinear bleed.
    internal static class InterfaceArt
    {
        public const string Atlas = "interface_art";
        internal static readonly Dictionary<string, Rect> Regions = new()
        {
            ["paperTall"] = new(4, 4, 347, 550),
            ["paper"] = new(359, 4, 800, 538),
            ["술래"] = new(1167, 4, 188, 96),
            ["닫기"] = new(1363, 4, 190, 96),
            ["설정"] = new(1561, 4, 178, 96),
            ["off"] = new(1747, 4, 91, 96),
            ["on"] = new(1846, 4, 92, 96),
            ["<"] = new(1946, 4, 78, 96),
            [">"] = new(4, 562, 78, 96),
            ["ready"] = new(90, 562, 93, 96),
            ["waiting"] = new(191, 562, 94, 96),
            ["count3"] = new(293, 562, 73, 96),
            ["count2"] = new(374, 562, 72, 96),
            ["count1"] = new(454, 562, 59, 96),
            ["valve"] = new(521, 562, 94, 96),
            ["valveOpen"] = new(623, 562, 95, 96),
            ["gateOpen"] = new(726, 562, 96, 96),
            ["gateClosed"] = new(830, 562, 96, 96),
            ["drainArrow"] = new(934, 562, 102, 96),
            ["arrow"] = new(1044, 562, 101, 96),
            ["drain"] = new(1153, 562, 96, 96),
            ["0"] = new(1257, 562, 59, 96),
            ["1"] = new(1324, 562, 45, 96),
            ["2"] = new(1377, 562, 60, 96),
            ["3"] = new(1445, 562, 60, 96),
            ["4"] = new(1513, 562, 67, 96),
            ["5"] = new(1588, 562, 59, 96),
            ["6"] = new(1655, 562, 61, 96),
            ["7"] = new(1724, 562, 63, 96),
            ["8"] = new(1795, 562, 62, 96),
            ["9"] = new(1865, 562, 61, 96),
            [":"] = new(1934, 562, 38, 96),
            ["/"] = new(1980, 562, 58, 96),
            ["north"] = new(4, 666, 89, 96),
            ["east"] = new(101, 666, 128, 96),
            ["south"] = new(237, 666, 93, 96),
            ["west"] = new(338, 666, 129, 96),
            ["rotating"] = new(475, 666, 94, 96),
            ["reflow"] = new(577, 666, 94, 96),
            ["progress4"] = new(679, 666, 92, 96),
            ["progress3"] = new(779, 666, 92, 96),
            ["progress2"] = new(879, 666, 92, 96),
            ["progress1"] = new(979, 666, 92, 96),
            ["progress0"] = new(1079, 666, 92, 96),
            ["reflowFrame"] = new(1179, 666, 97, 96),
            ["closedFrame"] = new(1284, 666, 100, 96),
            ["그래픽"] = new(1392, 666, 220, 80),
            ["오디오"] = new(1620, 666, 220, 78),
            ["해상도"] = new(4, 770, 220, 76),
            ["메아리"] = new(232, 770, 220, 75),
            ["컨트롤"] = new(460, 770, 220, 74),
            ["도망자"] = new(688, 770, 220, 74),
            ["activeDrain"] = new(916, 770, 360, 71),
            ["frame"] = new(1284, 770, 220, 70),
            ["last"] = new(1512, 770, 360, 70),
            ["handle"] = new(1880, 770, 137, 68),
            ["계속하기"] = new(4, 854, 220, 57),
            ["키 가이드"] = new(232, 854, 220, 56),
            ["방 나가기"] = new(460, 854, 220, 56),
            ["출력 장치"] = new(688, 854, 220, 56),
            ["Y축 반전"] = new(916, 854, 220, 56),
            ["색맹 모드"] = new(1144, 854, 220, 55),
            ["게임 종료"] = new(1372, 854, 220, 54),
            ["FOV (가로)"] = new(1600, 854, 220, 48),
            ["기본값 복원"] = new(4, 919, 220, 47),
            ["마이크 장치"] = new(232, 919, 220, 47),
            ["마스터 볼륨"] = new(460, 919, 220, 44),
            ["마이크 레벨"] = new(688, 919, 220, 44),
            ["프레임 제한"] = new(916, 919, 220, 44),
            ["마우스 감도"] = new(1144, 919, 220, 44),
            ["밝기(파문 대비)"] = new(1372, 919, 220, 37),
            ["디스플레이 모드"] = new(1600, 919, 220, 34),
            ["효과음/UI 볼륨"] = new(4, 974, 220, 33),
            ["프레임 표시(FPS)"] = new(232, 974, 220, 32),
            ["track"] = new(460, 974, 220, 25),
            ["fill"] = new(688, 974, 220, 25),
        };

        private static readonly Dictionary<string, Sprite> sprites = new();
        public static Sprite Get(string key)
        {
            if (!Regions.TryGetValue(key, out var rect)) return null;
            if (sprites.TryGetValue(key, out var sprite) && sprite != null) return sprite;
            var texture = RuinedUi.Texture(Atlas);
            if (texture == null) return null;
            rect.y = texture.height - rect.yMax;
            sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            sprites[key] = sprite;
            return sprite;
        }

        public static void Draw(Rect bounds, string key)
        {
            if (Event.current.type != EventType.Repaint) return;
            var region = Regions[key];
            var texture = RuinedUi.Texture(Atlas);
            if (texture == null) return;
            float scale = Mathf.Min(bounds.width / region.width, bounds.height / region.height);
            var size = region.size * scale;
            RuinedUi.Region(new Rect(bounds.center - size * .5f, size), Atlas, region,
                new Vector2(texture.width, texture.height));
        }

        public static void Number(Rect bounds, string value)
        {
            float cell = Mathf.Min(bounds.height * .7f, bounds.width / value.Length);
            float x = bounds.center.x - cell * value.Length * .5f;
            foreach (char glyph in value)
            {
                float height = glyph == ':' ? bounds.height * .66f : bounds.height;
                Draw(new Rect(x, bounds.center.y - height * .5f, cell - 2, height), glyph.ToString());
                x += cell;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            foreach (var sprite in sprites.Values)
                if (sprite != null) Object.DestroyImmediate(sprite);
            sprites.Clear();
        }
    }
}
