using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Audio;
using Marco.Network.Player;
using Marco.Network.Valves;
using UnityEngine;

namespace Marco.Network.Round
{
    // Routes effects and their presentation using the existing authoritative round actions.
    public sealed partial class NetworkRound
    {
        private sealed class SoundMotion
        {
            public float NextMoan;
            public bool Wounded;
            public Vector3 BreathPosition;
            public float StillSeconds, BreathMoved, BreathResetMoved, NextBreath;
            public int BreathCount; // breaths since the player last moved (S1 radius steps)
        }

        private readonly Dictionary<int, SoundMotion> soundMotion = new();
        private readonly HashSet<ValveId> rotatingSounds = new();
        private readonly Dictionary<(int, int), float> nextValvePulse = new();
        private readonly Dictionary<int, double> healingSoundTimes = new();
        private readonly Dictionary<int, Marco.Network.Vision.MovementPulseClock> movementClocks = new();
        private readonly Dictionary<int, Vector3> movementPositions = new();
        private float nextMovementTick, movementTickAt;
        private float nextSoundTick;
        private bool soundWasRunning;
        private RoundAudioPlayback soundPlayback;
        private SoundWorld soundWorld;
        private bool SoundMap => gameObject.scene.name == "NetworkPool_v1_2";

        private sealed class ValveAudioSink : IValveSoundSink
        {
            private readonly NetworkRound owner;
            public ValveAudioSink(NetworkRound owner) => this.owner = owner;
            public void NotifyValveSound(ValveId id, ValveSoundEvent evt) => owner.ValveAudio(id, evt);
        }

        private void StartAudioClient()
        {
            if (!SoundMap) return;
            if (soundPlayback == null) soundPlayback = gameObject.AddComponent<RoundAudioPlayback>();
            soundPlayback.Round = this;
        }

        private void StopAudioClient()
        {
            if (soundPlayback != null) Destroy(soundPlayback);
            soundPlayback = null;
        }

        private void ResetAudioServer()
        {
            soundMotion.Clear();
            movementClocks.Clear(); movementPositions.Clear(); nextMovementTick = movementTickAt = 0;
            rotatingSounds.Clear();
            nextValvePulse.Clear();
            healingSoundTimes.Clear();
            nextSoundTick = 0;
            soundWasRunning = false;
            if (IsServerInitialized && SoundMap) ClearAudioObservers();
        }

