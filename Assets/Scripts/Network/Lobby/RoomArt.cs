using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network.Lobby
{
    internal static class RoomArt
    {
        private static readonly Dictionary<string, Rect> Regions = new()
        {
            ["panel/대기방"] = new(2, 2, 1440, 724),
            ["panel/마이크_보정"] = new(2, 730, 1440, 304),
            ["label/참가자"] = new(1446, 730, 390, 145),
            ["label/방 코드"] = new(2, 1038, 430, 140),
            ["label/초대 IP"] = new(436, 1038, 395, 135),
            ["list/명단 배경"] = new(2, 1182, 1352, 128),
            ["label/대기방"] = new(1358, 1182, 169, 60),
            ["button/게임 시작"] = new(2, 1314, 828, 84),
            ["button/방 나가기"] = new(834, 1314, 504, 84),
            ["button/방장의 시작을 기다리는 중"] = new(2, 1402, 828, 84),
            ["button/건너뛰기"] = new(834, 1402, 300, 80),
            ["button/다시 시도"] = new(1138, 1402, 380, 80),
            ["button/마이크 다시 측정"] = new(1522, 1402, 380, 80),
            ["label/마이크 보정 전"] = new(2, 1490, 403, 60),
            ["label/마이크 보정"] = new(456, 1490, 320, 60),
            ["button/IP 복사"] = new(821, 1490, 240, 68),
            ["button/코드 복사"] = new(1065, 1490, 240, 68),
            ["label/보정 완료"] = new(1309, 1490, 270, 60),
            ["list/방장 행"] = new(2, 1564, 1352, 56),
            ["list/참가자 행"] = new(2, 1624, 1352, 56),
            ["label/방장"] = new(1358, 1624, 110, 55),
            ["list/측정 게이지 바탕"] = new(2, 1684, 1048, 20),
            ["list/측정 게이지 채움"] = new(2, 1708, 1048, 20),
            ["list/구분선"] = new(2, 1732, 1352, 2),
        };

        internal static void Draw(Rect bounds, string key, float labelHeight = 0)
        {
            var src = Regions[key];
            if (labelHeight > 0)
            {
                float scale = Mathf.Min(labelHeight / src.height, bounds.width / src.width, bounds.height / src.height);
                bounds = new Rect(bounds.x, bounds.center.y - src.height * scale / 2, src.width * scale, src.height * scale);
            }
            RuinedUi.Region(bounds, "room_art", src, new Vector2(2048, 2048));
        }

        internal static bool Has(string key) => Regions.ContainsKey(key);
    }
}
