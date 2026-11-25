using System;
using Marco.Core.WinCondition;
using Marco.Network.Player;

namespace Marco.Network.Round
{
    // §5.5-2 발생원 스냅샷 — server helpers for EmitSound callers. No remote calls, no synced state.
    public sealed partial class NetworkRound
    {
        // The Runners turning a ground valve (at most two are sent). Never throws: a fault gives none.
        private void ValveWorkers(ValveId id, out int first, out int second)
        {
            first = second = SourceSnapshotRules.None;
            try
            {
                var valve = board?.GetValve(id);
                if (valve == null) return;
                foreach (var player in NetworkPlayer.All)
                {
                    if (player == null || !valve.HasWorker(player.PlayerId)) continue;
                    if (first < 0) first = player.PlayerId;
                    else { second = player.PlayerId; return; }
                }
            }
            catch (Exception exception)
            {
                first = second = SourceSnapshotRules.None;
                SourceSnapshotRules.Report("ValveWorkers", exception);
            }
        }
    }
}
