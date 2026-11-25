using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // 살아있는 Runner가 1명이 되는 순간 1회 진입하는 페이즈. 제한 90초(라운드 잔여가 더 짧으면 그쪽).
    public sealed class LastSurvivorPhase
    {
        private readonly WinConditionConfig _config;
        private readonly Dictionary<DrainHatchId, DrainHatch> _drainHatches;

        public bool HasEntered { get; private set; }
        public bool IsActive { get; private set; }
        public DrainHatchId? ActiveDrainHatchId { get; private set; }
        public float RemainingSeconds { get; private set; }

        public LastSurvivorPhase(WinConditionConfig config)
        {
            _config = config;
            _drainHatches = new Dictionary<DrainHatchId, DrainHatch>
            {
                { DrainHatchId.First, new DrainHatch(config) },
                { DrainHatchId.Second, new DrainHatch(config) },
            };
        }

        public DrainHatch GetDrainHatch(DrainHatchId id) => _drainHatches[id];

        // gateIsOpen이면 배수구를 활성화하지 않는다. 아니면 2곳 중 무작위 1개를 nextIndex로 고른다.
        public void Enter(bool gateIsOpen, int openValveCount, float roundRemainingSeconds, Func<int, int> nextIndex)
        {
            if (HasEntered) return;

            HasEntered = true;
            IsActive = true;
            RemainingSeconds = Math.Min(_config.LastSurvivorPhaseDurationSeconds, roundRemainingSeconds);

            if (!gateIsOpen)
            {
                var chosenId = DrainHatchActivation.SelectRandom(nextIndex);
                ActiveDrainHatchId = chosenId;
                _drainHatches[chosenId].Activate(openValveCount);
            }
        }

        public void Tick(float dt)
        {
            if (!IsActive) return;

            foreach (var hatch in _drainHatches.Values)
            {
                hatch.Tick(dt);
            }

            RemainingSeconds -= dt;

            if (RemainingSeconds <= 0f)
            {
                IsActive = false;
            }
        }
    }
}
