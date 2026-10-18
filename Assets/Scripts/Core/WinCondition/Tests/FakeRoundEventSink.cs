using System.Collections.Generic;
using Marco.Core.WinCondition;

namespace Marco.Core.WinCondition.Tests
{
    // IRoundEventSink 가짜 구현. 호출된 내용을 그대로 기록만 한다.
    public sealed class FakeRoundEventSink : IRoundEventSink
    {
        public readonly List<(ValveId ValveId, ValveStatus Status)> StatusChanges =
            new List<(ValveId ValveId, ValveStatus Status)>();

        public readonly List<(ValveId ValveId, float Progress)> ProgressChanges =
            new List<(ValveId ValveId, float Progress)>();

        public bool GateOpenedCalled { get; private set; }
        public int RoundEndedCallCount { get; private set; }
        public RoundResult LastRoundResult { get; private set; } = RoundResult.InProgress;

        public void OnValveStatusChanged(ValveId valveId, ValveStatus status)
        {
            StatusChanges.Add((valveId, status));
        }

        public void OnValveProgressChanged(ValveId valveId, float progress)
        {
            ProgressChanges.Add((valveId, progress));
        }

        public void OnGateOpened()
        {
            GateOpenedCalled = true;
        }

        public void OnRoundEnded(RoundResult result)
        {
            RoundEndedCallCount++;
            LastRoundResult = result;
        }

        public int LastSurvivorPhaseEnteredCallCount { get; private set; }
        public DrainHatchId? ActivatedDrainHatchId { get; private set; }
        public int DrainHatchActivatedCallCount { get; private set; }

        public void OnLastSurvivorPhaseEntered()
        {
            LastSurvivorPhaseEnteredCallCount++;
        }

        public void OnDrainHatchActivated(DrainHatchId drainHatchId)
        {
            DrainHatchActivatedCallCount++;
            ActivatedDrainHatchId = drainHatchId;
        }
    }
}
