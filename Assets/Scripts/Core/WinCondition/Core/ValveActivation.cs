using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // 라운드 시작 시 활성 밸브를 고르는 기능.
    // 10-26: 고정 활성 없이 A~E 5개 중 3개를 무작위로 고른다(조합 10가지, 기획서 6.1-0).
    // 활성 목록은 서버만 보관한다 — 클라이언트에 보내지 않는다(14.3 ValveActivated).
    public static class ValveActivation
    {
        private static readonly ValveId[] Candidates = { ValveId.A, ValveId.B, ValveId.C, ValveId.D, ValveId.E };

        // nextIndex(n)은 0 이상 n 미만의 정수를 돌려주는 함수.
        // System.Random이면 그냥 random.Next를 넘기면 되고, 테스트에서는 정해진 값을 돌려주는 람다를 넘겨서 결과를 고정할 수 있음.
        // 남은 후보에서 하나씩 뽑는다(nextIndex(5) → nextIndex(4) → nextIndex(3)). 60가지 순서가 10가지 조합에 6번씩 고르게 대응한다.
        // 반환 순서는 항상 ValveId 오름차순 (뽑은 순서를 노출하지 않도록).
        public static IReadOnlyList<ValveId> SelectActiveValves(Func<int, int> nextIndex, int activeCount = 3)
        {
            var remaining = new List<ValveId>(Candidates);
            var selected = new List<ValveId>(activeCount);

            for (int i = 0; i < activeCount; i++)
            {
                int index = nextIndex(remaining.Count);
                selected.Add(remaining[index]);
                remaining.RemoveAt(index);
            }

            selected.Sort();
            return selected;
        }
    }
}
