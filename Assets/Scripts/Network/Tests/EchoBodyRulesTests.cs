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

        // 노크 서버 판정 3~6 (부록 A [메아리]: 전환 후 20초 잠금 · 쿨다운 25초 · 라운드당 5회 · 대기 중 1건).
        [TestCase(10f, 0f, float.NegativeInfinity, 0, false, false)]   // 잠금 20초 안
        [TestCase(20f, 0f, float.NegativeInfinity, 0, false, true)]    // 잠금 끝
        [TestCase(40f, 0f, 20f, 1, false, false)]                     // 쿨다운 25초 안
        [TestCase(45f, 0f, 20f, 1, false, true)]                      // 쿨다운 끝
        [TestCase(500f, 0f, 100f, 5, false, false)]                   // 6번째
        [TestCase(500f, 0f, 100f, 4, false, true)]                    // 5번째
        [TestCase(500f, 0f, 100f, 1, true, false)]                    // 이미 대기 중
        [TestCase(float.NaN, 0f, float.NegativeInfinity, 0, false, false)]   // 비정상 입력: 거부
        public void Knock_AcceptsOnlyAfterLockCooldownWithinLimit(float now, float taggedAt, float last, int used, bool pending, bool expected) =>
            Assert.AreEqual(expected, Marco.Network.Round.EchoKnockRules.Accept(now, taggedAt, last, used, pending));

        [Test]
        public void Knock_OnlyEliminatedEchoMayKnock()
        {
            Assert.IsTrue(EchoBodyRules.CanKnock(Tagged));
            Assert.IsFalse(EchoBodyRules.CanKnock(Escaped));
            Assert.IsFalse(EchoBodyRules.CanKnock(Joiner));
            Assert.IsFalse(EchoBodyRules.CanKnock(Alive));
        }

        [TestCase(0f, 0f, true)]
        [TestCase(2.99f, 2.99f, true)]
        [TestCase(3f, 3f, false)]           // 3.0초: 조작 가능
        [TestCase(1f, 3.1f, false)]         // 실시간 3.0 + 0.1초를 넘으면 무조건 해제
        [TestCase(-.1f, 0f, false)]
        [TestCase(float.NaN, 0f, false)]
        [TestCase(1f, float.NaN, false)]
        [TestCase(float.PositiveInfinity, 0f, false)]
        public void TagBlackout_InputLockedForThreeSecondsOnly(float elapsed, float real, bool expected) =>
            Assert.AreEqual(expected, EchoBodyRules.BlocksTagInput(elapsed, real));

        [Test]
        public void TagBlackout_WhiteFlashThenBlackThenClears()
        {
            Assert.AreEqual(1f, EchoBodyRules.TagBlackoutCover(0f, out float shade)); Assert.AreEqual(1f, shade);      // 백색
            Assert.AreEqual(1f, EchoBodyRules.TagBlackoutCover(.1f, out shade)); Assert.AreEqual(1f, shade);
            Assert.AreEqual(1f, EchoBodyRules.TagBlackoutCover(.325f, out shade)); Assert.AreEqual(.5f, shade, 1e-4f);  // 암전 중
            Assert.AreEqual(1f, EchoBodyRules.TagBlackoutCover(.5f, out shade)); Assert.AreEqual(0f, shade);           // 완전 암전
            Assert.AreEqual(1f, EchoBodyRules.TagBlackoutCover(2.99f, out shade)); Assert.AreEqual(0f, shade);
            Assert.AreEqual(.5f, EchoBodyRules.TagBlackoutCover(3.25f, out shade), 1e-4f);                             // 소나 페이드 인
            Assert.AreEqual(0f, EchoBodyRules.TagBlackoutCover(3.5f, out _));
            Assert.AreEqual(0f, EchoBodyRules.TagBlackoutCover(float.PositiveInfinity, out _));
            Assert.AreEqual(0f, EchoBodyRules.TagBlackoutCover(float.NaN, out _));
            Assert.AreEqual(0f, EchoBodyRules.TagBlackoutCover(-1f, out _));
            Assert.IsFalse(EchoBodyRules.TagCompassShown(3.4f));
            Assert.IsTrue(EchoBodyRules.TagCompassShown(3.5f));
            Assert.IsTrue(EchoBodyRules.TagCompassShown(float.PositiveInfinity));                                       // 탈출자 · 난입자
            Assert.IsFalse(EchoBodyRules.HasTagBlackout(Escaped));
            Assert.IsFalse(EchoBodyRules.HasTagBlackout(Joiner));
        }

        [Test]
        public void Flight_StaysInsideTheBuildingUnderTheCeiling()
        {
            var inside = new UnityEngine.Vector3(10f, 2f, 20f);
            Assert.AreEqual(inside, EchoBodyRules.ClampFlight(inside));                                     // 안쪽: 그대로
            Assert.AreEqual(new UnityEngine.Vector3(0f, 9f, 62f), EchoBodyRules.ClampFlight(new UnityEngine.Vector3(-5f, 20f, 70f)));
            Assert.AreEqual(new UnityEngine.Vector3(64f, -3.5f, 0f), EchoBodyRules.ClampFlight(new UnityEngine.Vector3(80f, -10f, -1f)));
            Assert.AreEqual(new UnityEngine.Vector3(64f, 9f, 62f),
                EchoBodyRules.ClampFlight(new UnityEngine.Vector3(float.PositiveInfinity, float.PositiveInfinity, 1e9f)));
            var bad = EchoBodyRules.ClampFlight(new UnityEngine.Vector3(float.NaN, 30f, -9f));                // NaN: 원래 위치
            Assert.IsTrue(float.IsNaN(bad.x));
            Assert.AreEqual(30f, bad.y);
            Assert.AreEqual(-9f, bad.z);
        }

        [Test]
        public void KnockLocal_LockThenCountsAndResetsOnLeavingEcho()
        {
            var local = typeof(Marco.Network.Round.EchoKnockLocal);
            Marco.Network.Round.EchoKnockLocal.Observe(false, 0f);
            Marco.Network.Round.EchoKnockLocal.Observe(true, 100f);
            Assert.IsFalse(Marco.Network.Round.EchoKnockLocal.TryRequest(110f));   // locked
            Assert.IsTrue(Marco.Network.Round.EchoKnockLocal.TryRequest(120f));
            Assert.AreEqual(4, Marco.Network.Round.EchoKnockLocal.Remaining);
            Assert.IsFalse(Marco.Network.Round.EchoKnockLocal.TryRequest(130f));   // cooldown
            Marco.Network.Round.EchoKnockLocal.Observe(false, 140f);
            Assert.AreEqual(5, Marco.Network.Round.EchoKnockLocal.Remaining);
            Assert.IsFalse(Marco.Network.Round.EchoKnockLocal.TryRequest(200f));   // not an Echo
            Assert.IsNotNull(local);
        }
    }
}
