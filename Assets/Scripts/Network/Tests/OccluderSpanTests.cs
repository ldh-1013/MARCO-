using System.Collections.Generic;
using Marco.Network.Audio;
using Marco.Network.Round;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 부록 A [사운드 차폐] 차폐 셈 단위 · 지속시간 최소 유지 배율 [11-16] — pure functions, no scene.
    public class OccluderSpanTests
    {
        private static int Count(params OccluderSpan[] spans) => OccluderSpans.Count(new List<OccluderSpan>(spans), PulseSpec.OccluderMergeGap);

        [Test]
        public void MergeGap_IsCapsuleDiameter() => Assert.AreEqual(.7f, PulseSpec.OccluderMergeGap, 1e-4f);

        [Test]
        public void Gap0point69_IsOneWall() => Assert.AreEqual(1, Count(new OccluderSpan(1f, 1.2f, false), new OccluderSpan(1.89f, 2.1f, false)));

        [Test]
        public void Gap0point71_IsTwoWalls() => Assert.AreEqual(2, Count(new OccluderSpan(1f, 1.2f, false), new OccluderSpan(1.91f, 2.1f, false)));

        [Test]
        public void Overlap_IsOneWall() => Assert.AreEqual(1, Count(new OccluderSpan(1f, 2f, false), new OccluderSpan(1.5f, 3f, false)));

        [Test]
        public void BlockWithASlab_IsTwoWalls()
        {
            Assert.AreEqual(2, Count(new OccluderSpan(3f, 3.2f, true), new OccluderSpan(3.2f, 3.4f, false)), "slab + wall touching");
            Assert.AreEqual(2, Count(new OccluderSpan(3f, 3.2f, true), new OccluderSpan(3.3f, 3.5f, true)), "two touching slabs");
            Assert.AreEqual(3, Count(new OccluderSpan(1f, 1.2f, false), new OccluderSpan(5f, 5.2f, true)), "a wall and a separate slab");
        }

        [Test]
        public void Empty_IsZero()
        {
            Assert.AreEqual(0, Count());
            Assert.AreEqual(0, OccluderSpans.Count(null, PulseSpec.OccluderMergeGap));
        }

        [Test]
        public void Unsorted_IsSortedFirst() => Assert.AreEqual(2, Count(new OccluderSpan(5f, 6f, false), new OccluderSpan(1f, 1.5f, false), new OccluderSpan(1.6f, 2f, false)));

        [TestCase(0, 0, false, 1f)]
        [TestCase(1, 0, false, .5f)]
        [TestCase(2, 0, false, .5f)]
        [TestCase(0, 1, false, .5f)]
        [TestCase(3, 1, true, 1f)]
        public void OccludedDurationScale(int walls, int water, bool echo, float expected) =>
            Assert.AreEqual(expected, PulseSpec.OccludedDurationScale(walls, water, echo), 1e-4f);

        [Test]
        public void ListenerSeconds_SeekerTalkThroughAWall_Is0point9()
        {
            float scale = PulseSpec.OccludedDurationScale(1, 0, false);
            Assert.AreEqual(1.2f * 1.5f * .5f, PulseSpec.ListenerSeconds(PulseSpec.TalkSeconds, true, false, false, scale), 1e-4f);
            Assert.AreEqual(.9f, PulseSpec.ListenerSeconds(PulseSpec.TalkSeconds, true, false, false, scale), 1e-4f);
        }

        [Test]
        public void ListenerSeconds_EchoBodyUnchanged()
        {
            // Echo bodies ignore occlusion (scale 1) and keep the current ring rule until the 3.2-1 fix (개정이력 11-16).
            Assert.AreEqual(PulseSpec.EchoSonarSeconds, PulseSpec.ListenerSeconds(1.2f, false, true, true, 1f), 1e-4f);
            Assert.AreEqual(1.2f, PulseSpec.ListenerSeconds(1.2f, true, true, false, 1f), 1e-4f);
            Assert.AreEqual(1.2f, PulseSpec.ListenerSeconds(1.2f, false, false, false, 1f), 1e-4f, "runner, open");
        }
    }
}
