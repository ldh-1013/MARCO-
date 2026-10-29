using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // 밸런스 숫자를 전부 모아두는 곳 (규칙 2: 다른 클래스에는 숫자 상수를 두지 않는다)
    // 모든 값은 기획서 패치 v0.6 기준 "플레이테스트 시작값"이고, 확정값이 아니다.
    public sealed class WinConditionConfig
    {
        // ── 밸브 진행도 ───────────────────────────────
        // 1인 작업 기준 초당 진행도. 1/8이면 8.0초에 완료.
        public float ValveBaseProgressPerSecond = 1f / 8f;

        // 재개방 1인 초당 진행도. 1/4이면 4.0초에 완료 (기획서 6.1 · 부록 A [밸브], 10-26).
        // 한 번이라도 Open에 도달한 밸브에 적용되고 라운드 내 유지된다(역류로 닫혀도 유지). 다인 배율은 그대로 곱한다.
        public float ValveReopenProgressPerSecond = 1f / 4f;

        // 2인 동시 작업 배율 (약 5.0초에 완료, 재개방 2.5초)
        public float ValveTwoWorkerMultiplier = 1.6f;

        // 3인 이상 작업 배율, 상한값 (약 4.2초에 완료, 재개방 2.1초)
        public float ValveThreeOrMoreWorkerMultiplier = 1.9f;

        // 작업자가 0명이 된 뒤 버티는 유예 시간. 이 시간 동안은 진행도가 줄지 않음.
        public float ValveNoWorkerGraceSeconds = 3f;

        // 유예가 끝난 뒤 초당 줄어드는 진행도
        public float ValveNoWorkerDecayPerSecond = 0.10f;

        // 역류로 완전히 닫히면(진행도 0) 다시 Closed 상태로 돌아가 처음부터 작업해야 하는가.
        // TODO(기획 확인): 아직 공식 답변 없음. 우선 "돌아간다"로 가정.
        public bool ValveFullyReflowsBackToClosed = true;

        // ── 밸브 활성화 / 게이트 (v0.6: 인원과 무관하게 항상 고정값) ───────────────────────────────
        // 활성 밸브는 항상 3개: A~E 5개 중 무작위 3개, 위치는 서버만 안다(10-26, ValveActivation.SelectActiveValves 참고)
        public int ActiveValveCount = 3;

        // 게이트가 열리려면 활성 밸브 3개가 전부 동시에 열려야 함 (활성 밸브 수와 항상 같음)
        public int RequiredOpenValveCount = 3;

        // ── 역류 (Reflow) ───────────────────────────────
        // 밸브가 열린 뒤 역류가 시작되기까지의 유예 시간 (부록 A [밸브 역류] 240초, 10-26 — 예전 180초)
        public float ReflowOpenGraceSeconds = 240f;

        // 유예가 끝난 뒤 완전히 닫힐 때까지 걸리는 시간
        public float ReflowCloseDurationSeconds = 30f;

        // 역류가 진행되는 동안 물소리 파문 알림을 몇 번 보내는지 (파문 자체는 남경 파트, 여기서는 이벤트 횟수만 관리)
        // TODO(기획 확인): 30초 동안 균등 간격으로 3번 알리는 것으로 임시 처리. 정확한 타이밍 미정.
        public int ReflowRippleEventCount = 3;

        // ── 종반 압박 (SuddenDeath, 기획서 6.2-1 · 부록 A [종반 압박]) ───────────────────────────────
        // 모두 "라운드 잔여 시간" 기준이다. 제한시간이 바뀌어도 그대로 동작한다.

        // 잔여 2분: 맵 전체 저역 드론 (연출만, 규칙 변화 없음)
        public float SuddenDeathDroneRemainingSeconds = 120f;

        // 잔여 1분: 술래 이동속도 +5% 추가. 1회 적용 후 라운드 끝까지 유지, 기존 속도에 곱한다(5.30 → 5.57 m/s)
        public float SuddenDeathSeekerBoostRemainingSeconds = 60f;
        public float SuddenDeathSeekerSpeedMultiplier = 1.05f;

        // 잔여 30초: 양쪽 출구에서 고함급(22m) 파문, 5초 주기 6회 — 잔여 30 · 25 · 20 · 15 · 10 · 5초
        public float SuddenDeathExitPulseRemainingSeconds = 30f;
        public float SuddenDeathExitPulseIntervalSeconds = 5f;
        public int SuddenDeathExitPulseCount = 6;

        // ── 라운드 시작 (RoundStartSchedule, 부록 A [라운드 시작] · 15.4, 10-26) ───────────────────────────────
        // 0초 = 역할 확인 시작. 역할 확인 동안은 전원 이동 잠금이고, 끝나는 순간(5초)이 InGame 시작이다.
        public float RoleRevealSeconds = 5f;

        // InGame 시작 뒤 술래만 더 묶어두는 격리 시간. 술래는 역할 확인 시작 후 10초 시점에 풀린다.
        public float SeekerHoldSeconds = 5f;

        // ── 최후 생존자 페이즈 (LastSurvivorPhase) ───────────────────────────────
        public float LastSurvivorPhaseDurationSeconds = 90f;

        // 배수구(DrainHatch) 후보 개수. 이 중 무작위로 1개만 활성화됨.
        public int DrainHatchCandidateCount = 2;

        // 배수구 작업 시간 T = 11 − (동시 개방 밸브 수 × 3)초, 최소 1초 (v0.6)
        public float GetDrainHatchWorkSeconds(int openValveCount)
        {
            float seconds = 11f - (openValveCount * 3f);
            return seconds < 1f ? 1f : seconds;
        }

        // ── 인원 표 (도망자 수 → 탈출 요구 / 제한시간) ───────────────────────────────
        // v0.6: 활성 밸브/요구 개방은 인원과 무관하게 항상 고정값이라 표에서 빠짐 (위 ActiveValveCount/RequiredOpenValveCount 참고)
        // 탈출 요구 인원 = ⌈도망자 수 ÷ 2⌉ (아래 값들과 일치함)
        // 제한시간은 부록 A [밸브/시간 — 인원별] (10-26 전 구간 +3분). 도망자 1명(총원 2, 9분)은 v1.x라 넣지 않는다.
        private readonly Dictionary<int, RoundSettings> _roundSettingsByRunnerCount = new Dictionary<int, RoundSettings>
        {
            { 2, new RoundSettings(escapeRequiredCount: 1, timeLimitSeconds: 11f * 60f) },
            { 3, new RoundSettings(escapeRequiredCount: 2, timeLimitSeconds: 13f * 60f) },
            { 4, new RoundSettings(escapeRequiredCount: 2, timeLimitSeconds: 13f * 60f) },
            { 5, new RoundSettings(escapeRequiredCount: 3, timeLimitSeconds: 15f * 60f) },
        };

        // 도망자 수에 맞는 라운드 설정을 돌려준다. 기획서에 정의된 2~5명만 지원.
        public RoundSettings GetRoundSettings(int runnerCount)
        {
            if (_roundSettingsByRunnerCount.TryGetValue(runnerCount, out var settings))
            {
                return settings;
            }

            throw new ArgumentOutOfRangeException(nameof(runnerCount), runnerCount, "도망자 수는 2~5명만 기획서에 정의되어 있음");
        }
    }
}
