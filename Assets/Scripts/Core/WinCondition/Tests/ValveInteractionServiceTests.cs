using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    public class ValveInteractionServiceTests
    {
        private const float Tolerance = 0.001f;

        private static (
            ValveInteractionService Service,
            ValveBoard Board,
            FakePlayerRoster Roster,
            FakeValveSoundSink Sound,
            FakeRoundEventSink Events) CreateService(params ValveId[] activeValveIds)
        {
            var config = new WinConditionConfig();
            var board = new ValveBoard(activeValveIds, config);
            var gate = new Gate(requiredOpenValveCount: 1);
            var roster = new FakePlayerRoster();
            var sound = new FakeValveSoundSink();
            var events = new FakeRoundEventSink();
            var service = new ValveInteractionService(board, gate, roster, sound, events);

            return (service, board, roster, sound, events);
        }

        [Test]
        public void EchoAndSeekerRequests_AreRejected()
        {
            var (service, board, roster, _, _) = CreateService(ValveId.A);
            roster.SetRole(10, PlayerRole.Echo);
            roster.SetRole(20, PlayerRole.Seeker);

            Assert.IsFalse(service.TryBeginWork(10, ValveId.A));
            Assert.IsFalse(service.TryBeginWork(20, ValveId.A));
            Assert.AreEqual(0, board.GetValve(ValveId.A).WorkerCount);
        }

        [Test]
        public void DeadRunnerRequest_IsRejected()
        {
            var (service, board, roster, _, _) = CreateService(ValveId.A);
            roster.SetRole(30, PlayerRole.Runner);
            roster.SetAlive(30, false);

            Assert.IsFalse(service.TryBeginWork(30, ValveId.A));
            Assert.AreEqual(0, board.GetValve(ValveId.A).WorkerCount);
        }

        [Test]
        public void InactiveValveId_IsRejected()
        {
            // 보드에는 A만 활성화되어 있고 B는 없음
            var (service, _, roster, _, _) = CreateService(ValveId.A);
            roster.SetRole(1, PlayerRole.Runner);

            Assert.IsFalse(service.TryBeginWork(1, ValveId.B));
        }

        [Test]
        public void WorkerLeaving_TriggersDecay()
        {
            var (service, board, roster, _, _) = CreateService(ValveId.A);
            roster.SetRole(1, PlayerRole.Runner);

            Assert.IsTrue(service.TryBeginWork(1, ValveId.A));
            board.Tick(4f); // progress 0.5

            service.RemovePlayerFromAllValves(1); // 탈락/이탈 상황 흉내
            board.Tick(3f); // 유예 3초 소비, 진행도는 그대로 0.5
            board.Tick(1f); // 유예 끝난 뒤 1초 감쇠

            Assert.AreEqual(0.4f, board.GetValve(ValveId.A).Progress, Tolerance);
        }

        [Test]
        public void RotatingSound_FiresOnlyOncePerStartAndStop()
        {
            var (service, _, roster, sound, _) = CreateService(ValveId.A);
            roster.SetRole(1, PlayerRole.Runner);
            roster.SetRole(2, PlayerRole.Runner);

            service.TryBeginWork(1, ValveId.A); // 0 -> 1, 시작 알림
            service.TryBeginWork(2, ValveId.A); // 1 -> 2, 알림 없음
            service.EndWork(1, ValveId.A); // 2 -> 1, 알림 없음
            service.EndWork(2, ValveId.A); // 1 -> 0, 정지 알림

            Assert.AreEqual(2, sound.Notifications.Count);
            Assert.AreEqual((ValveId.A, ValveSoundEvent.RotatingStarted), sound.Notifications[0]);
            Assert.AreEqual((ValveId.A, ValveSoundEvent.RotatingStopped), sound.Notifications[1]);
        }

        // 버그 수정: 열린 밸브 앞에서 E를 계속 누르고 있어도(EndWork 없음) 역류가 일어나서 닫혀야 한다.
        [Test]
        public void HoldingWorkThroughOpen_ValveStillClosesAfter210Seconds()
        {
            var (service, board, roster, _, _) = CreateService(ValveId.A);
            roster.SetRole(1, PlayerRole.Runner);
            var valve = board.GetValve(ValveId.A);

            service.TryBeginWork(1, ValveId.A); // 누르기만 하고 끝까지 놓지 않음

            for (int i = 0; i < 8; i++) board.Tick(1f);
            Assert.AreEqual(ValveStatus.Open, valve.Status);

            for (int i = 0; i < 210; i++) board.Tick(1f); // 유예 180 + 역류 30

            Assert.AreEqual(ValveStatus.Closed, valve.Status);
            Assert.AreEqual(0f, valve.Progress, Tolerance);
        }

        [Test]
        public void OpenValve_TryBeginWork_IsRejectedWithoutSound()
        {
            var (service, board, roster, sound, _) = CreateService(ValveId.A);
            roster.SetRole(1, PlayerRole.Runner);
            roster.SetRole(2, PlayerRole.Runner);

            service.TryBeginWork(1, ValveId.A);
            board.Tick(8f); // Open
            int soundCountAfterOpen = sound.Notifications.Count;

            Assert.IsFalse(service.TryBeginWork(2, ValveId.A));
            Assert.AreEqual(0, board.GetValve(ValveId.A).WorkerCount);
            Assert.AreEqual(soundCountAfterOpen, sound.Notifications.Count);

            board.Tick(181f); // 역류가 시작되면 다시 잡을 수 있다
            Assert.IsTrue(service.TryBeginWork(2, ValveId.A));
        }

        [Test]
        public void ValveOpening_NotifiesRotatingStoppedOnce()
        {
            var (service, board, roster, sound, _) = CreateService(ValveId.A);
            roster.SetRole(1, PlayerRole.Runner);

            service.TryBeginWork(1, ValveId.A);
            board.Tick(8f); // Open -> 작업자 목록이 비워지면서 정지 알림
            service.EndWork(1, ValveId.A); // 이미 빠져 있으니 추가 알림 없음

            Assert.AreEqual(2, sound.Notifications.Count);
            Assert.AreEqual((ValveId.A, ValveSoundEvent.RotatingStarted), sound.Notifications[0]);
            Assert.AreEqual((ValveId.A, ValveSoundEvent.RotatingStopped), sound.Notifications[1]);
        }

        [Test]
        public void ValveStatusAndProgress_AreForwardedToRoundEventSink()
        {
            var (service, board, roster, _, events) = CreateService(ValveId.A);
            roster.SetRole(1, PlayerRole.Runner);

            service.TryBeginWork(1, ValveId.A);
            board.Tick(1f); // Closed -> Rotating
            board.Tick(7f); // Rotating -> Open (총 8초)

            CollectionAssert.Contains(events.StatusChanges, (ValveId.A, ValveStatus.Rotating));
            CollectionAssert.Contains(events.StatusChanges, (ValveId.A, ValveStatus.Open));
            Assert.AreEqual(1f, events.ProgressChanges[events.ProgressChanges.Count - 1].Progress, Tolerance);
        }

        [Test]
        public void ReflowRipple_IsForwardedToSoundSink()
        {
            var (service, board, roster, sound, _) = CreateService(ValveId.A);
            roster.SetRole(1, PlayerRole.Runner);

            service.TryBeginWork(1, ValveId.A);
            board.Tick(8f); // Open
            service.EndWork(1, ValveId.A);
            board.Tick(210f); // 유예 180 + 역류 30 전부 통과 -> Closed, 물소리 3회

            int rippleCount = 0;
            foreach (var notification in sound.Notifications)
            {
                if (notification.SoundEvent == ValveSoundEvent.ReflowRipple) rippleCount++;
            }

            Assert.AreEqual(3, rippleCount);
        }

        [Test]
        public void GateOpened_IsForwardedToRoundEventSink()
        {
            var (service, board, roster, _, events) = CreateService(ValveId.A); // Gate 요구 개방 수 1
            roster.SetRole(1, PlayerRole.Runner);

            Assert.IsFalse(events.GateOpenedCalled);

            service.TryBeginWork(1, ValveId.A);
            board.Tick(8f); // Open -> 요구 개방 수(1) 충족 -> 밸브 상태 변화 시 자동으로 게이트 재평가됨

            Assert.IsTrue(events.GateOpenedCalled);
        }
    }
}
