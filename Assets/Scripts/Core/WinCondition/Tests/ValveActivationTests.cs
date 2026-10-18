using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    public class ValveActivationTests
    {
        [Test]
        public void SelectActiveValves_AlwaysReturnsThreeValves()
        {
            var selected = ValveActivation.SelectActiveValves(_ => 0);

            Assert.AreEqual(3, selected.Count);
        }

        [Test]
        public void SelectActiveValves_AlwaysIncludesBAndE()
        {
            var selected = ValveActivation.SelectActiveValves(_ => 1);

            CollectionAssert.Contains(selected, ValveId.B);
            CollectionAssert.Contains(selected, ValveId.E);
        }

        [TestCase(0, ValveId.A)]
        [TestCase(1, ValveId.C)]
        [TestCase(2, ValveId.D)]
        public void SelectActiveValves_ThirdValveComesFromNextIndex_AmongACD(int injectedIndex, ValveId expectedWildcard)
        {
            var selected = ValveActivation.SelectActiveValves(_ => injectedIndex);

            CollectionAssert.AreEqual(new[] { ValveId.B, ValveId.E, expectedWildcard }, selected);
        }
    }
}
