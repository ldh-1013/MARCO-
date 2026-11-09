using Marco.Network.Character;
using Marco.Network.Player;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // 메아리 몸 규칙 (기획서 3.2 · 3.3-3 · 13.3, 11-10). NetworkPlayer.ApplyLook · PlayerMovement · PlayerInteractor가
    // 이 규칙만 읽으므로, 상태 전환과 라운드 리셋 뒤의 몸 · 충돌 · 색 · 가시성을 여기서 고정한다.
    public class EchoBodyRulesTests
    {
        private const PlayerStatus Alive = PlayerStatus.Alive;
        private const PlayerStatus Tagged = PlayerStatus.Tagged;
        private const PlayerStatus Escaped = PlayerStatus.Escaped;
        private const PlayerStatus Joiner = PlayerStatus.Spectating;

        [TestCase(Alive, false)]
        [TestCase(Tagged, true)]
        [TestCase(Escaped, true)]
        [TestCase(Joiner, true)]
        public void EchoBody_IsTaggedEscapedOrMidRoundJoiner(PlayerStatus status, bool expected)
        {
            Assert.AreEqual(expected, EchoBodyRules.HasEchoBody(status));
            Assert.AreEqual(!expected, EchoBodyRules.HasCollision(status));
        }

        [TestCase(Alive, GhostLook.None)]
        [TestCase(Tagged, GhostLook.Echo)]
        [TestCase(Escaped, GhostLook.Escapee)]
        [TestCase(Joiner, GhostLook.Escapee)]
        public void GhostColour_PurpleForEliminated_CyanForEscapeeAndJoiner(PlayerStatus status, GhostLook expected)
        {
            Assert.AreEqual(expected, EchoBodyRules.LookOf(status));
        }

        // Survivors (alive Runner or Seeker — both Alive) never see an Echo body; Echo bodies see each other
        // in every combination, eliminated and escapee alike; living bodies are seen by everyone.
        [TestCase(Escaped, Alive, false)]
        [TestCase(Tagged, Alive, false)]
        [TestCase(Joiner, Alive, false)]
        [TestCase(Escaped, Tagged, true)]
        [TestCase(Tagged, Escaped, true)]
        [TestCase(Escaped, Escaped, true)]
        [TestCase(Tagged, Tagged, true)]
        [TestCase(Joiner, Escaped, true)]
        [TestCase(Escaped, Joiner, true)]
        [TestCase(Alive, Alive, true)]
        [TestCase(Alive, Escaped, true)]
        [TestCase(Alive, Tagged, true)]
        public void Visibility(PlayerStatus subject, PlayerStatus viewer, bool expected)
        {
            Assert.AreEqual(expected, EchoBodyRules.IsBodyVisible(subject, viewer));
        }

        [Test]
        public void NoLocalViewer_HidesEchoBodies_ShowsLivingOnes()
        {
            Assert.IsFalse(EchoBodyRules.IsBodyVisible(Escaped, null));
            Assert.IsTrue(EchoBodyRules.IsBodyVisible(Alive, null));
        }

        [TestCase(Alive, false)]
        [TestCase(Tagged, true)]
        [TestCase(Escaped, false)]
        [TestCase(Joiner, false)]
        public void OnlyTheEliminatedEchoKnocks(PlayerStatus status, bool expected)
        {
            Assert.AreEqual(expected, EchoBodyRules.CanKnock(status));
            Assert.AreEqual(expected, EchoBodyRules.HasTagBlackout(status));
        }

        [TestCase(Alive, true)]
        [TestCase(Tagged, false)]
        [TestCase(Escaped, false)]
        [TestCase(Joiner, false)]
        public void OnlyAlivePlayersInteract(PlayerStatus status, bool expected)
        {
            Assert.AreEqual(expected, EchoBodyRules.CanInteract(status));
        }

        [TestCase(Alive, Escaped, true)]
        [TestCase(Alive, Tagged, false)]
        [TestCase(Alive, Joiner, false)]
        [TestCase(Escaped, Alive, false)]
        [TestCase(Escaped, Escaped, false)]
        public void EscapeFade_OnlyOnAliveToEscaped(PlayerStatus from, PlayerStatus to, bool expected)
        {
            Assert.AreEqual(expected, EchoBodyRules.HasEscapeFade(from, to));
        }

        // Rematch and lobby return set everyone back to Alive (NetworkRound.StartRound / ReturnToLobby):
        // the escapee and the mid-round joiner come back as ordinary living bodies.
        [TestCase(Escaped)]
        [TestCase(Joiner)]
        [TestCase(Tagged)]
        public void BackToAlive_RestoresLivingBody(PlayerStatus during)
        {
            Assert.IsTrue(EchoBodyRules.HasEchoBody(during));

            PlayerStatus after = Alive;
            Assert.IsFalse(EchoBodyRules.HasEchoBody(after));
            Assert.IsTrue(EchoBodyRules.HasCollision(after));
            Assert.AreEqual(GhostLook.None, EchoBodyRules.LookOf(after));
            Assert.IsTrue(EchoBodyRules.IsBodyVisible(after, Alive));
            Assert.IsTrue(EchoBodyRules.CanInteract(after));
            Assert.IsFalse(EchoBodyRules.HasEscapeFade(during, after));
        }
    }
}
