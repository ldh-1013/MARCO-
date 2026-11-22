using System;
using System.Collections.Generic;

namespace Marco.Core.WinCondition
{
    // 라운드 진행(제한시간, 탈출/태그 집계)과 승패 판정 + 최후 생존자 페이즈 진입.
    public sealed class RoundState
    {
        private readonly WinConditionConfig _config;
        private readonly IPlayerRoster _playerRoster;
        private readonly Gate _gate;
        private readonly ValveInteractionService _interactionService;
        private readonly IRoundEventSink _roundEventSink;
        private readonly Func<int, int> _drainHatchRandomNextIndex;
        private readonly LastSurvivorPhase _lastSurvivorPhase;

        private readonly int _totalRunnerCount;
        private readonly float _timeLimitSeconds;
        private float _elapsedSeconds;

        private readonly int _escapeRequiredCount;
        private int _escapedCount;
        private int _taggedCount;

        // 같은 playerId가 탈출/태그/이탈로 중복 처리되지 않게 막는 용도 (판정용 카운트와는 별개)
        private readonly HashSet<int> _resolvedPlayerIds = new HashSet<int>();

        // 이번 Tick에서 적용을 기다리는 탈출/태그. Tick에서 "탈출 → 태그 → 시간 종료" 순서로 처리됨.
        private readonly List<int> _pendingEscapedPlayerIds = new List<int>();
        private readonly List<int> _pendingTaggedPlayerIds = new List<int>();

        public RoundResult Result { get; private set; } = RoundResult.InProgress;
        public int EscapedCount => _escapedCount;
        public int EscapeRequiredCount => _escapeRequiredCount;
        public float RemainingSeconds => _timeLimitSeconds - _elapsedSeconds > 0f ? _timeLimitSeconds - _elapsedSeconds : 0f;
        public LastSurvivorPhase LastSurvivorPhase => _lastSurvivorPhase;

        // 종반 압박(6.2-1). 라운드 시계가 흐를 때마다 잔여 시간으로 갱신된다.
        public SuddenDeath SuddenDeath { get; }

        public RoundState(
            WinConditionConfig config,
            IPlayerRoster playerRoster,
            Gate gate,
            ValveInteractionService interactionService,
            IRoundEventSink roundEventSink,
            Func<int, int> drainHatchRandomNextIndex = null)
        {
            _config = config;
            _playerRoster = playerRoster;
            _gate = gate;
            _interactionService = interactionService;
            _roundEventSink = roundEventSink;
            _drainHatchRandomNextIndex = drainHatchRandomNextIndex ?? new Random().Next;

            _totalRunnerCount = playerRoster.TotalRunnerCount;
            var settings = config.GetRoundSettings(_totalRunnerCount);
            _escapeRequiredCount = settings.EscapeRequiredCount;
            _timeLimitSeconds = settings.TimeLimitSeconds;

            _lastSurvivorPhase = new LastSurvivorPhase(config);
            SuddenDeath = new SuddenDeath(config);
            WireDrainHatch(_lastSurvivorPhase.GetDrainHatch(DrainHatchId.First));
            WireDrainHatch(_lastSurvivorPhase.GetDrainHatch(DrainHatchId.Second));
        }

        private void WireDrainHatch(DrainHatch hatch)
        {
            hatch.StatusChanged += status =>
            {
                if (status == DrainHatchStatus.Completed)
                {
                    HandleDrainHatchCompleted(hatch);
                }
            };
        }

        // 게이트가 열려있을 때만 탈출을 인정한다(최후 생존자 페이즈의 배수구 탈출은 별도 경로, 아래 HandleDrainHatchCompleted 참고).
        // 역할은 인자로 받지 않고 IPlayerRoster에서 직접 확인.
        public bool NotifyRunnerEscaped(int playerId)
        {
            if (Result != RoundResult.InProgress) return false;
            if (_resolvedPlayerIds.Contains(playerId)) return false;
            if (_playerRoster.GetRole(playerId) != PlayerRole.Runner) return false;
            if (!_gate.IsOpen) return false;

            _resolvedPlayerIds.Add(playerId);
            _pendingEscapedPlayerIds.Add(playerId);
            _interactionService.RemovePlayerFromAllValves(playerId);
            return true;
        }

        public bool NotifyRunnerTagged(int playerId)
        {
            if (Result != RoundResult.InProgress) return false;
            if (_resolvedPlayerIds.Contains(playerId)) return false;
            if (_playerRoster.GetRole(playerId) != PlayerRole.Runner) return false;

            EnqueueTag(playerId);
            _interactionService.RemovePlayerFromAllValves(playerId);
            return true;
        }

        // 탈락/연결 끊김.
        // v0.6: 술래가 나가면 판 자체가 무효(Void)로 즉시 끝난다. 도망자가 나가면 태그당한 것과 완전히 동일하게
        // 처리한다(메아리로 전환된 것으로 간주 — 탈출/태그 집계와 최후 생존자 페이즈 진입 판정에도 똑같이 들어감).
        public void NotifyPlayerLeft(int playerId)
        {
            if (Result != RoundResult.InProgress) return;

            _interactionService.RemovePlayerFromAllValves(playerId);

            var role = _playerRoster.GetRole(playerId);

            if (role == PlayerRole.Seeker)
            {
                Finalize(RoundResult.Void);
                return;
            }

            if (role != PlayerRole.Runner) return; // Echo는 특별 처리 없음

            EnqueueTag(playerId);
        }

