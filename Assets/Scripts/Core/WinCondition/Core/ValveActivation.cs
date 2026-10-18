using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // 라운드 시작 시 활성 밸브를 고르는 기능.
    // v0.6: 활성 밸브는 항상 3개. B·E는 항상 포함되고, 나머지 1개만 A·C·D 중 무작위로 고른다.
    public static class ValveActivation
    {
        private static readonly ValveId[] WildcardCandidates = { ValveId.A, ValveId.C, ValveId.D };

        // nextIndex(n)은 0 이상 n 미만의 정수를 돌려주는 함수.
        // System.Random이면 그냥 random.Next를 넘기면 되고, 테스트에서는 고정값을 돌려주는 람다를 넘겨서 결과를 고정할 수 있음.
        // 반환 순서는 항상 [B, E, 무작위 1개] 고정 (호출하는 쪽에서 키 매핑 등에 쓰기 편하도록).
        public static IReadOnlyList<ValveId> SelectActiveValves(Func<int, int> nextIndex)
        {
            int index = nextIndex(WildcardCandidates.Length);
            var wildcard = WildcardCandidates[index];

            return new List<ValveId> { ValveId.B, ValveId.E, wildcard };
        }
    }
}
