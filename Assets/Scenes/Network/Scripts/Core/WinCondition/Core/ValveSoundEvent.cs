namespace Marco.Core.WinCondition
{
    // IValveSoundSink로 넘어가는 "소리 종류". 반경 등 숫자는 여기 포함하지 않음(파문 쪽 책임).
    public enum ValveSoundEvent
    {
        RotatingStarted,
        RotatingStopped,
        ReflowRipple,
    }
}