        // NotifyRunnerTagged와 NotifyPlayerLeft(도망자)가 공유하는 "태그 대기열에 넣기" 로직.
        // 이미 탈출/태그/이탈로 끝난 사람이면 조용히 무시한다.
        private void EnqueueTag(int playerId)
        {
            if (_resolvedPlayerIds.Contains(playerId)) return;

            _resolvedPlayerIds.Add(playerId);
            _pendingTaggedPlayerIds.Add(playerId);
        }

        // 같은 틱에 여러 일이 겹치면 탈출 → 태그 → (최후 생존자 페이즈 진입/진행) → 시간 종료 순서로 처리한다.
        public void Tick(float dt)
        {
            if (Result != RoundResult.InProgress) return;
            if (dt <= 0f) return;

            foreach (var playerId in _pendingEscapedPlayerIds)
            {
                ApplyEscape(playerId);
                if (Result != RoundResult.InProgress) break;
            }

            _pendingEscapedPlayerIds.Clear();

            if (Result != RoundResult.InProgress)
            {
                _pendingTaggedPlayerIds.Clear();
                return;
            }

            foreach (var playerId in _pendingTaggedPlayerIds)
            {
                ApplyTag(playerId);
                if (Result != RoundResult.InProgress) break;
            }

            _pendingTaggedPlayerIds.Clear();

            if (Result != RoundResult.InProgress) return;

            TryEnterLastSurvivorPhase();

            if (_lastSurvivorPhase.IsActive)
            {
                _lastSurvivorPhase.Tick(dt);

                // 배수구 완료로 그 사이에 확정됐을 수 있음 (StatusChanged 콜백에서 즉시 Finalize됨)
                if (Result != RoundResult.InProgress) return;

                if (!_lastSurvivorPhase.IsActive)
                {
                    // 90초(또는 라운드 잔여, 더 짧은 쪽) 소진 -> 그때까지의 탈출 인원으로 판정
                    Finalize(_escapedCount >= _escapeRequiredCount ? RoundResult.RunnersWin : RoundResult.SeekersWin);
                    return;
                }
            }

            _elapsedSeconds += dt;

            // 시간 종료 판정보다 먼저: 큰 dt로 끝까지 넘어가도 지나간 종반 단계는 전부 알린다.
            SuddenDeath.Update(RemainingSeconds);

            if (_elapsedSeconds >= _timeLimitSeconds)
            {
                // 제한시간 종료. 남은 도망자는 미탈출로 집계(별도 처리 없이 그냥 요구 인원 미달로 판정).
                Finalize(_escapedCount >= _escapeRequiredCount ? RoundResult.RunnersWin : RoundResult.SeekersWin);
            }
        }

        // 살아있는(=아직 탈출/태그/이탈로 처리되지 않은) 도망자가 1명이 되는 순간 1회 진입.
        private void TryEnterLastSurvivorPhase()
        {
            if (_lastSurvivorPhase.HasEntered) return;

            int undecided = _totalRunnerCount - _escapedCount - _taggedCount;
            if (undecided != 1) return; // 도망자가 0명이 되면(이미 전부 확정) 여기 도달하지 않음

            float roundRemaining = _timeLimitSeconds - _elapsedSeconds;
            bool gateOpen = _gate.IsOpen;
            int openValveCount = _interactionService.OpenValveCount;

            _lastSurvivorPhase.Enter(gateOpen, openValveCount, roundRemaining, _drainHatchRandomNextIndex);

            _roundEventSink.OnLastSurvivorPhaseEntered();

            if (_lastSurvivorPhase.ActiveDrainHatchId.HasValue)
            {
                _roundEventSink.OnDrainHatchActivated(_lastSurvivorPhase.ActiveDrainHatchId.Value);
            }
        }

        // 배수구 작업이 끝나면 그 작업자는 게이트 상태와 무관하게 탈출이 인정된다.
        private void HandleDrainHatchCompleted(DrainHatch hatch)
        {
            if (Result != RoundResult.InProgress) return;

            var workerIds = new List<int>(hatch.WorkerIds);

            foreach (var playerId in workerIds)
            {
                if (_resolvedPlayerIds.Contains(playerId)) continue;

                _resolvedPlayerIds.Add(playerId);
                ApplyEscape(playerId);
                _interactionService.RemovePlayerFromAllValves(playerId);

                if (Result != RoundResult.InProgress) break;
            }
        }

        private void ApplyEscape(int playerId)
        {
            _escapedCount++;
            bool allResolved = _escapedCount + _taggedCount == _totalRunnerCount;

            // 요구 인원 달성만으로는 끝나지 않는다(기획서 10-01 개정: 전원 확정 또는 시간 종료 때 판정).
            // 마지막으로 남은 도망자가 탈출로 확정됐다면 = 마지막 생존자 탈출 → 항상 도망자 승.
            if (allResolved)
            {
                Finalize(RoundResult.RunnersWin);
            }
        }

        private void ApplyTag(int playerId)
        {
            _taggedCount++;
            bool allResolved = _escapedCount + _taggedCount == _totalRunnerCount;

            if (allResolved)
            {
                Finalize(_escapedCount >= _escapeRequiredCount ? RoundResult.RunnersWin : RoundResult.SeekersWin);
            }
        }

        private void Finalize(RoundResult result)
        {
            Result = result;
            _roundEventSink.OnRoundEnded(result);
        }
    }
}
