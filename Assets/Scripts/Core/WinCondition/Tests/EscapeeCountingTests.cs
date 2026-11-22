using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    // 탈출자 집계 (기획서 3.3-3 · 6.3 · 6.5-1, 11-10): 탈출 인원에는 포함, "생존 도망자"(최후 생존자 판정)에는 제외.
    // 탈출자가 메아리 몸이 되어도 Core 집계는 바뀌지 않는다는 것을 고정한다.
    public class EscapeeCountingTests
    {
        private sealed class Context
        {
            public RoundState Round;
            public FakePlayerRoster Roster;
            public ValveInteractionService Valves;
            public FakeRoundEventSink Events;
            public int EscapeRequired;
        }

        private static Context Create(int runnerCount, bool openGate = true)
        {
            var config = new WinConditionConfig();
            var roster = new FakePlayerRoster { TotalRunnerCount = runnerCount };
            for (int i = 1; i <= runnerCount; i++) roster.SetRole(i, PlayerRole.Runner);
            var board = new ValveBoard(new[] { ValveId.A }, config);
            var gate = new Gate(requiredOpenValveCount: 1);
            var events = new FakeRoundEventSink();
            var valves = new ValveInteractionService(board, gate, roster, new FakeValveSoundSink(), events);
            if (openGate) gate.Evaluate(1);
            return new Context
            {
                Round = new RoundState(config, roster, gate, valves, events, _ => 0),
                Roster = roster,
                Valves = valves,
                Events = events,
                EscapeRequired = config.GetRoundSettings(runnerCount).EscapeRequiredCount,
            };
        }

        // What NetworkRound does on escape: Core first, then the roster sees the Escaped status.
        private static void Escape(Context ctx, int playerId)
        {
            Assert.IsTrue(ctx.Round.NotifyRunnerEscaped(playerId));
            ctx.Roster.SetAlive(playerId, false);
            ctx.Roster.SetEscaped(playerId, true);
        }

        private static void Tag(Context ctx, int playerId)
        {
            Assert.IsTrue(ctx.Round.NotifyRunnerTagged(playerId));
            ctx.Roster.SetAlive(playerId, false);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Escapee_CountsTowardEscaped(int runnerCount)
        {
            var ctx = Create(runnerCount);
            Escape(ctx, 1);
            ctx.Round.Tick(0.01f);

            Assert.AreEqual(1, ctx.Round.EscapedCount);
            Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);
        }

        [Test]
        public void TwoRunners_OneEscapes_TheOtherEntersLastSurvivorPhase()
        {
            var ctx = Create(2);
            Escape(ctx, 1);
            ctx.Round.Tick(0.01f);

            Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);
            Assert.IsTrue(ctx.Round.LastSurvivorPhase.HasEntered);
            Assert.IsTrue(ctx.Round.LastSurvivorPhase.IsActive);
            Assert.AreEqual(1, ctx.Events.LastSurvivorPhaseEnteredCallCount);
            // The gate was open (that is how runner 1 got out), so the last one uses the exits, not a drain.
            Assert.IsNull(ctx.Round.LastSurvivorPhase.ActiveDrainHatchId);
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Escapees_AreNotSurvivors_PhaseWaitsForOneUndecidedRunner(int runnerCount)
        {
            var ctx = Create(runnerCount);
            // Everyone but the last two leaves by the exit: two are still undecided, so no phase yet.
            for (int id = 1; id <= runnerCount - 2; id++) Escape(ctx, id);
            ctx.Round.Tick(0.01f);
            Assert.IsFalse(ctx.Round.LastSurvivorPhase.HasEntered);

            // One more escapes: exactly one undecided Runner remains.
            Escape(ctx, runnerCount - 1);
            ctx.Round.Tick(0.01f);
            Assert.IsTrue(ctx.Round.LastSurvivorPhase.HasEntered);
            Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);
        }

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void EscapeAndTags_LeaveOne_EntersLastSurvivorPhase(int runnerCount)
        {
            var ctx = Create(runnerCount);
            Escape(ctx, 1);
            for (int id = 2; id <= runnerCount - 1; id++) Tag(ctx, id);
            ctx.Round.Tick(0.01f);

            Assert.IsTrue(ctx.Round.LastSurvivorPhase.HasEntered);
            Assert.AreEqual(1, ctx.Round.EscapedCount);
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void LastRunnerEscapes_EndsRoundAtOnce_RunnersWin(int runnerCount)
        {
            var ctx = Create(runnerCount);
            for (int id = 1; id < runnerCount; id++) Tag(ctx, id);
            ctx.Round.Tick(0.01f);
            Assert.IsTrue(ctx.Round.LastSurvivorPhase.HasEntered);

            Escape(ctx, runnerCount);
            ctx.Round.Tick(0.01f);

            // One escape is below the requirement from 3 Runners on, but the last survivor out wins (6.3).
            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
            Assert.AreEqual(1, ctx.Events.RoundEndedCallCount);
        }

        [Test]
        public void AllRunnersEscape_EndsRound_RunnersWin()
        {
            var ctx = Create(3);
            Escape(ctx, 1);
            Escape(ctx, 2);
            ctx.Round.Tick(0.01f);
            Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);

            Escape(ctx, 3);
            ctx.Round.Tick(0.01f);
            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
            Assert.AreEqual(3, ctx.Round.EscapedCount);
        }

        [Test]
        public void Escapee_CannotEscapeAgain_OrBeTagged()
        {
            var ctx = Create(3);
            Escape(ctx, 1);
            ctx.Round.Tick(0.01f);

            Assert.IsFalse(ctx.Round.NotifyRunnerEscaped(1));
            Assert.IsFalse(ctx.Round.NotifyRunnerTagged(1));
            ctx.Round.Tick(0.01f);
            Assert.AreEqual(1, ctx.Round.EscapedCount);
            Assert.IsFalse(ctx.Round.LastSurvivorPhase.HasEntered);
        }

        [Test]
        public void EscapeeLeaving_IsNotCountedAsTag()
        {
            // 2 of 3 escape (requirement met), the escapee then disconnects: still one undecided Runner,
            // and tagging that one still ends with the escapes counted.
            var ctx = Create(3);
            Escape(ctx, 1);
            Escape(ctx, 2);
            ctx.Round.Tick(0.01f);
            ctx.Round.NotifyPlayerLeft(1);
            ctx.Round.Tick(0.01f);
            Assert.AreEqual(RoundResult.InProgress, ctx.Round.Result);
            Assert.IsTrue(ctx.Round.LastSurvivorPhase.HasEntered);

            Tag(ctx, 3);
            ctx.Round.Tick(0.01f);
            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
            Assert.AreEqual(2, ctx.Round.EscapedCount);
        }

        [Test]
        public void Escapee_CannotWorkValvesOrDrain()
        {
            // The services only ask the roster, so an Escaped status (not alive) is enough to be refused.
            var ctx = Create(2, openGate: false);
            Assert.IsTrue(ctx.Valves.TryBeginWork(1, ValveId.A));
            ctx.Valves.EndWork(1, ValveId.A);

            ctx.Roster.SetAlive(1, false);
            ctx.Roster.SetEscaped(1, true);
            Assert.IsFalse(ctx.Valves.TryBeginWork(1, ValveId.A));

            // Tag the other one with the gate still closed so a drain hatch activates, then try it.
            Tag(ctx, 2);
            ctx.Round.Tick(0.01f);
            var phase = ctx.Round.LastSurvivorPhase;
            Assert.IsNotNull(phase.ActiveDrainHatchId);
            var drains = new DrainHatchInteractionService(phase, ctx.Roster);
            Assert.IsFalse(drains.TryBeginWork(1, phase.ActiveDrainHatchId.Value));
        }

        [TestCase(3)]
        [TestCase(5)]
        public void TimeLimit_EscapeesCount_RemainingAreUnescaped(int runnerCount)
        {
            var ctx = Create(runnerCount);
            for (int id = 1; id <= ctx.EscapeRequired; id++) Escape(ctx, id);
            ctx.Round.Tick(0.01f);
            ctx.Round.Tick(10000f);

            Assert.AreEqual(RoundResult.RunnersWin, ctx.Round.Result);
            Assert.AreEqual(ctx.EscapeRequired, ctx.Round.EscapedCount);
        }
    }
}
