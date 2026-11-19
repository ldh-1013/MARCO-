namespace Marco.Core.WinCondition
{
    // "작업자가 없으면 유예 시간 동안 버티다가, 그 뒤로는 초당 일정량 줄어든다"는 규칙을 공유하는 작은 도우미.
    // Valve와 DrainHatch가 똑같이 쓴다 (밸브는 다인 배율이 있고, 배수구는 없다는 차이만 각자 Tick에서 처리).
    public sealed class GraceDecayTracker
    {
        private readonly float _graceSeconds;
        private readonly float _decayPerSecond;
        private float _noWorkerElapsedSeconds;

        public GraceDecayTracker(float graceSeconds, float decayPerSecond)
        {
            _graceSeconds = graceSeconds;
            _decayPerSecond = decayPerSecond;
        }

        // 작업자가 다시 생기면 유예 타이머를 처음부터 다시 잰다.
        public void ResetGrace()
        {
            _noWorkerElapsedSeconds = 0f;
        }

        // 작업자가 없는 채로 dt만큼 흘렀을 때 진행도에서 빼야 할 양(0 이상)을 돌려준다.
        // dt가 유예 경계를 가로질러도(유예 0.5초 남았는데 dt가 1초) 유예분과 감쇠분을 나눠서 정확히 처리.
        public float ConsumeDecay(float dt)
        {
            float remaining = dt;
            float graceLeft = _graceSeconds - _noWorkerElapsedSeconds;

            if (graceLeft > 0f)
            {
                float consumed = remaining < graceLeft ? remaining : graceLeft;
                _noWorkerElapsedSeconds += consumed;
                remaining -= consumed;
            }

            if (remaining <= 0f) return 0f;

            _noWorkerElapsedSeconds += remaining;
            return _decayPerSecond * remaining;
        }
    }
}
