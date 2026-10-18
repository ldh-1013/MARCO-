namespace Marco.Core.WinCondition
{
    // 서버가 "playerId가 valveId를 잡았다/놨다"를 호출하는 입구.
    // 역할(role)은 인자로 받지 않고, IPlayerRoster에서 직접 조회해서 확인한다.
    public sealed class ValveInteractionService
    {
        private readonly ValveBoard _valveBoard;
        private readonly Gate _gate;
        private readonly IPlayerRoster _playerRoster;
        private readonly IValveSoundSink _soundSink;
        private readonly IRoundEventSink _roundEventSink;

        // 최후 생존자 페이즈에서 배수구 작업 시간(T = 11 − 3 × 동시 개방 수, 최소 1초)을 정할 때 필요함.
        public int OpenValveCount => _valveBoard.OpenValveCount;

        public ValveInteractionService(
            ValveBoard valveBoard,
            Gate gate,
            IPlayerRoster playerRoster,
            IValveSoundSink soundSink,
            IRoundEventSink roundEventSink)
        {
            _valveBoard = valveBoard;
            _gate = gate;
            _playerRoster = playerRoster;
            _soundSink = soundSink;
            _roundEventSink = roundEventSink;

            foreach (var pair in _valveBoard.Valves)
            {
                WireValve(pair.Key, pair.Value);
            }

            _gate.GateOpened += () => _roundEventSink.OnGateOpened();
        }

        private void WireValve(ValveId valveId, Valve valve)
        {
            valve.StatusChanged += status =>
            {
                _roundEventSink.OnValveStatusChanged(valveId, status);
                _roundEventSink.OnValveProgressChanged(valveId, valve.Progress);

                // 상태가 바뀔 때마다 게이트를 재평가한다. latch라서 여러 번 평가해도 안전하고,
                // 한 Tick 안에서 밸브 여러 개가 같이 전환돼도 마지막 평가 시점엔 정확한 동시 개방 수를 보게 된다.
                _gate.Evaluate(_valveBoard.OpenValveCount);
            };

            valve.ReflowRippleTriggered += () =>
            {
                _soundSink.NotifyValveSound(valveId, ValveSoundEvent.ReflowRipple);
            };
        }

        // 살아있는 Runner만 밸브를 잡을 수 있다 (Seeker, Echo 불가).
        public bool TryBeginWork(int playerId, ValveId valveId)
        {
            if (!_valveBoard.Valves.TryGetValue(valveId, out var valve)) return false;
            if (!IsAliveRunner(playerId)) return false;

            bool wasWorking = valve.WorkerCount > 0;
            valve.BeginWork(playerId);

            if (!wasWorking && valve.WorkerCount > 0)
            {
                _soundSink.NotifyValveSound(valveId, ValveSoundEvent.RotatingStarted);
            }

            return true;
        }

        // 놓는 건 항상 허용 (거부 조건 없음).
        public void EndWork(int playerId, ValveId valveId)
        {
            if (!_valveBoard.Valves.TryGetValue(valveId, out var valve)) return;

            bool wasWorking = valve.WorkerCount > 0;
            valve.EndWork(playerId);

            if (wasWorking && valve.WorkerCount == 0)
            {
                _soundSink.NotifyValveSound(valveId, ValveSoundEvent.RotatingStopped);
            }
        }

        // 작업 중이던 플레이어가 탈락/연결 끊김이 되면 모든 밸브의 작업자 목록에서 뺀다.
        // "작업 중단"과 같은 취급이라 리셋은 아니고, EndWork를 재사용한다.
        public void RemovePlayerFromAllValves(int playerId)
        {
            foreach (var valveId in _valveBoard.Valves.Keys)
            {
                EndWork(playerId, valveId);
            }
        }

        private bool IsAliveRunner(int playerId)
        {
            return _playerRoster.GetRole(playerId) == PlayerRole.Runner && _playerRoster.IsAlive(playerId);
        }
    }
}
