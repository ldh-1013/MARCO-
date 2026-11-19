using System.Collections.Generic;
using Marco.Core.WinCondition;

namespace Marco.Core.WinCondition.Tests
{
    // IValveSoundSink 가짜 구현. 호출된 내용을 그대로 기록만 한다.
    public sealed class FakeValveSoundSink : IValveSoundSink
    {
        public readonly List<(ValveId ValveId, ValveSoundEvent SoundEvent)> Notifications =
            new List<(ValveId ValveId, ValveSoundEvent SoundEvent)>();

        public void NotifyValveSound(ValveId valveId, ValveSoundEvent soundEvent)
        {
            Notifications.Add((valveId, soundEvent));
        }
    }
}
