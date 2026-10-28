using Marco.Core.WinCondition;
using NUnit.Framework;

namespace Marco.Core.WinCondition.Tests
{
    public class ValveBoardGateTests
    {
        private static void Open(ValveBoard board, ValveId id)
        {
            board.GetValve(id).BeginWork(1);
            board.Tick(100f); // 여유있게 큰 dt로 한 번에 Open
            board.GetValve(id).EndWork(1);
        }

        [Test]
        public void ValveBoard_AcceptsManuallyChosenValveIdList()
        {
            var config = new WinConditionConfig();
            var board = new ValveBoard(new[] { ValveId.B, ValveId.D }, config);

            Assert.AreEqual(2, board.Valves.Count);
            Assert.IsTrue(board.Valves.ContainsKey(ValveId.B));
            Assert.IsTrue(board.Valves.ContainsKey(ValveId.D));
            Assert.IsFalse(board.Valves.ContainsKey(ValveId.A));
        }

        [Test]
        public void Gate_DoesNotOpen_WhenRequiredCountWasNeverOpenAtTheSameTime()
        {
            // A를 열었다가 역류로 완전히 닫은 뒤에 B, C를 열어도
            // 세 밸브가 "동시에" 열려있던 적은 없으므로 (요구 개방 수 3) 게이트는 열리지 않아야 함.
            var config = new WinConditionConfig();
            var board = new ValveBoard(new[] { ValveId.A, ValveId.B, ValveId.C }, config);
            var gate = new Gate(requiredOpenValveCount: 3);

            Open(board, ValveId.A);
            gate.Evaluate(board.OpenValveCount);

            board.Tick(270f); // A 완전히 역류 닫힘 (유예 240 + 역류 30)
            gate.Evaluate(board.OpenValveCount);
            Assert.AreEqual(ValveStatus.Closed, board.GetValve(ValveId.A).Status);

            Open(board, ValveId.B);
            gate.Evaluate(board.OpenValveCount);

            Open(board, ValveId.C);
            gate.Evaluate(board.OpenValveCount);

            Assert.AreEqual(2, board.OpenValveCount); // B, C만 Open (A는 Closed)
            Assert.IsFalse(gate.IsOpen);
        }

        [Test]
        public void Gate_StaysOpenAfterAllValvesCloseAgain()
        {
            var config = new WinConditionConfig();
            var board = new ValveBoard(new[] { ValveId.A, ValveId.B, ValveId.C }, config);
            var gate = new Gate(requiredOpenValveCount: 3);

            board.GetValve(ValveId.A).BeginWork(1);
            board.GetValve(ValveId.B).BeginWork(1);
            board.GetValve(ValveId.C).BeginWork(1);
            board.Tick(100f); // 세 밸브가 동시에 Open

            gate.Evaluate(board.OpenValveCount);
            Assert.IsTrue(gate.IsOpen);

            board.GetValve(ValveId.A).EndWork(1);
            board.GetValve(ValveId.B).EndWork(1);
            board.GetValve(ValveId.C).EndWork(1);
            board.Tick(270f); // 셋 다 역류로 완전히 닫힘

            gate.Evaluate(board.OpenValveCount);

            Assert.AreEqual(0, board.OpenValveCount);
            Assert.IsTrue(gate.IsOpen); // latch: 닫혀도 게이트는 계속 열려있음
        }

        [Test]
        public void Gate_UsesOpenCountAtTheMomentReflowCloseAndValveOpenCoincideInSameTick()
        {
            var config = new WinConditionConfig();
            var board = new ValveBoard(new[] { ValveId.A, ValveId.B, ValveId.C }, config);
            var gate = new Gate(requiredOpenValveCount: 3);
            var valveA = board.GetValve(ValveId.A);
            var valveB = board.GetValve(ValveId.B);
            var valveC = board.GetValve(ValveId.C);

            // A: 미리 열어서 역류가 끝나기 2초 전까지 진행시켜둔다 (board.Tick이 아니라 valve 단위로 미리 진행)
            valveA.BeginWork(1);
            valveA.Tick(8f); // Open, 열린 지 0초
            valveA.EndWork(1);
            valveA.Tick(268f); // 유예 240 + 역류 28초 = 역류 종료까지 2초 남음
            Assert.AreEqual(ValveStatus.Reflowing, valveA.Status);

            // B: 이미 열려서 유지 중 (역류까지는 한참 남음)
            valveB.BeginWork(1);
            valveB.Tick(8f);
            valveB.EndWork(1);

            // C: 작업 중, 2초만 더 일하면 딱 완료되도록 진행도를 맞춰둔다
            valveC.BeginWork(1);
            valveC.Tick(6f); // 0.125 * 6 = 0.75, 남은 0.25는 2초치 작업량

            gate.Evaluate(board.OpenValveCount); // 지금은 B만 Open → 1
            Assert.AreEqual(1, board.OpenValveCount);
            Assert.IsFalse(gate.IsOpen);

            // 같은 틱에서 A는 역류 완료(Closed)로, C는 작업 완료(Open)로 동시에 전환됨
            board.Tick(2f);
            gate.Evaluate(board.OpenValveCount);

            Assert.AreEqual(ValveStatus.Closed, valveA.Status);
            Assert.AreEqual(ValveStatus.Open, valveB.Status);
            Assert.AreEqual(ValveStatus.Open, valveC.Status);
            Assert.AreEqual(2, board.OpenValveCount); // A가 빠지고 C가 들어와서 B+C = 2
            Assert.IsFalse(gate.IsOpen); // 요구 3에 못 미치므로, 그 시점의 정확한 개방 수(2)로 판정되어 열리지 않음
        }
    }
}
