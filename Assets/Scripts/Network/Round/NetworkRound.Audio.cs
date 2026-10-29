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
    // Audio-only bridge: existing actions and health remain authoritative in NetworkRound.
    // No new abilities, visual pulses, role rules or round settings are introduced here.
    public sealed partial class NetworkRound
    {
        private sealed class SoundMotion
        {
            public float NextMoan;
            public bool Wounded;
        }

        private readonly Dictionary<int, SoundMotion> soundMotion = new();
        private readonly HashSet<ValveId> rotatingSounds = new();
        private readonly Dictionary<int, double> healingSoundTimes = new();
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
            rotatingSounds.Clear();
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
            if (Time.time < nextSoundTick) return;
            nextSoundTick = Time.time + 0.1f;
            soundWorld ??= new SoundWorld();

            foreach (var player in NetworkPlayer.All)
            {
                Vector3 position = player.transform.position;
                if (!soundMotion.TryGetValue(player.PlayerId, out var motion))
                {
                    motion = new SoundMotion();
                    soundMotion.Add(player.PlayerId, motion);
                }
                if (player.Status != PlayerStatus.Alive || player.IsEcho)
                {
                    motion.Wounded = false;
                    continue;
                }
                int health = runnerHalves.TryGetValue(player.PlayerId, out int h) ? h : FullHalves;
                bool wounded = player.Role == PlayerRole.Runner && health == WoundedHalves;
                if (wounded && !motion.Wounded) motion.NextMoan = Time.time + 6f;
                if (wounded && Time.time >= motion.NextMoan)
                {
                    motion.NextMoan = Time.time + 6f;
                    EmitSound("Moan_" + Random.Range(0, 2), position, 9f, 0.65f, 1.2f);
                }
                motion.Wounded = wounded;
            }

            foreach (var id in rotatingSounds)
                if (ValveView.TryGet(id, out var valve))
                    EmitSound("Valve", valve.transform.position,
                        id == ValveId.A || id == ValveId.B || id == ValveId.E ? 6f : 12f,
                        0.5f, 0f, 100000 + (int)id, id == ValveId.B || id == ValveId.E);

            foreach (var pair in healingChannels)
            {
                var channel = pair.Value;
                if (!NetworkPlayer.TryGet(channel.ActorId, out var actor)) continue;
                if (healingSoundTimes.TryGetValue(pair.Key, out double next) && Time.realtimeSinceStartupAsDouble < next) continue;
                healingSoundTimes[pair.Key] = Time.realtimeSinceStartupAsDouble + (channel.IsBandage ? 2.0 : 2.25);
                EmitSound(channel.IsBandage ? "Bandage" : "Treat", actor.transform.position,
                    channel.IsBandage ? 6f : 9f, 0.65f, 1.2f, -1, false, channel.ActorId);
            }
        }

        private void ValveAudio(ValveId id, ValveSoundEvent evt)
        {
            if (!SoundMap) return;
            if (evt == ValveSoundEvent.RotatingStarted) rotatingSounds.Add(id);
            else if (evt == ValveSoundEvent.RotatingStopped)
            {
                rotatingSounds.Remove(id);
                StopAudioObservers(100000 + (int)id, false);
            }
            else if (evt == ValveSoundEvent.ReflowRipple && ValveView.TryGet(id, out var valve))
                EmitSound("Reflow", valve.transform.position, id == ValveId.B || id == ValveId.E ? 6f : 12f,
                    0.55f, 8f, -1, id == ValveId.B || id == ValveId.E);
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

        private void EmitSound(string clip, Vector3 origin, float radius, float volume, float duration,
            int loopKey = -1, bool underwater = false, int actionId = -1)
        {
            soundWorld ??= new SoundWorld();
            foreach (var listener in NetworkPlayer.All)
            {
                if (listener.Status != PlayerStatus.Alive || listener.IsEcho) continue;
                if (!soundWorld.Hear(origin, listener, radius, out var point, out float gain, out bool blocked)) continue;
                SoundTarget(listener.Owner, clip, point, volume * gain, duration, loopKey, underwater || blocked, actionId);
            }
        }

        [TargetRpc]
        private void SoundTarget(NetworkConnection connection, string clip, Vector3 point, float volume,
            float duration, int loopKey, bool muffled, int actionId)
        {
            if (soundPlayback != null) soundPlayback.Play(clip, point, volume, duration, loopKey, muffled, actionId);
        }

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
