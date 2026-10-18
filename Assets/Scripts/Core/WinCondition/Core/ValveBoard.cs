using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // 이번 라운드에 활성화된 밸브들을 모아서 관리한다.
    public sealed class ValveBoard
    {
        private readonly Dictionary<ValveId, Valve> _valves = new Dictionary<ValveId, Valve>();

        public IReadOnlyDictionary<ValveId, Valve> Valves => _valves;

        public ValveBoard(IEnumerable<ValveId> activeValveIds, WinConditionConfig config)
        {
            foreach (var id in activeValveIds)
            {
                _valves[id] = new Valve(config);
            }
        }

        public void Tick(float dt)
        {
            foreach (var valve in _valves.Values)
            {
                valve.Tick(dt);
            }
        }

        public Valve GetValve(ValveId id) => _valves[id];

        // 지금 이 순간 동시에 Open 상태인 밸브 수. Reflowing(닫히는 중)은 포함하지 않음.
        public int OpenValveCount
        {
            get
            {
                int count = 0;
                foreach (var valve in _valves.Values)
                {
                    if (valve.Status == ValveStatus.Open) count++;
                }

                return count;
            }
        }
    }
}