        private void TickAudioServer()
        {
            if (!SoundMap) return;
            bool running = mode.Value != RoundMode.NotStarted && result.Value == RoundResult.InProgress;
            if (!running)
            {
                if (soundWasRunning) ResetAudioServer();
                return;
            }
            soundWasRunning = true;
            TickMovementRings();
            if (Time.time < nextSoundTick) return;
            float soundDelta = nextSoundTick > 0 ? Mathf.Min(.5f, Time.time - nextSoundTick + .25f) : 0f;
            nextSoundTick = Time.time + .25f;
            soundWorld ??= new SoundWorld();

            foreach (var player in NetworkPlayer.All)
            {
                Vector3 position = player.transform.position;
                if (!soundMotion.TryGetValue(player.PlayerId, out var motion))
                {
                    motion = new SoundMotion { BreathPosition = position };
                    soundMotion.Add(player.PlayerId, motion);
                }
                if (player.Status != PlayerStatus.Alive || player.IsEcho)
                {
                    motion.Wounded = false;
                    continue;
                }
                TickBreath(player, motion, position, soundDelta);
                int health = runnerHalves.TryGetValue(player.PlayerId, out int h) ? h : FullHalves;
                bool wounded = player.Role == PlayerRole.Runner && health == WoundedHalves;
                if (wounded && !motion.Wounded) motion.NextMoan = Time.time + 6f;
                if (wounded && Time.time >= motion.NextMoan)
                {
                    motion.NextMoan = Time.time + 6f;
                    EmitSound("Moan_" + Random.Range(0, 2), position, 9f, 0.65f, 1.2f, sourceId: player.PlayerId, snapshotA: player.PlayerId);
                }
                motion.Wounded = wounded;
            }

            foreach (var id in rotatingSounds)
                if (ValveView.TryGet(id, out var valve))
                {
                    bool underwaterValve = id == ValveId.B || id == ValveId.E;
                    int workerA = SourceSnapshotRules.None, workerB = SourceSnapshotRules.None;
                    if (!underwaterValve) ValveWorkers(id, out workerA, out workerB);   // §5.5-2: the turning Runners
                    EmitSound(underwaterValve ? "ValveUnderwater" : "Valve",
                        valve.transform.position,
                        ValveSoundRadius(id),
                        0.5f, DefaultConfig.GetValveRotationSeconds(id, board.GetValve(id).HasReachedOpen),
                        100000 + (int)id, underwaterValve, snapshotA: workerA, snapshotB: workerB);
                }

            foreach (var pair in healingChannels)
            {
                var channel = pair.Value;
                if (!NetworkPlayer.TryGet(channel.ActorId, out var actor)) continue;
                if (healingSoundTimes.TryGetValue(pair.Key, out double next) && Time.realtimeSinceStartupAsDouble < next) continue;
                healingSoundTimes[pair.Key] = Time.realtimeSinceStartupAsDouble + (channel.IsBandage ? 2.0 : 2.25);
                EmitSound(channel.IsBandage ? "Bandage" : "Treat", actor.transform.position,
                    channel.IsBandage ? 6f : 9f, 0.65f, 1.2f, -1, false, channel.ActorId, channel.ActorId,
                    snapshotA: channel.ActorId, snapshotB: channel.IsBandage ? SourceSnapshotRules.None : channel.PatientId);
                RecordAwardNoise(channel.ActorId, channel.IsBandage ? AwardNoise.Bandage : AwardNoise.Treat, channel.IsBandage ? 6f : 9f, 1.2f); // 기획서 8.2 — 치료자만
            }
            TickDrainWorkPulse();
        }

        private void TickMovementRings()
        {
            if (Time.time < nextMovementTick) return;
            float dt = movementTickAt > 0 ? Time.time - movementTickAt : .1f;
            movementTickAt = Time.time;
            nextMovementTick = Time.time + .1f;
            soundWorld ??= new SoundWorld();
            foreach (var source in NetworkPlayer.All)
            {
                int id = source.PlayerId;
                if (!movementClocks.TryGetValue(id, out var clock))
                    movementClocks[id] = clock = new Marco.Network.Vision.MovementPulseClock();
                Vector3 position = source.transform.position;
                float speed = movementPositions.TryGetValue(id, out var previous)
                    ? Vector3.ProjectOnPlane(position-previous, Vector3.up).magnitude / Mathf.Max(dt,.0001f) : 0;
                movementPositions[id] = position;
                int water = soundWorld.WaterState(source, out float surface);
                bool grounded = soundWorld.TryFloorNear(source, out var floor, .09f);
                bool fast = Marco.Network.Vision.MovementPulseClock.IsFastMovement(source.Role, speed, water == 1);
                bool alive = source.Status == PlayerStatus.Alive && !source.IsEcho;
                bool step = clock.Advance(position, dt, alive && water != 2 && (grounded || water == 1),
                    fast, out bool ring);
                fast = clock.Fast;
                if (!step) continue;
                bool swimming = water == 1;
                string material = swimming ? "" : soundWorld.Surface(floor, position);
                int variants = SoundWorld.FootstepVariants(material);
                if (!swimming && variants == 0) continue;
                string clip = swimming ? "SurfaceSwim" : variants == 1
                    ? material : material + "_" + Random.Range(0, variants);
                Vector3 origin = position;
                if (swimming) origin.y = surface;
                Vector3 ringPoint = position;
                ringPoint.y = swimming ? surface + .025f : source.GetComponent<CharacterController>().bounds.min.y + .025f;
                float radius = (fast ? 6f : 2f) * (swimming ? 1f : soundWorld.SurfaceMultiplier(floor, position));
                RecordAwardNoise(id, AwardNoise.Footstep, radius, fast ? .8f : .4f); // 기획서 8.2 — 발소리 · 헤엄 1회
                foreach (var listener in NetworkPlayer.All)
                {
                    if (listener == source || listener.Status != PlayerStatus.Alive || listener.IsEcho) continue;
                    if (!soundWorld.Hear(origin, listener, radius, out var perceived, out float gain, out bool blocked)) continue;
                    Vector3 visiblePoint = ringPoint;
                    if (blocked)
                    {
                        visiblePoint = perceived;
                        visiblePoint.y = listener.GetComponent<CharacterController>().bounds.min.y + .025f;
                    }
                    float duration = (fast ? .8f : .4f) * (listener.Role == PlayerRole.Seeker ? 1.5f : 1f);
                    MovementStepTarget(listener.Owner, id, clip, perceived, gain * (swimming ? .55f : .7f),
                        duration, blocked, ring && fast, visiblePoint, source.Role == PlayerRole.Seeker);
                }
            }
        }

