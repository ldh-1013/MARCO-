using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Player
{
    public sealed partial class PlayerMovement
    {
        private float shoutEnds, shoutReady, localShoutEnds, localShoutReady, fearEnds, screamFlash;
        private const float ShoutWindupSeconds = 1f, ShoutCooldownSeconds = 45f;
        private bool shoutPending, localShoutRequested;
        private readonly HashSet<int> suppressedRunners = new();
        public int ScreamCount { get; private set; }
        public float ShoutCooldownRemaining => Mathf.Max(0, localShoutReady - Time.time);
        public float ShoutCooldownFraction => Mathf.Clamp01(ShoutCooldownRemaining / ShoutCooldownSeconds);
        public bool IsPreparingShout => localShoutRequested && CanShout;
        public float ShoutPreparationFraction => IsPreparingShout
            ? Mathf.Clamp01(1f - (localShoutEnds - Time.time) / ShoutWindupSeconds) : 0f;

        private bool ShoutRoundActive => NetworkRound.Instance != null &&
            NetworkRound.Instance.Mode != RoundMode.NotStarted &&
            NetworkRound.Instance.Result == RoundResult.InProgress;
        private bool CanShout => ShoutRoundActive && player.Status == PlayerStatus.Alive &&
            player.Role == PlayerRole.Seeker && !player.IsTreatmentLocked &&
            !NetworkRound.Instance.IsStartLocked(player.Role);

        private void ResetShoutServer()
        {
            shoutPending = false; shoutEnds = shoutReady = 0;
            suppressedRunners.Clear(); ScreamCount = 0;
            ShoutStateTarget(Owner, 0, 0);
            FearTarget(Owner, 0);
        }

        private void TickShout()
        {
            // Predict the stop before owner movement runs; the server confirms the ability state.
            if (IsOwner)
            {
                bool held = Cursor.lockState == CursorLockMode.Locked && CanShout && Input.GetKey(KeyCode.R);
                if (localShoutRequested && !held)
                {
                    localShoutRequested = false;
                    localShoutEnds = 0;
                    CancelShoutServer();
                }
                if (held && Input.GetKeyDown(KeyCode.R) && !localShoutRequested && ShoutCooldownRemaining <= 0)
                {
                    localShoutRequested = true;
                    localShoutEnds = Time.time + ShoutWindupSeconds;
                    BeginShoutServer();
                }
            }
            if (IsServerInitialized && shoutPending)
            {
                if (!CanShout) FinishShout(false);
                else if (Time.time >= shoutEnds) FinishShout(true);
            }
            if (!IsOwner || Cursor.lockState != CursorLockMode.Locked || !ShoutRoundActive) return;
            if (player.Role == PlayerRole.Runner && Input.GetKeyDown(KeyCode.LeftControl)) SuppressShoutServer();
        }

        [ServerRpc]
        private void BeginShoutServer()
        {
            if (shoutPending) return;
            if (!CanShout || Time.time < shoutReady)
            {
                ShoutStateTarget(Owner, 0, Mathf.Max(0, shoutReady - Time.time));
                return;
            }
            shoutPending = true; shoutEnds = Time.time + ShoutWindupSeconds;
            suppressedRunners.Clear();
            ShoutStateTarget(Owner, ShoutWindupSeconds, 0);
            foreach (var target in NetworkPlayer.All)
                if (target.Status == PlayerStatus.Alive && target.Role == PlayerRole.Runner &&
                    Vector3.Distance(transform.position, target.transform.position) <= 22f &&
                    target.TryGetComponent<PlayerMovement>(out var movement))
                    movement.FearTarget(target.Owner, 1f);
        }

        [ServerRpc]
        private void CancelShoutServer()
        {
            if (shoutPending) FinishShout(false);
        }

        [ServerRpc]
        private void SuppressShoutServer()
        {
            if (!ShoutRoundActive || player.Status != PlayerStatus.Alive || player.Role != PlayerRole.Runner ||
                player.IsTreatmentLocked || NetworkRound.Instance.IsStartLocked(player.Role)) return;
            foreach (var seeker in NetworkPlayer.All)
            {
                if (!seeker.TryGetComponent<PlayerMovement>(out var skill) || !skill.shoutPending ||
                    !skill.CanShout || Time.time >= skill.shoutEnds ||
                    Vector3.Distance(transform.position, seeker.transform.position) > 22f ||
                    skill.suppressedRunners.Contains(player.PlayerId)) continue;
                if (!IsDiving && !TrySpendSuppressionBreath()) return;
                skill.suppressedRunners.Add(player.PlayerId);
                BreathTarget(Owner, serverBreath, serverForcedSurface, Mathf.Max(0, serverPenaltyUntil - Time.time));
                FearTarget(Owner, 0);
            }
        }

        private void FinishShout(bool fired)
        {
            shoutPending = false;
            if (fired) shoutReady = Time.time + ShoutCooldownSeconds;
            ShoutStateTarget(Owner, 0, Mathf.Max(0, shoutReady - Time.time));
            foreach (var target in NetworkPlayer.All)
                if (target.Role == PlayerRole.Runner && target.TryGetComponent<PlayerMovement>(out var movement))
                    movement.FearTarget(target.Owner, 0);
            if (!fired) return;
            EmitAbilitySound(false);
            foreach (var target in NetworkPlayer.All)
            {
                if (target.Status != PlayerStatus.Alive || target.Role != PlayerRole.Runner ||
                    Vector3.Distance(transform.position, target.transform.position) > 22f ||
                    !target.TryGetComponent<PlayerMovement>(out var movement)) continue;
                movement.FearTarget(target.Owner, 0);
                if (movement.IsDiving || suppressedRunners.Contains(target.PlayerId)) continue;
                movement.ScreamCount++;
                NetworkRound.Instance?.RecordAwardNoise(target.PlayerId, AwardNoise.Scream, 9f, 1f); // 기획서 8.1 · 8.2
                movement.EmitAbilitySound(true);
            }
        }

        private void EmitAbilitySound(bool scream)
        {
            var round = NetworkRound.Instance;
            Vector3 origin = transform.position; origin.y = WaterEyeHeight;
            float radius = scream ? 9f : 22f, duration = scream ? 1f : 2.5f;
            foreach (var listener in NetworkPlayer.All)
            {
                if (listener.Status != PlayerStatus.Alive && !listener.HasEchoBody) continue;
                if (listener.HasEchoBody && !round.CanTarget(listener)) continue;   // 3.2-1: Echo bodies watch the map's pulses
                if (listener == player)
                    AbilitySoundTarget(listener.Owner, origin, radius, duration, 1, false, scream, true, SourceSnapshotRules.None, SourceSnapshotRules.None);
                else if (round.TryHearPoolVoice(origin, listener, radius, out var point, out float gain,
                    out bool blocked, out float perceivedRadius))
                    AbilitySoundTarget(listener.Owner, point, perceivedRadius,
                        listener.HasEchoBody ? 3f : duration * (listener.Role == PlayerRole.Seeker ? 1.5f : 1), gain, blocked, scream, false,
                        SourceSnapshotRules.ForListener(player.PlayerId, listener, blocked), SourceSnapshotRules.None);
            }
        }

        [TargetRpc]
        private void ShoutStateTarget(NetworkConnection connection, float windup, float cooldown)
        {
            // A delayed windup acknowledgement must not restart a released hold.
            localShoutEnds = localShoutRequested && windup > 0 ? Time.time + windup : 0;
            localShoutReady = Time.time + cooldown;
            if (windup <= 0) localShoutRequested = false;
        }

        [TargetRpc]
        private void FearTarget(NetworkConnection connection, float remaining) => fearEnds = Time.time + remaining;

        [TargetRpc]
        private void AbilitySoundTarget(NetworkConnection connection, Vector3 point, float radius,
            float duration, float gain, bool blocked, bool scream, bool self, int snapshotA, int snapshotB)
        {
            var round = NetworkRound.Instance;
            if (!blocked) round?.ShowSoundPulse(point, radius, duration, voiceFromSeeker: !scream);
            if (!blocked && !self && (snapshotA >= 0 || snapshotB >= 0))   // §5.5-2
                try { Marco.Network.Vision.SourceSnapshots.ShowBoth(snapshotA, snapshotB, duration); } catch (System.Exception exception) { Marco.Network.Vision.DirectionHints.ReportFault("SourceSnapshots", exception); }
            if (!self) round?.NotifyOtherSound();
            if (self && scream) screamFlash = Time.time + .2f;
            // Keep survivor screams and seeker vocals separate, including missing assets.
            var clip = Resources.Load<AudioClip>(scream ? "MarcoSound/Scream" : "MarcoSound/Shout");
            if (clip == null) return;
            var root = new GameObject("Ability vocal"); root.transform.position = point;
            var source = root.AddComponent<AudioSource>();
            source.clip = clip; source.spatialBlend = self ? 0 : 1; source.volume = gain;
            source.dopplerLevel = 0; source.maxDistance = 200;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0, 1, 1));
            // Wall muffling (1,000 Hz) and the listener's own dive (650 Hz) — same values as every round sound (11-14).
            Marco.Network.Audio.RoundAudioPlayback.Follow(root.AddComponent<AudioLowPassFilter>(), blocked);
            // Audio finishes naturally; pulse lifetime is controlled independently above.
            source.Play(); Destroy(root, clip.length + .1f);
        }

        private void OnGUI()
        {
            if (!IsOwner || !ShoutRoundActive || player.Status != PlayerStatus.Alive) return;
            string message = player.Role == PlayerRole.Runner && fearEnds > Time.time
                ? "[Left Ctrl] 숨 참기 · 숨 4.5 소모" : "";
            if (message.Length > 0) GUI.Box(new Rect(Screen.width / 2f - 160, Screen.height - 170, 320, 30), message);
            if (screamFlash > Time.time)
            {
                var previous = GUI.color;
                GUI.color = new Color(1, 1, 1, (screamFlash - Time.time) * 2);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, 8), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0, Screen.height - 8, Screen.width, 8), Texture2D.whiteTexture);
                GUI.color = previous;
            }
        }
    }
}
