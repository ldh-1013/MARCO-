namespace Marco.Core.WinCondition
{
    // 남경(소리·파문) 파트가 구현. 밸브 ID와 소리 종류만 넘기고, 반경 같은 숫자는 넘기지 않음.
    public interface IValveSoundSink
    {
        void NotifyValveSound(ValveId valveId, ValveSoundEvent soundEvent);
    }
}