        [TargetRpc]
        private void MovementStepTarget(NetworkConnection connection, int source, string clip, Vector3 soundPoint,
            float volume, float duration, bool blocked, bool showRing, Vector3 ringPoint, bool seeker)
        {
            var local = NetworkPlayer.Local;
            if (local == null || local.PlayerId == source) return;
            NotifyOtherSound();
            soundPlayback?.Play(clip, soundPoint, volume, duration, -1, blocked, -1);
            if (showRing)
                local.GetComponentInChildren<Marco.Network.Vision.WalkingFootRings>()?.Show(source, ringPoint, true, seeker);
        }

        private void ValveAudio(ValveId id, ValveSoundEvent evt)
        {
            if (!SoundMap) return;
            if (evt == ValveSoundEvent.RotatingStarted) rotatingSounds.Add(id);
            else if (evt == ValveSoundEvent.RotatingStopped)
            {
                rotatingSounds.Remove(id);
                int key = 100000 + (int)id;
                foreach (var listener in NetworkPlayer.All) nextValvePulse.Remove((key, listener.PlayerId));
                StopAudioObservers(key, false);
            }
            else if (evt == ValveSoundEvent.ReflowRipple && ValveView.TryGet(id, out var valve))
                EmitSound("Reflow", valve.transform.position, 12f,
                    0.55f, DefaultConfig.GetValveRotationSeconds(id, reopen: false), -1, id == ValveId.B || id == ValveId.E);
        }

        private void StopHealingAudio(int actorId)
        {
            healingSoundTimes.Remove(actorId);
            if (SoundMap) StopAudioObservers(actorId, true);
        }

        private void PlayHurtAudio(NetworkPlayer target)
        {
            if (SoundMap) HurtAudioTarget(target.Owner);
        }

        [Server]
        public void EmitDrowningSound(NetworkPlayer player, float surface)
        {
            var origin = player.transform.position;
            origin.y = surface + .1f;
            EmitSound("Hurt", origin, 22f, 1f, 1.5f, sourceId: player.PlayerId);
            RecordAwardNoise(player.PlayerId, AwardNoise.Drowning, 22f, 1.5f); // 기획서 8.2
            soundWorld ??= new SoundWorld();
            foreach (var listener in NetworkPlayer.All)
                if (listener.Status == PlayerStatus.Alive && soundWorld.Hear(origin, listener, 22f, out var point, out _, out bool blocked) &&
                    !(RippleServerFeatures.DrowningOcclusion && blocked))
                    DrowningPulseTarget(listener.Owner, point, player.PlayerId,
                        SourceSnapshotRules.ForListener(player.PlayerId, listener, blocked), SourceSnapshotRules.None);   // §5.5-2
        }

