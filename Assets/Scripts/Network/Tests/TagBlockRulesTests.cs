using Marco.Network.Round;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 부록 A [태그 판정] 벽 차단 [11-16]: only tagBlocker map parts stop a tag.
    public class TagBlockRulesTests
    {
        [Test]
        public void NoPart_DoesNotBlock() => Assert.IsFalse(TagBlockRules.Blocks(false, false), "furniture · props · pillars without a map part");

        [Test]
        public void TagBlockerPart_Blocks() => Assert.IsTrue(TagBlockRules.Blocks(true, true));

        [Test]
        public void NonBlockerPart_DoesNotBlock() => Assert.IsFalse(TagBlockRules.Blocks(true, false), "Obstacle · Door · WaterVolume parts");
    }
}
