using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    public class RoundStateTests
    {
        private const int SeekerId = 9001;
        private const int EchoId = 9002;

        private sealed class RoundTestContext
        {
            public RoundState Round;
            public FakePlayerRoster Roster;
            public Gate Gate;
            public ValveBoard Board;
            public ValveInteractionService Service;
            public FakeRoundEventSink Events;
            public int EscapeRequired;
            public float TimeLimit;
        }

        private static RoundTestContext CreateRound(int runnerCount, bool openGate = true)
        {
            var config = new WinConditionConfig();
            var roster = new FakePlayerRoster { TotalRunnerCount = runnerCount };

            for (int i = 1; i <= runnerCount; i++)
            {
                roster.SetRole(i, PlayerRole.Runner);
            }

            roster.SetRole(SeekerId, PlayerRole.Seeker);
            roster.SetRole(EchoId, PlayerRole.Echo);

            var board = new ValveBoard(new[] { ValveId.A }, config);
            var gate = new Gate(requiredOpenValveCount: 1);
            var soundSink = new FakeValveSoundSink();
            var events = new FakeRoundEventSink();
            var service = new ValveInteractionService(board, gate, roster, soundSink, events);

            if (openGate) gate.Evaluate(1);

            var round = new RoundState(config, roster, gate, service, events);
            var settings = config.GetRoundSettings(runnerCount);

            return new RoundTestContext
            {
                Round = round,
                Roster = roster,
                Gate = gate,
                Board = board,
                Service = service,
                Events = events,
                EscapeRequired = settings.EscapeRequiredCount,
                TimeLimit = settings.TimeLimitSeconds,
            };
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void EscapeRequiredCountMet_RunnersWin(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            for (int playerId = 1; playerId <= ctx.EscapeRequired; playerId++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerEscaped(playerId));
            }

            ctx.Round.Tick(0.01f);

            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
            Assert.AreEqual(1, ctx.Events.RoundEndedCallCount);
            Assert.AreEqual(RoundResult.RunnersWin, ctx.Events.LastRoundResult);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void OneShortOfRequirement_RestAllTagged_SeekersWin(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);
            int playerId = 1;

            for (int i = 0; i < ctx.EscapeRequired - 1; i++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerEscaped(playerId));
                playerId++;
            }

            for (; playerId <= runnerCount; playerId++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerTagged(playerId));
            }

            ctx.Round.Tick(0.01f);

            Assert.AreEqual(RoundResult.SeekersWin, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void TimeLimitExpires_WithNoEscapes_SeekersWin(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            ctx.Round.Tick(ctx.TimeLimit);

            Assert.AreEqual(RoundResult.SeekersWin, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void AllRunnersTagged_NoEscapes_SeekersWin(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            for (int playerId = 1; playerId <= runnerCount; playerId++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerTagged(playerId));
            }

            ctx.Round.Tick(0.01f);

            Assert.AreEqual(RoundResult.SeekersWin, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void EscapeAndTagInSameTick_EscapeWinsFirst(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);
            int playerId = 1;

            // 요구 인원에서 하나 모자른 상태를 미리 만들어둔다 (각각 별도 Tick으로 적용)
            for (int i = 0; i < ctx.EscapeRequired - 1; i++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerEscaped(playerId));
                playerId++;
                ctx.Round.Tick(0.01f);
                Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);
            }

            int escapingPlayer = playerId;
            int taggedPlayer = playerId + 1;

            // 호출 순서를 일부러 태그 먼저로 뒤집어도 "탈출 먼저" 처리 순서 때문에 결과는 바뀌지 않아야 함
            Assert.IsTrue(ctx.Round.NotifyRunnerTagged(taggedPlayer));
            Assert.IsTrue(ctx.Round.NotifyRunnerEscaped(escapingPlayer));

            ctx.Round.Tick(0.01f);

            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void TimeUpAndEscapeInSameTick_EscapeWinsFirst(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);
            int playerId = 1;

            for (int i = 0; i < ctx.EscapeRequired - 1; i++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerEscaped(playerId));
                playerId++;
                ctx.Round.Tick(0.01f);
            }

            Assert.IsTrue(ctx.Round.NotifyRunnerEscaped(playerId)); // 마지막 한 명, 아직 Tick 전이라 미적용 상태

            ctx.Round.Tick(ctx.TimeLimit); // 이 틱에서 제한시간도 끝나지만 탈출이 먼저 처리되어야 함

            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void GateClosed_EscapeRequest_IsRejected(int runnerCount)
        {
            var ctx = CreateRound(runnerCount, openGate: false);

            Assert.IsFalse(ctx.Round.NotifyRunnerEscaped(1));
            Assert.AreEqual(0, ctx.Round.EscapedCount);
            Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void SeekerAndEchoEscapeRequests_AreRejected(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            Assert.IsFalse(ctx.Round.NotifyRunnerEscaped(SeekerId));
            Assert.IsFalse(ctx.Round.NotifyRunnerEscaped(EchoId));
            Assert.AreEqual(0, ctx.Round.EscapedCount);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void ResultFinalized_FurtherInputIsIgnored(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            for (int playerId = 1; playerId <= ctx.EscapeRequired; playerId++)
            {
                ctx.Round.NotifyRunnerEscaped(playerId);
            }

            ctx.Round.Tick(0.01f);

            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
            Assert.AreEqual(1, ctx.Events.RoundEndedCallCount);

            Assert.IsFalse(ctx.Round.NotifyRunnerEscaped(runnerCount + 500));
            Assert.IsFalse(ctx.Round.NotifyRunnerTagged(1));
            ctx.Round.NotifyPlayerLeft(1);
            ctx.Round.Tick(100000f);

            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
            Assert.AreEqual(1, ctx.Events.RoundEndedCallCount); // 추가로 호출되지 않아야 함
        }

        // v0.6: 술래가 라운드 중 나가면 판 자체가 무효(Void)로 즉시 끝난다.
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void SeekerLeaves_VoidsRoundImmediately(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            ctx.Round.NotifyPlayerLeft(SeekerId); // Tick 없이도 즉시 확정되어야 함

            Assert.AreEqual(RoundResult.Void, ctx.Round.Result);
            Assert.AreEqual(1, ctx.Events.RoundEndedCallCount);
            Assert.AreEqual(RoundResult.Void, ctx.Events.LastRoundResult);
        }

        // v0.6: Echo가 나가는 건 특별 처리 없음(무시).
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void EchoLeaves_IsIgnored(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            ctx.Round.NotifyPlayerLeft(EchoId);

            Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);
        }

        // v0.6: 도망자가 나가면 태그당한 것과 완전히 동일하게 처리된다(메아리 전환으로 간주).
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void RunnerLeaves_IsTreatedExactlyLikeTag_AllLeaveWithNoEscapes_SeekersWin(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            for (int playerId = 1; playerId <= runnerCount; playerId++)
            {
                ctx.Round.NotifyPlayerLeft(playerId);
            }

            ctx.Round.Tick(0.01f); // 태그와 마찬가지로 Tick에서 처리됨

            Assert.AreEqual(RoundResult.SeekersWin, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void RunnerLeaves_IsRemovedFromValveWork(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            Assert.IsTrue(ctx.Service.TryBeginWork(1, ValveId.A));
            Assert.AreEqual(1, ctx.Board.GetValve(ValveId.A).WorkerCount);

            ctx.Round.NotifyPlayerLeft(1);

            Assert.AreEqual(0, ctx.Board.GetValve(ValveId.A).WorkerCount);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void TaggedRunner_IsRemovedFromValveWork(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            Assert.IsTrue(ctx.Service.TryBeginWork(1, ValveId.A));
            Assert.AreEqual(1, ctx.Board.GetValve(ValveId.A).WorkerCount);

            Assert.IsTrue(ctx.Round.NotifyRunnerTagged(1));

            Assert.AreEqual(0, ctx.Board.GetValve(ValveId.A).WorkerCount);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void LastSurvivorPhase_EntersExactlyWhenOneRunnerRemainsUndecided(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            for (int playerId = 1; playerId <= runnerCount - 1; playerId++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerTagged(playerId));
            }

            ctx.Round.Tick(0.01f);

            Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);
            Assert.IsTrue(ctx.Round.LastSurvivorPhase.HasEntered);
            Assert.IsTrue(ctx.Round.LastSurvivorPhase.IsActive);
            Assert.AreEqual(1, ctx.Events.LastSurvivorPhaseEnteredCallCount);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void LastSurvivor_EscapesViaDrainHatch_RunnersWin(int runnerCount)
        {
            var ctx = CreateRound(runnerCount, openGate: false); // 게이트가 안 열려있어야 배수구가 활성화됨
            int survivorId = runnerCount;

            for (int playerId = 1; playerId <= runnerCount - 1; playerId++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerTagged(playerId));
            }

            ctx.Round.Tick(0.01f); // 페이즈 진입, 배수구 활성화

            Assert.IsNotNull(ctx.Round.LastSurvivorPhase.ActiveDrainHatchId);
            var drainHatchId = ctx.Round.LastSurvivorPhase.ActiveDrainHatchId.Value;

            var drainHatchService = new DrainHatchInteractionService(ctx.Round.LastSurvivorPhase, ctx.Roster);
            Assert.IsTrue(drainHatchService.TryBeginWork(survivorId, drainHatchId));

            ctx.Round.Tick(11f); // 개방 밸브 0개 -> T=11초로 배수구 작업 완료

            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void LastSurvivor_EscapesNormallyWhileGateAlreadyOpen_RunnersWin(int runnerCount)
        {
            var ctx = CreateRound(runnerCount); // 게이트 열려있음 (기본값)
            int survivorId = runnerCount;

            for (int playerId = 1; playerId <= runnerCount - 1; playerId++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerTagged(playerId));
            }

            ctx.Round.Tick(0.01f); // 페이즈 진입, 게이트가 열려있으니 배수구는 비활성

            Assert.IsNull(ctx.Round.LastSurvivorPhase.ActiveDrainHatchId);

            Assert.IsTrue(ctx.Round.NotifyRunnerEscaped(survivorId)); // 게이트가 열려있으니 일반 탈출 가능
            ctx.Round.Tick(0.01f);

            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void LastSurvivorPhase_TimesOutAt90Seconds_SeekersWin(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            for (int playerId = 1; playerId <= runnerCount - 1; playerId++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerTagged(playerId));
            }

            ctx.Round.Tick(0.01f); // 라운드 시작 직후라 라운드 잔여가 90초보다 훨씨 크므로 90초가 그대로 적용됨

            ctx.Round.Tick(91f); // 90초 초과

            Assert.AreEqual(RoundResult.SeekersWin, ctx.Round.Result);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void LastSurvivorPhase_CapsAtRoundRemaining_WhenShorterThan90(int runnerCount)
        {
            var ctx = CreateRound(runnerCount);

            ctx.Round.Tick(ctx.TimeLimit - 60f); // 라운드 잔여를 60초로 만들어둠
            Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);

            for (int playerId = 1; playerId <= runnerCount - 1; playerId++)
            {
                Assert.IsTrue(ctx.Round.NotifyRunnerTagged(playerId));
            }

            ctx.Round.Tick(70f); // 이 틱 안에서 진입(잔여 60초로 캡)과 60초 초과가 함께 일어남

            Assert.AreEqual(RoundResult.SeekersWin, ctx.Round.Result);
        }
    }
}
