using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // 밸브 하나의 상태기계. 시간은 Tick(dt)로만 흐른다 (Unity 프레임에 묶이지 않음).
    public sealed class Valve
    {
        private const float RippleEpsilon = 0.0001f;

        private readonly WinConditionConfig _config;
        private readonly HashSet<int> _activeWorkerIds = new HashSet<int>();

        // 작업자가 0명일 때 유예 후 감쇠 처리 (DrainHatch와 공유하는 도우미)
        private readonly GraceDecayTracker _noWorkerTracker;

        // Open 상태에서 흐른 시간. 역류 유예(ReflowOpenGraceSeconds)를 넘기면 역류가 시작됨.
        private float _openElapsedSeconds;

        // Reflowing 상태에서 흐른 시간. 닫힘 시간(ReflowCloseDurationSeconds)에 도달하면 Closed.
        private float _reflowElapsedSeconds;

        // 이번 역류에서 물소리 알림을 몇 번 보냈는지 (최대 ReflowRippleEventCount)
        private int _reflowRipplesFired;

        public ValveStatus Status { get; private set; } = ValveStatus.Closed;
        public float Progress { get; private set; } = 0f;

        // 역류 중 재작업 진행도(0.0 ~ 1.0). 역류로 줄어드는 Progress와 따로 0부터 채운다. 역류 중이 아니면 항상 0.
        public float ReworkProgress { get; private set; } = 0f;

        public int WorkerCount => _activeWorkerIds.Count;

        // 상태가 바뀔 때마다 새 상태를 알려줌 (UI/네트워크 쪽에서 구독)
        public event Action<ValveStatus> StatusChanged;

        // 역류 중 물소리 파문 알림 (파문 자체는 남경 파트, 여기서는 "지금 알려야 한다"는 신호만 보냄)
        public event Action ReflowRippleTriggered;

        public Valve(WinConditionConfig config)
        {
            _config = config;
            _noWorkerTracker = new GraceDecayTracker(config.ValveNoWorkerGraceSeconds, config.ValveNoWorkerDecayPerSecond);
        }

        // 같은 playerId로 여러 번 불러도 HashSet이라 1명으로만 센다.
        public void BeginWork(int playerId)
        {
            _activeWorkerIds.Add(playerId);
        }

        public void EndWork(int playerId)
        {
            _activeWorkerIds.Remove(playerId);
        }

        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            switch (Status)
            {
                case ValveStatus.Open:
                    TickOpen(dt);
                    break;
                case ValveStatus.Reflowing:
                    TickReflowing(dt);
                    break;
                default:
                    TickWorking(dt);
                    break;
            }
        }

        // Closed/Rotating 상태: 작업 진행도 처리 (2단계와 동일한 로직)
        private void TickWorking(float dt)
        {
            if (WorkerCount > 0)
            {
                _noWorkerTracker.ResetGrace();
                Progress += GetWorkRatePerSecond(WorkerCount) * dt;
            }
            else
            {
                Progress -= _noWorkerTracker.ConsumeDecay(dt);
            }

            Progress = Clamp01(Progress);
            UpdateStatusFromProgress();
        }

        // Open 상태: 역류 유예를 센다. 유예가 dt 중간에 끝나면 남은 시간을 그대로 역류 처리로 넘긴다.
        private void TickOpen(float dt)
        {
            float remaining = dt;
            float graceLeft = _config.ReflowOpenGraceSeconds - _openElapsedSeconds;

            if (graceLeft > 0f)
            {
                float consumed = remaining < graceLeft ? remaining : graceLeft;
                _openElapsedSeconds += consumed;
                remaining -= consumed;
            }

            if (remaining > 0f)
            {
                SetStatus(ValveStatus.Reflowing);
                _reflowElapsedSeconds = 0f;
                _reflowRipplesFired = 0;
                TickReflowing(remaining);
            }
        }

        // Reflowing 상태: 30초에 걸쳐 진행도를 1 -> 0으로 줄이고, 균등 간격으로 물소리 알림을 보낸다.
        // 역류 중 재작업(기획서 6.1-2): ReworkProgress를 0부터 따로 채운다(회전 시간은 처음과 동일).
        // 재작업이 먼저 끝나면 Open 복귀 + 180초 유예 리셋, 역류가 먼저 끝나면 Closed.
        // TODO(기획 확인): "재작업 중에도 역류는 멈추지 않고 계속 닫힌다"와
        // "역류가 먼저 끝나면 재작업 진행도를 버린다"는 임시 해석.
        // 완료 전까지는 Status를 Reflowing으로 유지한다(Rotating으로 바꾸면 다음 Tick이 TickWorking 경로로 새서 역류 타이머가 깨짐).
        private void TickReflowing(float dt)
        {
            float closeLeft = _config.ReflowCloseDurationSeconds - _reflowElapsedSeconds;
            float step = dt < closeLeft ? dt : closeLeft; // 이번 dt 중 역류가 실제로 진행되는 시간

            // 이번 step 안에 재작업이 끝나면 그 순간까지만 역류를 진행시키고 Open으로 돌린다.
            // 역류 종료와 정확히 같은 순간이면 재작업을 먼저 인정한다(플레이어 행동이 시간 종료보다 먼저).
            float reworkRate = GetWorkRatePerSecond(WorkerCount);
            if (reworkRate > 0f)
            {
                float reworkTimeLeft = (1f - ReworkProgress) / reworkRate;
                if (reworkTimeLeft <= step)
                {
                    AdvanceReflowClose(reworkTimeLeft);
                    BecomeOpen(); // 남은 dt는 버림 (일반 작업 완료 때와 동일)
                    return;
                }
            }

            TickReworkProgress(step);
            AdvanceReflowClose(step);

            if (_reflowElapsedSeconds >= _config.ReflowCloseDurationSeconds)
            {
                Progress = 0f;
                ReworkProgress = 0f; // 역류가 먼저 끝났으니 재작업 진행도는 버린다
                _reflowElapsedSeconds = 0f;
                _reflowRipplesFired = 0;
                _openElapsedSeconds = 0f;
                SetStatus(ValveStatus.Closed);

                float remaining = dt - step;
                if (remaining > 0f)
                {
                    // 완전히 닫힌 뒤 남은 시간은 그대로 다음 작업 진행에 사용 (예: 이미 작업자가 대기 중이면 바로 진행)
                    TickWorking(remaining);
                }
            }
        }

        // 역류를 seconds만큼 진행: 진행도를 줄이고, 때가 된 물소리 알림을 보낸다.
        private void AdvanceReflowClose(float seconds)
        {
            if (seconds <= 0f) return;

            _reflowElapsedSeconds += seconds;
            Progress -= seconds / _config.ReflowCloseDurationSeconds;
            FireDueRippleEvents();
        }

        // 재작업 진행도: 1인 8초, 다인 배율 동일. 손을 떼면 일반 작업과 같은 규칙(3초 유예 후 감쇠).
        // TODO(기획 확인): 역류 중 재작업을 멈췄을 때도 일반 작업처럼 유예 후 감쇠하는지 미정(지금은 같은 규칙으로 처리).
        private void TickReworkProgress(float seconds)
        {
            if (seconds <= 0f) return;

            if (WorkerCount > 0)
            {
                _noWorkerTracker.ResetGrace();
                ReworkProgress += GetWorkRatePerSecond(WorkerCount) * seconds;
            }
            else
            {
                ReworkProgress -= _noWorkerTracker.ConsumeDecay(seconds);
            }

            ReworkProgress = Clamp01(ReworkProgress);
        }

        // TODO(기획 확인): 균등 간격(30초 ÷ 3회 = 10초마다)으로 임시 처리. 정확한 타이밍 미정.
        private void FireDueRippleEvents()
        {
            float intervalSeconds = _config.ReflowCloseDurationSeconds / _config.ReflowRippleEventCount;

            while (_reflowRipplesFired < _config.ReflowRippleEventCount &&
                   _reflowElapsedSeconds >= intervalSeconds * (_reflowRipplesFired + 1) - RippleEpsilon)
            {
                _reflowRipplesFired++;
                ReflowRippleTriggered?.Invoke();
            }
        }

        private float GetWorkRatePerSecond(int workerCount)
        {
            if (workerCount <= 0) return 0f;
            if (workerCount == 1) return _config.ValveBaseProgressPerSecond;
            if (workerCount == 2) return _config.ValveBaseProgressPerSecond * _config.ValveTwoWorkerMultiplier;
            return _config.ValveBaseProgressPerSecond * _config.ValveThreeOrMoreWorkerMultiplier; // 3인 이상은 상한 배율로 동일
        }

        private void UpdateStatusFromProgress()
        {
            if (Progress <= 0f)
            {
                Progress = 0f;
                SetStatus(ValveStatus.Closed);
            }
            else if (Progress >= 1f)
            {
                BecomeOpen();
            }
            else
            {
                SetStatus(ValveStatus.Rotating);
            }
        }

        // 일반 작업 완료와 역류 중 재작업 완료가 같이 쓰는 "열림" 처리. 역류 타이머를 처음부터 다시 시작한다.
        // 작업자 목록을 비우는 이유: E를 계속 누르고 있던 사람이 남아 있으면 역류가 시작되자마자
        // 재작업으로 다시 열려버려서 역류가 영원히 안 일어나기 때문. 다시 돌리려면 역류 중에 새로 잡아야 한다.
        private void BecomeOpen()
        {
            Progress = 1f;
            ReworkProgress = 0f;
            _openElapsedSeconds = 0f;
            _reflowElapsedSeconds = 0f;
            _reflowRipplesFired = 0;
            _activeWorkerIds.Clear();
            _noWorkerTracker.ResetGrace();
            SetStatus(ValveStatus.Open); // 구독자가 WorkerCount == 0을 보도록, 비운 다음에 알린다
        }

        private void SetStatus(ValveStatus next)
        {
            if (next == Status) return;
            Status = next;
            StatusChanged?.Invoke(next);
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
