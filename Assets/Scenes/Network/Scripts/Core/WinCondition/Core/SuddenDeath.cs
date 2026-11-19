using System;

namespace Marco.Core.WinCondition
{
    // 종반 압박(기획서 6.2-1). 라운드 잔여 시간만 보고 단계를 판정한다 — 시간은 RoundState가 넘겨준다.
    // 한 번 지난 단계는 되돌리지 않는다. 큰 dt로 여러 경계를 한 번에 넘어도 지난 것은 전부 발동한다.
    // 최후 생존자 페이즈와도 무관하게 라운드 잔여 시간만 따른다(페이즈가 먼저 끝나 라운드가 끝나면 남은 단계는 발동하지 않는다).
    public sealed class SuddenDeath
    {
        // 경계값 비교에서 부동소수 오차로 한 틱 늦게 발동하는 것을 막는 여유
        private const float RemainingEpsilon = 0.0001f;

        private readonly WinConditionConfig _config;

        // 잔여 2분 저역 드론이 시작됐는가 (연출만, 클라이언트가 동기화된 잔여 시간으로 직접 재생한다)
        public bool IsDroneStarted { get; private set; }

        // 잔여 1분에 1회 켜지고 라운드 끝까지 유지된다
        public bool IsSeekerSpeedBoosted { get; private set; }

        // 술래 이동속도에 곱하는 값. 부스트 전 1.0, 후 1.05
        public float SeekerSpeedMultiplier => IsSeekerSpeedBoosted ? _config.SuddenDeathSeekerSpeedMultiplier : 1f;

        // 지금까지 보낸 출구 파문 횟수 (최대 SuddenDeathExitPulseCount)
        public int ExitPulsesFired { get; private set; }

        public event Action DroneStarted;
        public event Action SeekerSpeedBoosted;

        // 양쪽 출구에서 고함급 파문을 내야 하는 순간 (위치 · 반경은 Network/파문 쪽 책임, 여기서는 신호만)
        public event Action ExitPulseTriggered;

        public SuddenDeath(WinConditionConfig config)
        {
            _config = config;
        }

        public void Update(float remainingSeconds)
        {
            if (!IsDroneStarted && remainingSeconds <= _config.SuddenDeathDroneRemainingSeconds + RemainingEpsilon)
            {
                IsDroneStarted = true;
                DroneStarted?.Invoke();
            }

            if (!IsSeekerSpeedBoosted && remainingSeconds <= _config.SuddenDeathSeekerBoostRemainingSeconds + RemainingEpsilon)
            {
                IsSeekerSpeedBoosted = true;
                SeekerSpeedBoosted?.Invoke();
            }

            // n번째 파문(0부터)은 잔여 30 − 5n초: 30 · 25 · 20 · 15 · 10 · 5
            while (ExitPulsesFired < _config.SuddenDeathExitPulseCount &&
                   remainingSeconds <= NextExitPulseRemainingSeconds() + RemainingEpsilon)
            {
                ExitPulsesFired++;
                ExitPulseTriggered?.Invoke();
            }
        }

        private float NextExitPulseRemainingSeconds() =>
            _config.SuddenDeathExitPulseRemainingSeconds - _config.SuddenDeathExitPulseIntervalSeconds * ExitPulsesFired;
    }
}
