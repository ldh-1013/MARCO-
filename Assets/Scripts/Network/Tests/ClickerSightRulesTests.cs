using Marco.Network.Map;
using Marco.Network.Player;
using Marco.Network.Round;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 7절 · 부록 A [아이템] 섬광 대상 [11-16] and the shared structure test (5.6 차폐 구조물).
    public class ClickerSightRulesTests
    {
        [Test]
        public void User_AlwaysReceives()
        {
            Assert.IsTrue(ClickerSightRules.Receives(true, PlayerStatus.Alive, false, false), "self, even with nothing in range");
        }

        [TestCase(PlayerStatus.Alive, true)]
        [TestCase(PlayerStatus.Tagged, true)]      // Echo bodies receive it
        [TestCase(PlayerStatus.Escaped, false)]
        [TestCase(PlayerStatus.Spectating, false)]
        public void Others_InRangeAndClear(PlayerStatus status, bool expected) =>
            Assert.AreEqual(expected, ClickerSightRules.Receives(false, status, true, true));

        [Test]
        public void Others_OutOfRangeOrBlocked()
        {
            Assert.IsFalse(ClickerSightRules.Receives(false, PlayerStatus.Alive, false, true), "out of 6 m");
            Assert.IsFalse(ClickerSightRules.Receives(false, PlayerStatus.Alive, true, false), "behind a structure");
        }

        [TestCase("Wall", false, true)]
        [TestCase("Floor", false, true)]
        [TestCase("Ceiling", false, true)]
        [TestCase("NonWalkable", true, true)]   // soundOccluder flag
        [TestCase("Obstacle", false, false)]
        [TestCase("Door", false, false)]
        public void SoundStructure(string category, bool occluder, bool expected) =>
            Assert.AreEqual(expected, MapPart.IsStructure(category, occluder));
    }
}
