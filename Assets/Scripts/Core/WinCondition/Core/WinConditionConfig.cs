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

        // 2인 동시 작업 배율 (약 5.0초에 완료)
        public float ValveTwoWorkerMultiplier = 1.6f;

        // 3인 이상 작업 배율, 상한값 (약 4.2초에 완료)
        public float ValveThreeOrMoreWorkerMultiplier = 1.9f;

        // 작업자가 0명이 된 뒤 버티는 유예 시간. 이 시간 동안은 진행도가 줄지 않음.
        public float ValveNoWorkerGraceSeconds = 3f;

        // 유예가 끝난 뒤 초당 줄어드는 진행도
        public float ValveNoWorkerDecayPerSecond = 0.10f;

        // 역류로 완전히 닫히면(진행도 0) 다시 Closed 상태로 돌아가 처음부터 작업해야 하는가.
        // TODO(기획 확인): 아직 공식 답변 없음. 우선 "돌아간다"로 가정.
        public bool ValveFullyReflowsBackToClosed = true;

        // ── 밸브 활성화 / 게이트 (v0.6: 인원과 무관하게 항상 고정값) ───────────────────────────────
        // 활성 밸브는 항상 3개: B·E는 항상 포함, 나머지 1개는 A·C·D 중 무작위(ValveActivation.SelectActiveValves 참고)
        public int ActiveValveCount = 3;

        // 게이트가 열리려면 활성 밸브 3개가 전부 동시에 열려야 함 (활성 밸브 수와 항상 같음)
        public int RequiredOpenValveCount = 3;

        // ── 역류 (Reflow) ───────────────────────────────
        // 밸브가 열린 뒤 역류가 시작되기까지의 유예 시간
        public float ReflowOpenGraceSeconds = 180f;

        // 유예가 끝난 뒤 완전히 닫힐 때까지 걸리는 시간
        public float ReflowCloseDurationSeconds = 30f;

        // 역류가 진행되는 동안 물소리 파문 알림을 몇 번 보내는지 (파문 자체는 남경 파트, 여기서는 이벤트 횟수만 관리)
        // TODO(기획 확인): 30초 동안 균등 간격으로 3번 알리는 것으로 임시 처리. 정확한 타이밍 미정.
        public int ReflowRippleEventCount = 3;

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
        private readonly Dictionary<int, RoundSettings> _roundSettingsByRunnerCount = new Dictionary<int, RoundSettings>
        {
            { 2, new RoundSettings(escapeRequiredCount: 1, timeLimitSeconds: 8f * 60f) },
            { 3, new RoundSettings(escapeRequiredCount: 2, timeLimitSeconds: 10f * 60f) },
            { 4, new RoundSettings(escapeRequiredCount: 2, timeLimitSeconds: 10f * 60f) },
            { 5, new RoundSettings(escapeRequiredCount: 3, timeLimitSeconds: 12f * 60f) },
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
