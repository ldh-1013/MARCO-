using System;

namespace Marco.Core.WinCondition
{
    // 동시에 열려있는 밸브 수가 요구 개방 수 이상이 되는 순간 열리고, 그 뒤로는 계속 열려있다(latch).
    public sealed class Gate
    {
        private readonly int _requiredOpenValveCount;

        public bool IsOpen { get; private set; }

        public event Action GateOpened;

        public Gate(int requiredOpenValveCount)
        {
            _requiredOpenValveCount = requiredOpenValveCount;
        }

        // ValveBoard.Tick(dt) 이후의 "동시 개방 수"(ValveBoard.OpenValveCount)로 호출해야 함.
        public void Evaluate(int currentOpenValveCount)
        {
            if (IsOpen) return;

            if (currentOpenValveCount >= _requiredOpenValveCount)
            {
                IsOpen = true;
                GateOpened?.Invoke();
            }
        }
    }
}
