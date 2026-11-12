using System;
using System.Collections.Generic;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Round
{
    // §3.2 노크 (server). Entry: PlayerInteractor.EchoKnock (ServerRpc, no payload, owner only) → ServerEchoKnock(OwnerId).
    // 14.2 P2: every check is here and any failure drops the request silently — no reply, no log, no exception.
    //   1 round running   2 requester an eliminated Echo (EchoBodyRules.CanKnock = Tagged; escapees and mid-round
    //   joiners are dropped)   3 20 s after the tag   4 25 s after the last accepted knock   5 fewer than 5 this round
    //   6 no knock already waiting
    // An accepted knock is stored with the requester's position at that moment and sounds 1.5 s later from the round
    // tick (no coroutine): EmitSound at the stored point — Talk-grade pulse, no source id on the wire, the Runner
    // stand-in silhouette (§5.5-2) for unoccluded living listeners, never on the knocker's own screen — then the
    // 8.3 lure report. Not recorded as award noise (8.2). State is cleared on round reset and when the player leaves.
    public sealed partial class NetworkRound
    {
        private sealed class KnockState
        {
            public float TaggedAt, FireAt;
            public float LastAccepted = float.NegativeInfinity;
            public int Used;
            public bool Pending;
            public Vector3 Point;
        }

        private readonly Dictionary<int, KnockState> knockStates = new(8);

        // For the checks and temporary logs.
        public static int KnocksAccepted { get; private set; }
        public static int KnocksSounded { get; private set; }

        // ApplyTag (Alive → Tagged): the 20 s lock starts now and the round's 5 knocks are fresh.
        private void RecordKnockTagged(int playerId)
        {
            try
            {
                if (!knockStates.TryGetValue(playerId, out var state))
                {
                    state = new KnockState();
                    knockStates[playerId] = state;
                }
                state.TaggedAt = Time.time;
                state.LastAccepted = float.NegativeInfinity;
                state.Used = 0;
                state.Pending = false;
            }
            catch (Exception exception) { SourceSnapshotRules.Report("RecordKnockTagged", exception); }
        }

        private void ClearKnocks() => knockStates.Clear();

        private void ForgetKnocks(int playerId) => knockStates.Remove(playerId);

        // Round end: a waiting knock never sounds on the result screen.
        private void CancelPendingKnocks()
        {
            foreach (var state in knockStates.Values) state.Pending = false;
        }

        [Server]
        internal void ServerEchoKnock(int playerId)
        {
            try
            {
                if (!IsServerInitialized || mode.Value == RoundMode.NotStarted || result.Value != RoundResult.InProgress) return;   // 1
                if (!NetworkPlayer.TryGet(playerId, out var echo) || echo == null || !EchoBodyRules.CanKnock(echo.Status)) return;  // 2
                if (!knockStates.TryGetValue(playerId, out var state)) return;   // no recorded tag: fail-closed
                float now = Time.time;
                if (!EchoKnockRules.Accept(now, state.TaggedAt, state.LastAccepted, state.Used, state.Pending)) return;   // 3–6
                state.Used++;
                state.LastAccepted = now;
                state.Pending = true;
                state.FireAt = now + EchoKnockRules.DelaySeconds;
                state.Point = EchoBodyRules.ClampFlight(echo.transform.position);   // the Echo's flight range
                KnocksAccepted++;
            }
            catch (Exception exception) { SourceSnapshotRules.Report("ServerEchoKnock", exception); }
        }

        // Round tick (Step): knocks whose 1.5 s are up sound now. No allocation.
        private void TickKnocksServer()
        {
            if (knockStates.Count == 0) return;
            float now = Time.time;
            foreach (var pair in knockStates)
            {
                var state = pair.Value;
                if (!state.Pending || now < state.FireAt) continue;
                state.Pending = false;
                try { FireKnock(pair.Key, state.Point); }
                catch (Exception exception) { SourceSnapshotRules.Report("FireKnock", exception); }
            }
        }

        private void FireKnock(int echoId, Vector3 point)
        {
            if (result.Value != RoundResult.InProgress) return;
            // The knocker must still be an eliminated Echo in the room; the point is the stored one.
            if (!NetworkPlayer.TryGet(echoId, out var echo) || echo == null || !EchoBodyRules.CanKnock(echo.Status)) return;
            EmitSound(EchoKnockRules.Clip, point, EchoKnockRules.Radius, 0f, EchoKnockRules.Seconds, sourceId: echoId,
                playAudio: false, forcePulse: true, snapshotA: SourceSnapshotRules.FakeRunner);
            KnocksSounded++;
            ServerReportKnock(echoId, point);   // 8.3 — when the knock actually sounds
        }
    }
}
