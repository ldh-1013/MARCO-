namespace Marco.Core.WinCondition
{
    // 밸브 상태. Reflowing의 실제 동작(역류)은 3단계에서 구현함 — 지금은 이름만 있음.
    public enum ValveStatus
    {
        Closed,
        Rotating,
        Open,
        Reflowing,
    }
}