        [TargetRpc]
        private void DrowningPulseTarget(NetworkConnection connection, Vector3 point, int source, int snapshotA, int snapshotB)
        {
            var local = NetworkPlayer.Local;
            if (local == null) return;
            if (source != local.PlayerId) NotifyOtherSound();
            local.GetComponentInChildren<Marco.Network.Vision.WalkingFootRings>()?.EmitWaterPulse(point, 22f);
        }

        private static bool IsSelfOnlySound(string clip) => !RippleServerFeatures.BreathAudible && (clip == "Breath" || clip.StartsWith("Breath_"));
        private static bool CanReceiveSound(string clip, int sourceId, int listenerId) =>
            !IsSelfOnlySound(clip) || sourceId == listenerId;

        private void TickBreath(NetworkPlayer player, SoundMotion motion, Vector3 position, float dt)
        {
            float distance = Vector3.Distance(position, motion.BreathPosition);
            motion.BreathPosition = position;
            if (IsStartLocked(player.Role) || soundWorld.WaterState(player, out _) == 2 ||
                (player.Role == PlayerRole.Runner && valves != null && valves.IsWorking(player.PlayerId)))
                return;
            // S11: a player the server holds in place (treatment lock) is not camping — the timer stops.
            if (RippleServerFeatures.BreathPauseWhenLocked && player.IsTreatmentLocked) return;
            motion.BreathMoved += distance;
            motion.BreathResetMoved += distance;
            if ((motion.BreathMoved >= 3f && motion.NextBreath == 0f) || motion.BreathResetMoved >= 5f)
            {
                motion.StillSeconds = motion.BreathMoved = motion.BreathResetMoved = motion.NextBreath = 0;
                motion.BreathCount = 0;
                return;
            }
            motion.StillSeconds += dt;
            if (motion.StillSeconds < Mathf.Max(20f, motion.NextBreath)) return;
            motion.NextBreath = motion.StillSeconds + 15f;
            string clip = player.Role == PlayerRole.Seeker ? "Breath_Seeker" : "Breath_Runner";
            float breathRadius = RippleServerFeatures.BreathAudible ? PulseSpec.BreathRadius(motion.BreathCount++) : 0f;
            EmitSound(clip, position, breathRadius, .65f, .6f, sourceId: player.PlayerId);
            RecordAwardNoise(player.PlayerId, AwardNoise.Breath, breathRadius, .6f); // 기획서 8.2 · §3.6 — the radius actually emitted (3m→9m), 0.6s
        }

        private void EmitSound(string clip, Vector3 origin, float radius, float volume, float duration,
            int loopKey = -1, bool underwater = false, int actionId = -1, int sourceId = -1, bool playAudio = true, bool forcePulse = false,
            int snapshotA = SourceSnapshotRules.None, int snapshotB = SourceSnapshotRules.None)
        {
            soundWorld ??= new SoundWorld();
            foreach (var listener in NetworkPlayer.All)
            {
                if (listener.Status != PlayerStatus.Alive && !listener.HasEchoBody) continue;
                if (!CanReceiveSound(clip, sourceId, listener.PlayerId)) continue;
                if (IsSelfOnlySound(clip))
                {
                    SoundTarget(listener.Owner, clip, Vector3.zero, volume, duration, loopKey, underwater, actionId, true, false, 0f, 0f,
                        SourceSnapshotRules.None, SourceSnapshotRules.None);
                    continue;
                }
                bool own = sourceId == listener.PlayerId;
                if (!soundWorld.HearResolved(origin, listener, radius, out var point, out float gain,
                    out bool blocked, out float perceivedRadius) && !own) continue;
                if (own) { point = origin; gain = 1f; blocked = false; perceivedRadius = radius; }
                float durationScale = !own && listener.Role == PlayerRole.Seeker && !listener.HasEchoBody ? 1.5f : 1f;
                float pulseSeconds = clip == "ValveUnderwater" ? 2.5f : duration;
                bool showPulse = !blocked && (forcePulse || clip == "Valve" || clip == "ValveUnderwater" || clip == "Reflow" || clip == "Bandage" ||
                    clip == "Treat" || clip.StartsWith("Moan_"));
                if (showPulse && loopKey >= 0)
                {
                    var key = (loopKey, listener.PlayerId);
                    showPulse = !nextValvePulse.TryGetValue(key, out float next) || Time.time >= next;
                    if (showPulse) nextValvePulse[key] = underwater ? Time.time + 2.5f : float.PositiveInfinity;
                }
                if (listener.HasEchoBody) { if (showPulse) pulseSeconds = 3f; }
                else pulseSeconds *= durationScale;
                // §5.5-2: the snapshot goes with the ring only (fail-closed: None on any doubt).
                int seenA = SourceSnapshotRules.None, seenB = SourceSnapshotRules.None;
                if (showPulse && SourceSnapshotRules.IsCharacterSound(clip))
                {
                    seenA = SourceSnapshotRules.ForListener(snapshotA, listener, blocked);
                    seenB = SourceSnapshotRules.ForListener(snapshotB, listener, blocked);
                }
                SoundTarget(listener.Owner, playAudio ? clip : PulseSpec.NoAudio, point, volume * gain, duration * durationScale, loopKey,
                    underwater || blocked, actionId, false, !own, showPulse ? perceivedRadius : 0f, pulseSeconds, seenA, seenB);
            }
        }

