using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Round
{
    // One award as it travels to clients (14.3 GameEnd awards[]): winners (empty = 수상자 없음) and the
    // number behind it (screams, silence score, lures).
    public struct AwardWire
    {
        public AwardKind Kind;
        public int[] Winners;
        public float Value;
    }

    // 기획서 8 어워드 — server side. Core (AwardTally · AwardJudge · LureJudge) decides; this feeds it:
    //   sounds    RecordAwardNoise, one line at each server sound point (NetworkRound.Audio · PlayerVoice ·
    //             PlayerMovement.Shout), radius after the floor multiplier, once per pulse
    //   movement  horizontal distance of each alive Runner from the server's copy of its transform
    //   escape / tag freeze the Runner's numbers (MarkEscaped · ApplyTag)
    //   lures     ServerReportKnock (the knock itself is the sound part's), Seeker sampled every 0.5 s
    // Judged once when the round is decided and sent to everyone (GameEnd, buffered so the result screen
    // still gets it if it opens first). Not judged in ValvesOnly mode — no result is ever decided there.
    public sealed partial class NetworkRound
    {
        // A teleport (spawn, rematch) must not count as walking: larger single-frame moves are dropped.
        private const float AwardTeleportMeters = 2f;

        private AwardTally awardTally;
        private LureJudge lureJudge;
        private float nextLureSample;
        private readonly Dictionary<int, Vector3> awardPositions = new();
        private bool awardsSent;

        // Client: the round's awards once judged (null until then and after a reset).
        public IReadOnlyList<AwardWire> RoundAwards { get; private set; }
        // Client, the knocking Echo only: when the last "낚았다!" arrived (KnockLureSucceeded, 8.3).
        public float LastLureNoticeTime { get; private set; } = float.NegativeInfinity;

        // StartRound, after roles are drawn and everyone is set Alive: the round's Runners.
        private void BeginAwards()
        {
            awardTally = new AwardTally();
            foreach (var player in NetworkPlayer.All)
                if (player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive) awardTally.AddRunner(player.PlayerId);
            lureJudge = new LureJudge();
            lureJudge.Succeeded += OnLureSucceeded;
            nextLureSample = 0f;
            awardPositions.Clear();
            awardsSent = false;
        }

        private void ResetAwards()
        {
            lureJudge?.Stop();
            awardTally = null;
            lureJudge = null;
            awardPositions.Clear();
            awardsSent = false;
            if (IsServerInitialized) AwardsObservers(System.Array.Empty<AwardWire>());
        }

        private bool AwardsRecording => awardTally != null && startSchedule.HasInGameStarted && result.Value == RoundResult.InProgress;

        // One line at each server sound point. playerId = whose sound it is; radius after the floor multiplier.
        [Server]
        internal void RecordAwardNoise(int playerId, AwardNoise kind, float radius, float duration)
        {
            if (AwardsRecording) awardTally.RecordNoise(playerId, kind, radius, duration);
        }

        // 8.3 hook for the knock (sound part, 3.2): call once when the knock actually sounds (after its 1.5 s
        // delay) with the knocking Echo and the knock position. Requests from anyone but an eliminated Echo
        // (escapee, mid-round joiner, Runner, Seeker) are dropped here as well.
        [Server]
        public void ServerReportKnock(int echoId, Vector3 position)
        {
            if (!AwardsRecording || lureJudge == null) return;
            if (!NetworkPlayer.TryGet(echoId, out var echo) || !EchoBodyRules.CanKnock(echo.Status)) return;
            if (!TryGetSeeker(out var seeker)) return;
            lureJudge.ReportKnock(echoId, Time.time, ToPoint(position), ToPoint(seeker.transform.position));
        }

        private void TickAwardsServer()
        {
            if (!AwardsRecording) return;
            foreach (var player in NetworkPlayer.All)
            {
                Vector3 position = player.transform.position;
                bool walking = player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive;
                if (walking && awardPositions.TryGetValue(player.PlayerId, out var previous))
                {
                    float step = Vector3.ProjectOnPlane(position - previous, Vector3.up).magnitude;
                    if (step < AwardTeleportMeters) awardTally.RecordDistance(player.PlayerId, step);
                }
                awardPositions[player.PlayerId] = position;
            }
            if (Time.time < nextLureSample || !TryGetSeeker(out var seeker)) return;
            nextLureSample = Time.time + LureJudge.SampleSeconds;
            lureJudge.SampleSeeker(Time.time, ToPoint(seeker.transform.position));
        }

        private void AwardEscaped(int playerId) => awardTally?.MarkEscaped(playerId);
        private void AwardTagged(int playerId) => awardTally?.MarkTagged(playerId);
        // Row 6: any landed hit (first hit or Echo) ends every lure watch.
        private void AwardSeekerHit() => lureJudge?.SeekerTagged();
        private void AwardPlayerLeft(int playerId) => awardTally?.Remove(playerId);

        // From Step, the tick the round is decided.
        private void FinishAwards()
        {
            if (awardsSent || awardTally == null) return;
            awardsSent = true;
            lureJudge?.Stop();
            var outcomes = AwardJudge.JudgeAll(awardTally);
            var wires = new AwardWire[outcomes.Length];
            for (int i = 0; i < outcomes.Length; i++)
            {
                var winners = new int[outcomes[i].Winners.Count];
                for (int w = 0; w < winners.Length; w++) winners[w] = outcomes[i].Winners[w];
                wires[i] = new AwardWire { Kind = outcomes[i].Kind, Winners = winners, Value = outcomes[i].Value };
                Debug.Log($"[MARCO][Awards] {AwardRules.Title(outcomes[i].Kind)}: " +
                    (winners.Length == 0 ? AwardRules.NoWinner : string.Join(", ", winners) + " · " + AwardRules.Evidence(outcomes[i].Kind, outcomes[i].Value)));
            }
            AwardsObservers(wires);
        }

        // Buffered: a client whose result screen opens before this arrives still gets it; ResetAwards
        // replaces it with an empty list for the next round.
        [ObserversRpc(BufferLast = true)]
        private void AwardsObservers(AwardWire[] awards) => RoundAwards = awards == null || awards.Length == 0 ? null : awards;

        private void OnLureSucceeded(int echoId)
        {
            awardTally.RecordLure(echoId);
            Debug.Log($"[MARCO][Awards] P{echoId} 노크 유인 성공 (누적 {awardTally.LuresOf(echoId)})");
            if (NetworkPlayer.TryGet(echoId, out var echo)) KnockLureSucceededTarget(echo.Owner);
        }

        // 14.3 KnockLureSucceeded: no payload, the knocking Echo only.
        [TargetRpc]
        private void KnockLureSucceededTarget(NetworkConnection conn) => LastLureNoticeTime = Time.unscaledTime;

        private static bool TryGetSeeker(out NetworkPlayer seeker)
        {
            foreach (var player in NetworkPlayer.All)
                if (player.Role == PlayerRole.Seeker && player.Status == PlayerStatus.Alive) { seeker = player; return true; }
            seeker = null;
            return false;
        }

        private static AwardPoint ToPoint(Vector3 v) => new(v.x, v.y, v.z);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Test hook (render check · cheats): the local client shows "낚았다!" as if a lure succeeded.
        internal void ShowLureNoticeLocally() => LastLureNoticeTime = Time.unscaledTime;
#endif
    }
}
