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
        private bool shoutPending;
        private Vector3 shoutStart;
        private readonly HashSet<int> suppressedRunners = new();
        public int ScreamCount { get; private set; }
        public float ShoutCooldownRemaining => Mathf.Max(0, localShoutReady - Time.time);

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
            if (IsServerInitialized && shoutPending)
            {
                if (!CanShout || Vector3.Distance(transform.position, shoutStart) > .15f)
                    FinishShout(false);
                else if (Time.time >= shoutEnds) FinishShout(true);
            }
            if (!IsOwner || Cursor.lockState != CursorLockMode.Locked || !ShoutRoundActive) return;
            if (player.Role == PlayerRole.Seeker && Input.GetKeyDown(KeyCode.R)) BeginShoutServer();
            if (player.Role == PlayerRole.Runner && Input.GetKeyDown(KeyCode.LeftControl)) SuppressShoutServer();
        }

        [ServerRpc]
        private void BeginShoutServer()
        {
            if (!CanShout || shoutPending || Time.time < shoutReady) return;
            shoutPending = true; shoutStart = transform.position; shoutEnds = Time.time + 1f;
            suppressedRunners.Clear();
            ShoutStateTarget(Owner, 1f, 0);
            foreach (var target in NetworkPlayer.All)
                if (target.Status == PlayerStatus.Alive && target.Role == PlayerRole.Runner &&
                    Vector3.Distance(transform.position, target.transform.position) <= 22f &&
                    target.TryGetComponent<PlayerMovement>(out var movement))
                    movement.FearTarget(target.Owner, 1f);
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
                    Vector3.Distance(skill.shoutStart, seeker.transform.position) > .15f ||
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
            if (fired) shoutReady = Time.time + 45f;
            ShoutStateTarget(Owner, 0, Mathf.Max(0, shoutReady - Time.time));
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
                if (listener.Status != PlayerStatus.Alive) continue;
                if (listener == player)
                    AbilitySoundTarget(listener.Owner, origin, radius, duration, 1, false, scream, true);
                else if (round.TryHearPoolVoice(origin, listener, radius, out var point, out float gain,
                    out bool blocked, out float perceivedRadius))
                    AbilitySoundTarget(listener.Owner, point, perceivedRadius,
                        duration * (listener.Role == PlayerRole.Seeker ? 1.5f : 1), gain, blocked, scream, false);
            }
        }

        [TargetRpc]
        private void ShoutStateTarget(NetworkConnection connection, float windup, float cooldown)
        { localShoutEnds = Time.time + windup; localShoutReady = Time.time + cooldown; }

        [TargetRpc]
        private void FearTarget(NetworkConnection connection, float remaining) => fearEnds = Time.time + remaining;

        [TargetRpc]
        private void AbilitySoundTarget(NetworkConnection connection, Vector3 point, float radius,
            float duration, float gain, bool blocked, bool scream, bool self)
        {
            var round = NetworkRound.Instance;
            if (!blocked) round?.ShowSoundPulse(point, radius, duration, voiceFromSeeker: !scream);
            if (!self) round?.NotifyOtherSound();
            if (self && scream) screamFlash = Time.time + .2f;
            // Dedicated survivor scream; Hurt remains the fallback for missing recordings.
            var clip = Resources.Load<AudioClip>(scream ? "MarcoSound/Scream" : "MarcoSound/Shout")
                ?? Resources.Load<AudioClip>("MarcoSound/Hurt");
            if (clip == null) return;
            var root = new GameObject("Ability vocal"); root.transform.position = point;
            var source = root.AddComponent<AudioSource>();
            source.clip = clip; source.spatialBlend = self ? 0 : 1; source.volume = gain;
            source.dopplerLevel = 0; source.maxDistance = 200;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0, 1, 1));
            if (blocked) root.AddComponent<AudioLowPassFilter>().cutoffFrequency = 1000;
            // Audio finishes naturally; pulse lifetime is controlled independently above.
            source.Play(); Destroy(root, scream ? clip.length + .1f : Mathf.Min(clip.length, 2.5f));
        }

        private void OnGUI()
        {
            if (!IsOwner || !ShoutRoundActive || player.Status != PlayerStatus.Alive) return;
            string message = player.Role == PlayerRole.Seeker
                ? (localShoutEnds > Time.time ? "외침 준비 중 · 이동하면 취소" :
                    ShoutCooldownRemaining > 0 ? $"외침 {ShoutCooldownRemaining:0.0}초" : "[R] 외침")
                : fearEnds > Time.time ? "[Left Ctrl] 숨 참기 · 숨 4.5 소모" : "";
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
