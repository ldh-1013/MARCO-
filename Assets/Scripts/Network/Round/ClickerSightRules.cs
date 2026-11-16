using Marco.Network.Player;

namespace Marco.Network.Round
{
    // 기획서 7절 · 부록 A [아이템] 섬광 대상 [11-16]: who receives a clicker flash. The user always does; everyone else must be
    // within ClickerSpec.Radius and not behind a structure (the same structures as sound occlusion — walls, storey slabs,
    // 차폐 파트, locker rows; furniture and props do not block). Echo bodies receive it; escapees and spectators do not.
    public static class ClickerSightRules
    {
        public static bool Receives(bool self, PlayerStatus status, bool withinRadius, bool clearSight)
        {
            if (self) return true;
            if (status == PlayerStatus.Escaped || status == PlayerStatus.Spectating) return false;
            return withinRadius && clearSight;
        }
    }
}
