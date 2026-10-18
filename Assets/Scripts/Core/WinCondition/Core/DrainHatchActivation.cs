using System;

namespace Marco.Core.WinCondition
{
    // 배수구 2곳 중 1개를 무작위로 고르는 기능. nextIndex(2)가 0이면 First, 1이면 Second.
    public static class DrainHatchActivation
    {
        public static DrainHatchId SelectRandom(Func<int, int> nextIndex)
        {
            return nextIndex(2) == 0 ? DrainHatchId.First : DrainHatchId.Second;
        }
    }
}
