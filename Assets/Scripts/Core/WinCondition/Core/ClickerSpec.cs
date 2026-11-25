namespace Marco.Core.WinCondition
{
    // 부록 A [아이템] 찰칵이 "반경 6m, 0.6초 섬광 · 6m 안 지형 전체를 밝힘 · 시야가 닿는 6m 안의 플레이어에게는 빛이 보인다".
    // The radius is read in two places that must agree: the server (who receives the flash) and the client (what it lights).
    public static class ClickerSpec
    {
        public const float Radius = 6f;
    }
}