        [TargetRpc]
        private void SoundTarget(NetworkConnection connection, string clip, Vector3 point, float volume,
            float duration, int loopKey, bool muffled, int actionId, bool local, bool fromOther, float pulseRadius, float pulseSeconds,
            int snapshotA, int snapshotB)
        {
            if (fromOther) NotifyOtherSound();
            if (pulseRadius > 0) ShowSoundPulse(point, pulseRadius, pulseSeconds);
            if (soundPlayback != null) soundPlayback.Play(clip, point, volume, duration, loopKey, muffled, actionId, local);
            try { Marco.Network.Vision.DirectionHints.OnSound(clip, point, muffled, loopKey, actionId, fromOther, pulseRadius, pulseSeconds); } catch (System.Exception exception) { Marco.Network.Vision.DirectionHints.ReportFault("SoundTarget", exception); }
        }

        internal bool UsesPoolAudio => SoundMap;

        internal bool TryHearPoolVoice(Vector3 origin, NetworkPlayer listener, float radius,
            out Vector3 point, out float gain, out bool blocked, out float perceivedRadius)
        {
            soundWorld ??= new SoundWorld();
            return soundWorld.HearResolved(origin, listener, radius, out point, out gain, out blocked, out perceivedRadius);
        }

        internal void ShowSoundPulse(Vector3 point, float radius, float duration, bool? voiceFromSeeker = null)
        {
            var rings = NetworkPlayer.Local?.GetComponentInChildren<Marco.Network.Vision.WalkingFootRings>();
            rings?.ShowSound(point, radius, duration, voiceFromSeeker);
        }

        internal void PlaySeekerSwingAudio() =>
            soundPlayback?.Play("SeekerSwing", Vector3.zero, .85f, 0f, -1, false, -1, true);

        internal void PlaySeekerAttackAudio() =>
            soundPlayback?.Play("SeekerAttack", Vector3.zero, .7f, 0f, -1, false, -1, true);

        internal void NotifyOtherSound() => soundPlayback?.NotifyOtherSound();

        [TargetRpc]
        private void HurtAudioTarget(NetworkConnection connection)
        {
            if (soundPlayback != null) soundPlayback.Play("Hurt", Vector3.zero, 0.7f, 1.2f, -1, false, -1, true);
        }

        [ObserversRpc]
        private void StopAudioObservers(int key, bool healing)
        {
            if (soundPlayback != null) soundPlayback.Stop(key, healing);
        }

        [ObserversRpc]
        private void ClearAudioObservers()
        {
            if (soundPlayback != null) soundPlayback.Clear();
        }
    }
}
