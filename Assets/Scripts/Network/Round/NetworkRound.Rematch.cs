using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Round
{
    // Result screen + rematch vote (기획서 8 · 12.5 · 14.2 리매치 투표 · 15.4 RoundEnd). The server owns
    // the count (Core RematchVote); clients only send "yes" and read the synced numbers below.
    // Passed → RestartRound (role redraw, role reveal 5 s / Seeker hold 10 s). Failed → ReturnToLobby.
    public sealed partial class NetworkRound
    {
        private readonly SyncVar<int> rematchYesCount = new();
        private readonly SyncVar<int> rematchVoterCount = new();
        private readonly SyncVar<float> rematchRemainingSeconds = new();

        // Server only.
        private RematchVote rematchVote;
        private readonly HashSet<int> rematchConnectedIds = new();
        private readonly List<int> rematchLeftIds = new();

        // Client only: this client already sent its yes (one vote, no cancelling).
        private bool rematchRequested;
        private bool resultScreenWasShown;

        // Display values for the result screen UI (temporary IMGUI now, the real UI later).
        // The screen is up from the moment the round is decided until the vote restarts or ends it.
        public bool IsResultScreenShown => mode.Value == RoundMode.Full && result.Value != RoundResult.InProgress;
        public int RematchYesCount => rematchYesCount.Value;
        public int RematchVoterCount => rematchVoterCount.Value;
        public float RematchRemainingSeconds => rematchRemainingSeconds.Value;
        public bool HasRequestedRematch => rematchRequested;
        public bool CanRequestRematch => IsResultScreenShown && !rematchRequested && rematchRemainingSeconds.Value > 0f;

        // Called by the result screen's [찬성] button.
        public void RequestRematch()
        {
            if (!CanRequestRematch) return;
            rematchRequested = true;
            RequestRematchServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestRematchServer(NetworkConnection conn = null)
        {
            if (rematchVote == null || conn == null) return;
            if (!rematchVote.CastYes(conn.ClientId)) return;
            Debug.Log($"[MARCO][Rematch] P{conn.ClientId} 찬성 ({rematchVote.YesCount}/{rematchVote.VoterCount})");
            SyncRematch();
            ApplyRematchOutcome();
        }

        // From Step, the tick the round is decided.
        private void BeginRematchVote()
        {
            if (rematchVote != null) return;
            var ids = new List<int>();
            foreach (var player in NetworkPlayer.All) ids.Add(player.PlayerId);
            rematchVote = new RematchVote(ids);
            Debug.Log($"[MARCO][Rematch] 투표 시작 ({rematchVote.VoterCount}명, {RematchVote.DefaultDurationSeconds}초)");
            SyncRematch();
            TimeManager.OnTick += OnRematchTick;
        }

        private void EndRematchVote()
        {
            TimeManager.OnTick -= OnRematchTick;
            rematchVote = null;
            rematchYesCount.Value = 0;
            rematchVoterCount.Value = 0;
            rematchRemainingSeconds.Value = 0f;
        }

        private void OnRematchTick()
        {
            if (rematchVote == null) return;
            SyncRematchVoters();
            rematchVote.Tick((float)TimeManager.TickDelta);
            SyncRematch();
            ApplyRematchOutcome();
        }

        // Voters = everyone connected right now (leavers drop out, joiners count), read after FishNet
        // has despawned a leaver's player — the disconnect callback runs before that.
        private void SyncRematchVoters()
        {
            rematchConnectedIds.Clear();
            foreach (var player in NetworkPlayer.All)
            {
                rematchConnectedIds.Add(player.PlayerId);
                rematchVote.AddVoter(player.PlayerId);
            }
            rematchLeftIds.Clear();
            foreach (int id in rematchVote.VoterIds)
                if (!rematchConnectedIds.Contains(id)) rematchLeftIds.Add(id);
            foreach (int id in rematchLeftIds) rematchVote.RemoveVoter(id);
        }

        private void SyncRematch()
        {
            if (rematchVote == null) return;
            if (rematchYesCount.Value != rematchVote.YesCount) rematchYesCount.Value = rematchVote.YesCount;
            if (rematchVoterCount.Value != rematchVote.VoterCount) rematchVoterCount.Value = rematchVote.VoterCount;
            rematchRemainingSeconds.Value = rematchVote.RemainingSeconds;
        }

        private void ApplyRematchOutcome()
        {
            switch (rematchVote?.Outcome)
            {
                case RematchVoteOutcome.Passed:
                    Debug.Log($"[MARCO][Rematch] 가결 ({rematchVote.YesCount}/{rematchVote.VoterCount}) → 즉시 재시작");
                    RestartRound();
                    break;
                case RematchVoteOutcome.Failed:
                    Debug.Log($"[MARCO][Rematch] 부결 ({rematchVote.YesCount}/{rematchVote.VoterCount}) → 로비 복귀");
                    ReturnToLobby();
                    break;
            }
        }

        // RoundEnd → Lobby (기획서 15.4 로비 복귀 규칙): everyone, Echoes included, goes back to the state of a
        // player who just entered the lobby — Runner, alive, no tag, body/collision/gravity/sound back
        // (all driven by the status SyncVar) — standing on the lobby slots. The host starts again from LobbyMenu.
        [Server]
        public void ReturnToLobby()
        {
            Debug.Log("[MARCO][Round] ReturnToLobby");
            ResetRound();
            foreach (var player in NetworkPlayer.All)
            {
                player.SetRole(PlayerRole.Runner);
                player.SetStatus(PlayerStatus.Alive);
                player.SetCrouching(false);
                player.SetTreatmentLocked(false);
                if (player.TryGetComponent(out PlayerMovement movement)) movement.ResetSprintStamina();
            }
            MovePlayersToSpawn();
        }

        // Client: while the result screen is up the cursor stays free, which also stops moving, looking
        // and interacting (PlayerMovement / PlayerInteractor only act on a locked cursor).
        private void UpdateResultScreenClient()
        {
            bool shown = IsResultScreenShown;
            if (shown)
            {
                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                TestHudSkin.HoldsCursor = true;
            }
            else
            {
                rematchRequested = false;
                if (resultScreenWasShown)
                {
                    TestHudSkin.HoldsCursor = false;
                    // Rematch passed: straight into the new round. Lobby return: LobbyMenu frees the cursor itself.
                    if (mode.Value != RoundMode.NotStarted) Cursor.lockState = CursorLockMode.Locked;
                }
            }
            resultScreenWasShown = shown;
        }
    }
}
