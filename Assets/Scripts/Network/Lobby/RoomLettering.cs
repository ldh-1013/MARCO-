using UnityEngine;

namespace Marco.Network.Lobby
{
    // Atlas cells have a common baseline. Widths are pixel advances before tracking.
    internal static class RoomLettering
    {
        private const string Glyphs = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#%.,:()·-대기방장명마이크보정완료전다시측건너뛰도역할무작위배술래각자확률나머지는망생성됨참가코드은목소리로말해세요커조금떨어져서주음거중에잘안들려더까본감진행합니없준비/를립입했습연결공호스트복사실패변잡을걸러낼맞춥![]열린밸브남간게림닫힘체력붕찰칵오통존관웅탈출메아찬초승효인북동노잠쿨운낚았외침홀당신격판부터숨모";
        private static readonly byte[] Widths = { 36, 27, 36, 36, 40, 36, 37, 38, 37, 37, 50, 42, 45, 42, 39, 36, 47, 44, 15, 37, 49, 37, 52, 44, 47, 39, 49, 46, 42, 43, 44, 50, 63, 50, 50, 43, 39, 37, 39, 37, 39, 27, 37, 36, 13, 21, 39, 13, 57, 36, 38, 37, 37, 27, 36, 25, 36, 39, 60, 42, 39, 33, 38, 58, 14, 14, 14, 19, 19, 16, 19, 52, 52, 57, 61, 51, 57, 50, 58, 58, 56, 61, 58, 54, 57, 53, 58, 54, 48, 54, 58, 52, 59, 58, 61, 54, 52, 58, 52, 59, 61, 60, 58, 56, 51, 52, 58, 57, 56, 56, 53, 60, 60, 58, 58, 58, 58, 58, 49, 58, 57, 55, 56, 58, 54, 58, 58, 52, 52, 55, 56, 58, 58, 54, 58, 54, 61, 59, 58, 51, 51, 60, 58, 59, 53, 55, 59, 47, 57, 58, 49, 18, 58, 49, 50, 58, 58, 53, 54, 58, 58, 58, 58, 58, 62, 54, 55, 52, 61, 58, 54, 51, 52, 57, 58, 12, 17, 17, 53, 50, 53, 58, 55, 60, 55, 48, 57, 52, 56, 51, 58, 61, 59, 58, 58, 58, 61, 58, 57, 58, 52, 58, 61, 58, 58, 58, 52, 58, 58, 58, 61, 58, 58, 55, 57, 56, 52, 58, 57, 54, 53, 60, 58, 51, 58, 58 };
        private static readonly Vector2 AtlasSize = new(1280, 1200);

        internal static void Draw(Rect rect, string text, int height, Color? tint = null, bool centered = false)
        {
            if (string.IsNullOrEmpty(text) || Event.current.type != EventType.Repaint) return;
            float width = 0;
            foreach (char c in text)
            {
                int index = Glyphs.IndexOf(c);
                if (index < 0 && c != ' ')
                {
                    var style = new GUIStyle(TestHudSkin.Text(height, FontStyle.Bold,
                        centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft));
                    style.normal.textColor = tint ?? new Color(.90f, .87f, .81f);
                    GUI.Label(rect, text, style);
                    return;
                }
                width += index < 0 ? 21 : Widths[index] + 4;
            }
            float scale = Mathf.Min(height / 64f, rect.width / width, rect.height / 64f);
            float x = centered ? rect.center.x - width * scale * .5f : rect.x;
            float y = rect.center.y - 32 * scale;
            foreach (char c in text)
            {
                int index = Glyphs.IndexOf(c);
                if (index < 0) { x += 21 * scale; continue; }
                float w = Widths[index];
                RuinedUi.Region(new Rect(x, y, w * scale, 64 * scale), "room_lettering",
                    new Rect(index % 16 * 80 + 2, index / 16 * 80 + 4, w, 64), AtlasSize, tint);
                x += (w + 4) * scale;
            }
        }
    }
}
