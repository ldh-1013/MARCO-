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

        // Reflowing 상태: 작업자가 없으면 30초에 걸쳐 진행도를 1 -> 0으로 줄이고, 균등 간격으로 물소리 알림을 보낸다.
        // 작업자가 있으면(v0.6: 역류 중 재작업 가능) 닫히는 걸 멈추고 평소와 같은 속도로 다시 진행도를 올린다.
        private void TickReflowing(float dt)
        {
            if (WorkerCount > 0)
            {
                TickReworkDuringReflow(dt);
                return;
            }

            float remaining = dt;
            float closeLeft = _config.ReflowCloseDurationSeconds - _reflowElapsedSeconds;
            float consumed = remaining < closeLeft ? remaining : closeLeft;

            if (consumed > 0f)
            {
                _reflowElapsedSeconds += consumed;
                Progress -= consumed / _config.ReflowCloseDurationSeconds;
                remaining -= consumed;
                FireDueRippleEvents();
            }

            if (_reflowElapsedSeconds >= _config.ReflowCloseDurationSeconds)
            {
                Progress = 0f;
                _reflowElapsedSeconds = 0f;
                _reflowRipplesFired = 0;
                _openElapsedSeconds = 0f;
                SetStatus(ValveStatus.Closed);

                if (remaining > 0f)
                {
                    // 완전히 닫힌 뒤 남은 시간은 그대로 다음 작업 진행에 사용 (예: 이미 작업자가 대기 중이면 바로 진행)
                    TickWorking(remaining);
                }
            }
        }

        // 역류 중 재작업: 회전에 필요한 시간은 처음과 같음(1인 8초, 다인 배율 동일) → 평소와 같은 작업 속도로 Progress를 올린다.
        // TODO(기획 확인): "역류 중 재작업 진행도를 어떻게 셀지" 공식 답변 없음.
        // 지금은 역류로 줄어든 현재 Progress에서 이어서(처음부터 다시 X) 같은 속도로 올라가는 것으로 임시 처리함
        // (일반 작업 중단 후 재개 때의 기존 동작과 동일한 방식이라 일관성 있게 골랐음).
        // 완료 전까지는 Status를 Reflowing으로 유지해야 함(Rotating으로 바꾸면 다음 Tick이 TickWorking 경로로 새서
        // 역류 타이머 상태가 깨짐 - UpdateStatusFromProgress를 그대로 재사용하면 안 되는 이유).
        private void TickReworkDuringReflow(float dt)
        {
            Progress += GetWorkRatePerSecond(WorkerCount) * dt;

            if (Progress >= 1f)
            {
                Progress = 1f;
                _openElapsedSeconds = 0f;
                _reflowElapsedSeconds = 0f;
                _reflowRipplesFired = 0;
                SetStatus(ValveStatus.Open); // 180초 유예가 처음부터 다시 시작됨
            }
            else
            {
                Progress = Clamp01(Progress);
                // 아직 미완료: Status는 Reflowing 유지 (다음 Tick도 이 메서드를 계속 탐)
            }
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
                Progress = 1f;

                if (Status != ValveStatus.Open)
                {
                    // 새로 열렸으니 역류 타이머를 처음부터 다시 시작
                    _openElapsedSeconds = 0f;
                    _reflowElapsedSeconds = 0f;
                    _reflowRipplesFired = 0;
                }

                SetStatus(ValveStatus.Open);
            }
            else
            {
                SetStatus(ValveStatus.Rotating);
            }
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
