using Marco.Network.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // 부록 A [사운드 차폐] 수면 경유 판정 [11-16].
    public class WaterPathRulesTests
    {
        private const float T = 1e-4f;

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void SameSide_OneStraightLeg(bool aUnder, bool bUnder)
        {
            Vector3 a = new Vector3(1, -2, 3), b = new Vector3(5, 1.6f, 7);
            var legs = WaterPathRules.Build(a, aUnder, b, bUnder, 0f);
            Assert.AreEqual(1, legs.Count);
            Assert.AreEqual(a, legs[0].From);
            Assert.AreEqual(b, legs[0].To);
        }

        [Test]
        public void UnderToAbove_TwoLegsThroughTheSurfaceAboveTheUnderwaterEnd()
        {
            Vector3 under = new Vector3(7, -3.5f, 47), above = new Vector3(11.4f, 1.6f, 47);
            var legs = WaterPathRules.Build(under, true, above, false, 0f);
            Assert.AreEqual(2, legs.Count);
            var surface = new Vector3(7, 0, 47);
            Assert.AreEqual(under, legs[0].From);
            Assert.AreEqual(surface, legs[0].To);
            Assert.AreEqual(surface, legs[1].From);
            Assert.AreEqual(above, legs[1].To);
            Assert.AreEqual(3.5f, legs[0].Length, T, "straight up");
        }

        [Test]
        public void AboveToUnder_SamePathReversed()
        {
            Vector3 under = new Vector3(7, -3.5f, 47), above = new Vector3(11.4f, 1.6f, 47);
            var forward = WaterPathRules.Build(under, true, above, false, -.2f);
            var back = WaterPathRules.Build(above, false, under, true, -.2f);
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual(forward[1].To, back[0].From);
            Assert.AreEqual(forward[0].To, back[0].To, "the same surface point (under x, surfaceY, under z)");
            Assert.AreEqual(forward[0].From, back[1].To);
            Assert.AreEqual(forward[0].Length + forward[1].Length, back[0].Length + back[1].Length, T);
        }
    }
}
