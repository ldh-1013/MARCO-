using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // 최후 생존자 페이즈의 배수구 하나. 진행도 규칙은 밸브와 같다(유예 3초, 감쇠 0.10/초)지만
    // 역류가 없고, 다인 배율도 없다(몇 명이 붙어도 속도는 그대로).
    public sealed class DrainHatch
    {
        private readonly WinConditionConfig _config;
        private readonly GraceDecayTracker _noWorkerTracker;
        private readonly HashSet<int> _activeWorkerIds = new HashSet<int>();

        private float _workSecondsRequired = 14f;

        public DrainHatchStatus Status { get; private set; } = DrainHatchStatus.Inactive;
        public float Progress { get; private set; }
        public int WorkerCount => _activeWorkerIds.Count;
        public IReadOnlyCollection<int> WorkerIds => _activeWorkerIds;

        public event Action<DrainHatchStatus> StatusChanged;

        public DrainHatch(WinConditionConfig config)
        {
            _config = config;
            _noWorkerTracker = new GraceDecayTracker(config.ValveNoWorkerGraceSeconds, config.ValveNoWorkerDecayPerSecond);
        }

        // 동시 개방 밸브 수에 따라 작업 시간(T = 11 − 3 × 동시 개방 수, 최소 1초)을 정하고 작업 가능 상태로 전환한다.
        public void Activate(int openValveCountAtActivation)
        {
            if (Status != DrainHatchStatus.Inactive) return; // 이미 활성화됐으면 무시

            _workSecondsRequired = _config.GetDrainHatchWorkSeconds(openValveCountAtActivation);
            SetStatus(DrainHatchStatus.InProgress);
        }

        public void BeginWork(int playerId) => _activeWorkerIds.Add(playerId);
        public void EndWork(int playerId) => _activeWorkerIds.Remove(playerId);

        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            if (Status != DrainHatchStatus.InProgress) return;

            if (WorkerCount > 0)
            {
                _noWorkerTracker.ResetGrace();
                Progress += dt / _workSecondsRequired; // 다인 배율 없음: 몇 명이든 속도는 동일
            }
            else
            {
                Progress -= _noWorkerTracker.ConsumeDecay(dt);
            }

            Progress = Clamp01(Progress);

            if (Progress >= 1f)
            {
                Progress = 1f;
                SetStatus(DrainHatchStatus.Completed);
            }
        }

        private void SetStatus(DrainHatchStatus next)
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
