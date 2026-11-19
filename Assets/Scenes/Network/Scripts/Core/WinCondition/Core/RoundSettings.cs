namespace Marco.Core.WinCondition
{
    // 도망자 인원 수에 따라 정해지는 라운드 설정값 한 묶음.
    // v0.6: 활성 밸브/요구 개방 수는 인원과 무관하게 고정값이라 WinConditionConfig.ActiveValveCount/RequiredOpenValveCount로 옮김.
    public readonly struct RoundSettings
    {
        public readonly int EscapeRequiredCount;
        public readonly float TimeLimitSeconds;

        public RoundSettings(int escapeRequiredCount, float timeLimitSeconds)
        {
            EscapeRequiredCount = escapeRequiredCount;
            TimeLimitSeconds = timeLimitSeconds;
        }
    }
}
