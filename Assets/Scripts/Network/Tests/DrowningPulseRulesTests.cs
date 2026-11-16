using Marco.Network.Player;
using Marco.Network.Round;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 부록 A [숨 게이지] 질식 페널티 · §5.7 (1216-1221행): who receives the drowning ring and what it looks like for them.
    public class DrowningPulseRulesTests
    {
        private const float T = 1e-4f;

        [Test]
        public void Runner_HeardInTheOpen()
        {
            var p = DrowningPulseRules.For(PlayerStatus.Alive, false, false, true, false, 22f, 1f, true);
            Assert.IsTrue(p.Send);
            Assert.AreEqual(22f, p.Radius, T);
            Assert.AreEqual(2.5f, p.Seconds, T);
        }

        [Test]
        public void Seeker_26point4m_3point75s()
        {
            // SoundWorld gives the Seeker the ×1.2 perceived radius; the duration ×1.5 comes from PulseSpec.ListenerSeconds.
            var p = DrowningPulseRules.For(PlayerStatus.Alive, true, false, true, false, 22f * PulseSpec.SeekerHearing, 1f, true);
            Assert.IsTrue(p.Send);
            Assert.AreEqual(26.4f, p.Radius, T);
            Assert.AreEqual(3.75f, p.Seconds, T);
        }

        [TestCase(PlayerStatus.Tagged)]
        [TestCase(PlayerStatus.Escaped)]
        public void EchoBody_AlwaysGetsIt_FarAndBlocked(PlayerStatus status)
        {
            // 100 m away and behind walls: an Echo body ignores distance and occlusion (5.7 1218행). Radius = base 22 m;
            // duration = the current Echo rule of PulseSpec.ListenerSeconds (3 s — the 3.2-1 fix is a later step).
            var p = DrowningPulseRules.For(status, false, true, false, true, 0f, 1f, true);
            Assert.IsTrue(p.Send);
            Assert.AreEqual(22f, p.Radius, T);
            Assert.AreEqual(PulseSpec.EchoSonarSeconds, p.Seconds, T);
        }

        [Test]
        public void Runner_Blocked_WithDrowningOcclusion_NotSent() =>
            Assert.IsFalse(DrowningPulseRules.For(PlayerStatus.Alive, false, false, true, true, 11f, .5f, true).Send);

        [Test]
        public void Runner_Blocked_WithoutDrowningOcclusion_SentWithItsOcclusion()
        {
            var p = DrowningPulseRules.For(PlayerStatus.Alive, false, false, true, true, 11f, .5f, false);
            Assert.IsTrue(p.Send);
            Assert.AreEqual(11f, p.Radius, T);
            Assert.AreEqual(1.25f, p.Seconds, T, "2.5 s × 0.5 (부록 A 지속시간 최소 유지 배율)");
        }

        [Test]
        public void Spectator_NotSent() =>
            Assert.IsFalse(DrowningPulseRules.For(PlayerStatus.Spectating, false, true, true, false, 22f, 1f, true).Send);

        [Test]
        public void Runner_OutOfRange_NotSent() =>
            Assert.IsFalse(DrowningPulseRules.For(PlayerStatus.Alive, false, false, false, false, 22f, 1f, true).Send);

        [Test]
        public void DefaultOverload_UsesTheServerSwitch() =>
            Assert.AreEqual(!RippleServerFeatures.DrowningOcclusion,
                DrowningPulseRules.For(PlayerStatus.Alive, false, false, true, true, 11f, .5f).Send);
    }
}
