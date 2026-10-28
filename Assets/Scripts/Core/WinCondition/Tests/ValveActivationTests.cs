using System.Collections.Generic;
using System.Linq;
using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    // 기획서 6.1-0 (10-26): A~E 5개 중 3개 무작위, 조합 10가지, 고정 활성 없음.
    public class ValveActivationTests
    {
        // 주입한 인덱스를 차례로 돌려주는 난수. 호출 때 받은 n(남은 후보 수)도 기록한다.
        private static System.Func<int, int> Sequence(IList<int> indices, List<int> askedRanges = null)
        {
            int call = 0;
            return n =>
            {
                askedRanges?.Add(n);
                return indices[call++];
            };
        }

        // 가능한 주입 순서 60가지(5 × 4 × 3)를 전부 돌린다.
        private static IEnumerable<int[]> AllIndexSequences()
        {
            for (int a = 0; a < 5; a++)
            for (int b = 0; b < 4; b++)
            for (int c = 0; c < 3; c++)
                yield return new[] { a, b, c };
        }

        private static string Key(IEnumerable<ValveId> valves) => string.Join(",", valves);

        [Test]
        public void SelectActiveValves_ReturnsConfiguredCount_WithoutDuplicates()
        {
            var config = new WinConditionConfig();

            foreach (var sequence in AllIndexSequences())
            {
                var selected = ValveActivation.SelectActiveValves(Sequence(sequence), config.ActiveValveCount);

                Assert.AreEqual(3, selected.Count);
                Assert.AreEqual(3, selected.Distinct().Count());
            }
        }

        // 10가지 조합 전수: 모든 조합이 나오고, 각각 정확히 6번(60 ÷ 10)씩 나와 고르게 뽑힌다.
        [Test]
        public void SelectActiveValves_CoversAllTenCombinations_Evenly()
        {
            var counts = new Dictionary<string, int>();
            foreach (var sequence in AllIndexSequences())
            {
                string key = Key(ValveActivation.SelectActiveValves(Sequence(sequence)));
                counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
            }

            var expected = new[]
            {
                "A,B,C", "A,B,D", "A,B,E", "A,C,D", "A,C,E",
                "A,D,E", "B,C,D", "B,C,E", "B,D,E", "C,D,E",
            };
            CollectionAssert.AreEquivalent(expected, counts.Keys);
            foreach (var pair in counts) Assert.AreEqual(6, pair.Value, pair.Key);
        }

        // 조합마다 그 조합이 나오는 주입값 하나 — 결과가 주입한 난수로만 정해지는지 확인.
        [TestCase(0, 0, 0, new[] { ValveId.A, ValveId.B, ValveId.C })]
        [TestCase(0, 0, 1, new[] { ValveId.A, ValveId.B, ValveId.D })]
        [TestCase(0, 0, 2, new[] { ValveId.A, ValveId.B, ValveId.E })]
        [TestCase(0, 1, 1, new[] { ValveId.A, ValveId.C, ValveId.D })]
        [TestCase(0, 1, 2, new[] { ValveId.A, ValveId.C, ValveId.E })]
        [TestCase(0, 2, 2, new[] { ValveId.A, ValveId.D, ValveId.E })]
        [TestCase(1, 1, 1, new[] { ValveId.B, ValveId.C, ValveId.D })]
        [TestCase(1, 1, 2, new[] { ValveId.B, ValveId.C, ValveId.E })]
        [TestCase(1, 2, 2, new[] { ValveId.B, ValveId.D, ValveId.E })]
        [TestCase(4, 3, 2, new[] { ValveId.C, ValveId.D, ValveId.E })]
        public void SelectActiveValves_InjectedIndices_GiveExpectedCombination(int first, int second, int third, ValveId[] expected)
        {
            var selected = ValveActivation.SelectActiveValves(Sequence(new[] { first, second, third }));

            CollectionAssert.AreEqual(expected, selected); // ValveId 오름차순
        }

        // 남은 후보에서 하나씩 뽑으므로 난수는 5 → 4 → 3 범위로 요청된다.
        [Test]
        public void SelectActiveValves_AsksForShrinkingRanges()
        {
            var asked = new List<int>();

            ValveActivation.SelectActiveValves(Sequence(new[] { 0, 0, 0 }, asked));

            CollectionAssert.AreEqual(new[] { 5, 4, 3 }, asked);
        }

        // B · E 고정은 폐지됐다 — 둘 다 빠지는 조합(A, C, D)도 나온다.
        [Test]
        public void SelectActiveValves_NoFixedValves_ACDIsPossible()
        {
            var selected = ValveActivation.SelectActiveValves(Sequence(new[] { 0, 1, 1 }));

            CollectionAssert.DoesNotContain(selected, ValveId.B);
            CollectionAssert.DoesNotContain(selected, ValveId.E);
        }
    }
}
