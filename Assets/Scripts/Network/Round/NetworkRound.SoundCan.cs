using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.WinCondition;
using Marco.Network.Audio;
using Marco.Network.Player;
using Marco.Network.Vision;
using Marco.Network.Voice;
using UnityEngine;

namespace Marco.Network.Round
{
    public struct SoundCanPlacement { public int Id; public Vector3 Position; }

    // Server owns inventory, recording sessions, audio bytes, placement and playback routing.
    // Audio never goes to disk; only currently audible listeners receive playback frames.
    public sealed partial class NetworkRound
    {
        private sealed class SoundCan
        {
            public int OwnerId, Id, Token, Frames, NextFrame;
            public SoundCanStage Stage;
            public byte[] Audio;
            public Vector3 Position;
            public float Started, NextProgress;
            public Vector3 PlacementStart;
            public int PlacementHealth;
            public bool WasCrouching;
        }

        private readonly SyncList<bool> soundCanPickups = new();
        private readonly SyncList<SoundCanPlacement> soundCanPlacements = new();
        private readonly Dictionary<int, SoundCan> soundCans = new();
        private readonly List<int> soundCanOwners = new();
        private readonly List<GameObject> soundCanPickupViews = new();
        private readonly Dictionary<int, GameObject> soundCanPlacedViews = new();
        private readonly List<int> staleSoundCanViews = new();
        private int nextSoundCanToken, nextSoundCanId;
        private Material soundCanMaterial;
        private RecordedSoundPlayback recordedPlayback;

        private bool CanUseSoundCan(int playerId, out NetworkPlayer player)
        {
            player = null;
            if (!DecoyRunning || !TryGetAliveRunner(playerId, out player) || player.IsTreatmentLocked ||
                healingChannels.ContainsKey(playerId)) return false;
            soundWorld ??= new SoundWorld();
            return soundWorld.WaterState(player, out _) == 0;
        }

        public bool TryFindSoundCanPickup(Vector3 position, out int index)
        {
            index = -1;
            float best = SoundCanConfig.PickupRange * SoundCanConfig.PickupRange;
            for (int i = 0; i < soundCanPickups.Count && i < SoundCanConfig.PickupPositions.Length; i++)
            {
                float distance = (SoundCanConfig.PickupPositions[i] - position).sqrMagnitude;
                if (!soundCanPickups[i] || distance > best) continue;
                best = distance; index = i;
            }
            return index >= 0;
        }

        [Server]
        public bool TryPickupSoundCan(int playerId, int index)
        {
            if (!CanUseSoundCan(playerId, out var player) || soundCans.ContainsKey(playerId) ||
                index < 0 || index >= soundCanPickups.Count || index >= SoundCanConfig.PickupPositions.Length ||
                !soundCanPickups[index] || Vector3.Distance(player.transform.position,
                    SoundCanConfig.PickupPositions[index]) > SoundCanConfig.PickupRange ||
                !DecoyPathClear(player.transform.position + Vector3.up * .5f,
                    SoundCanConfig.PickupPositions[index] + Vector3.up * .5f)) return false;
            var can = new SoundCan { OwnerId = playerId, Id = ++nextSoundCanId, Stage = SoundCanStage.Empty };
            soundCans.Add(playerId, can);
            soundCanPickups[index] = false;
            SendSoundCanState(can);
            return true;
        }

        [Server]
        public bool TryBeginSoundCanRecording(int playerId)
        {
            if (!CanUseSoundCan(playerId, out _) || !soundCans.TryGetValue(playerId, out var can) ||
                can.Stage != SoundCanStage.Empty) return false;
            can.Stage = SoundCanStage.Recording;
            can.Token = ++nextSoundCanToken;
            can.Audio = new byte[SoundCanConfig.Bytes];
            can.Frames = 0;
            can.Started = Time.time;
            can.NextProgress = 0;
            SendSoundCanState(can);
            return true;
        }

        [Server]
        public bool TryAppendSoundCanFrame(int playerId, int token, int index, byte[] packet)
        {
            if (!CanUseSoundCan(playerId, out _) || !soundCans.TryGetValue(playerId, out var can) ||
                can.Stage != SoundCanStage.Recording || can.Token != token || index != can.Frames ||
                can.Frames >= SoundCanConfig.Frames || packet == null || packet.Length != VoiceCodec.SamplesPerPacket ||
                Time.time - can.Started > SoundCanConfig.RecordingTimeout) return false;
            Buffer.BlockCopy(packet, 0, can.Audio, can.Frames * VoiceCodec.SamplesPerPacket, packet.Length);
            can.Frames++;
            // A client cannot finish instantly by submitting all frames in one burst.
            if (can.Frames == SoundCanConfig.Frames && Time.time - can.Started >= SoundCanConfig.RecordingSeconds)
                CompleteSoundCanRecording(can);
            return true;
        }

        [Server]
        public bool TryCancelSoundCanRecording(int playerId, int token)
        {
            if (!soundCans.TryGetValue(playerId, out var can) || can.Stage != SoundCanStage.Recording || can.Token != token) return false;
            DiscardSoundCanAudio(can);
            can.Stage = SoundCanStage.Empty;
            SendSoundCanState(can);
            return true;
        }

        private void CompleteSoundCanRecording(SoundCan can)
        {
            can.Stage = SoundCanStage.Recorded;
            SendSoundCanState(can);
        }

        [Server]
        public bool TryPlaceSoundCan(int playerId)
        {
            if (!CanUseSoundCan(playerId, out var player) || !soundCans.TryGetValue(playerId, out var can) ||
                can.Stage != SoundCanStage.Recorded || can.Frames != SoundCanConfig.Frames) return false;
            Vector3 near = player.transform.position + player.transform.forward * SoundCanConfig.PlacementForward;
            RaycastHit best = default;
            float distance = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(near + Vector3.up * .8f, Vector3.down, 2f,
                ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < .7f || hit.collider.GetComponentInParent<NetworkPlayer>() != null || hit.distance >= distance) continue;
                best = hit; distance = hit.distance;
            }
            if (distance == float.MaxValue || Vector3.Distance(player.transform.position, best.point) > 1.5f ||
                !DecoyPathClear(player.transform.position + Vector3.up * .3f, best.point + Vector3.up * .3f)) return false;
            can.Position = best.point + Vector3.up * .1f;
            can.Stage = SoundCanStage.Placing;
            can.Started = Time.time;
            can.NextProgress = Time.time;
            can.PlacementStart = player.transform.position;
            can.PlacementHealth = runnerHalves.TryGetValue(playerId, out int health) ? health : FullHalves;
            can.WasCrouching = player.IsCrouching;
            RemoveFromWork(playerId);
            player.SetTreatmentLocked(true);
            player.SetCrouching(true);
            player.SetSoundCanPlacement(true, can.Position, 0);
            SendSoundCanState(can);
            return true;
        }

        [Server]
        public bool TryCancelSoundCanPlacement(int playerId)
        {
            if (!soundCans.TryGetValue(playerId, out var can) || can.Stage != SoundCanStage.Placing) return false;
            FinishSoundCanPlacement(can, false);
            return true;
        }

        private void FinishSoundCanPlacement(SoundCan can, bool completed)
        {
            if (NetworkPlayer.TryGet(can.OwnerId, out var owner))
            {
                owner.SetSoundCanPlacement(false, can.Position, completed ? 1 : 0);
                owner.SetTreatmentLocked(false);
                owner.SetCrouching(owner.Status == PlayerStatus.Alive && can.WasCrouching);
            }
            can.Stage = completed ? SoundCanStage.Placed : SoundCanStage.Recorded;
            if (completed) soundCanPlacements.Add(new SoundCanPlacement { Id = can.Id, Position = can.Position });
            SendSoundCanState(can);
        }

        [Server]
        public bool TryPlaySoundCan(int playerId)
        {
            if (!CanUseSoundCan(playerId, out _) || !soundCans.TryGetValue(playerId, out var can) ||
                can.Stage != SoundCanStage.Placed) return false;
            can.Stage = SoundCanStage.Playing;
            can.Started = Time.time;
            can.NextFrame = 0;
            SendSoundCanState(can);
            soundWorld ??= new SoundWorld();
            foreach (var listener in NetworkPlayer.All)
            {
                if (listener.Status != PlayerStatus.Alive || listener.IsEcho ||
                    !soundWorld.Hear(can.Position, listener, SoundCanConfig.PulseRadius,
                        out var point, out _, out bool blocked) || blocked) continue;
                bool seeker = listener.Role == PlayerRole.Seeker;
                SoundCanPulseTarget(listener.Owner, point, SoundCanConfig.PulseRadius * (seeker ? 1.2f : 1f),
                    SoundCanConfig.PulseSeconds * (seeker ? 1.5f : 1f));
            }
            return true;
        }

        private void TickSoundCansServer()
        {
            soundCanOwners.Clear();
            soundCanOwners.AddRange(soundCans.Keys);
            foreach (int id in soundCanOwners)
            {
                var can = soundCans[id];
                if (!DecoyRunning || !NetworkPlayer.TryGet(id, out var owner) || owner.Status != PlayerStatus.Alive || owner.Role != PlayerRole.Runner)
                { RemoveSoundCan(id); continue; }
                if (can.Stage == SoundCanStage.Placing)
                {
                    int health = runnerHalves.TryGetValue(id, out int h) ? h : FullHalves;
                    if (!owner.IsTreatmentLocked || Vector3.Distance(owner.transform.position, can.PlacementStart) > SoundCanConfig.PlacementMoveTolerance ||
                        health != can.PlacementHealth || soundWorld.WaterState(owner, out _) != 0 ||
                        !DecoyPathClear(owner.transform.position + Vector3.up * .3f, can.Position + Vector3.up * .2f))
                    { FinishSoundCanPlacement(can, false); continue; }
                    float progress = Mathf.Clamp01((Time.time - can.Started) / SoundCanConfig.PlacementSeconds);
                    if (Time.time >= can.NextProgress)
                    { can.NextProgress = Time.time + .05f; owner.SetSoundCanPlacement(true, can.Position, progress); SendSoundCanState(can); }
                    if (progress >= 1) FinishSoundCanPlacement(can, true);
                    continue;
                }
                if (can.Stage == SoundCanStage.Recording)
                {
                    if (!CanUseSoundCan(id, out _) || Time.time - can.Started > SoundCanConfig.RecordingTimeout)
                    { TryCancelSoundCanRecording(id, can.Token); continue; }
                    if (can.Frames == SoundCanConfig.Frames && Time.time - can.Started >= SoundCanConfig.RecordingSeconds)
                    { CompleteSoundCanRecording(can); continue; }
                    if (Time.time >= can.NextProgress)
                    { can.NextProgress = Time.time + .1f; SendSoundCanState(can); }
                }
                if (can.Stage != SoundCanStage.Playing) continue;
                int due = Mathf.Min(SoundCanConfig.Frames - 1, Mathf.FloorToInt((Time.time - can.Started) / SoundCanConfig.FrameSeconds));
                while (can.NextFrame <= due)
                {
                    int frame = can.NextFrame++;
                    var packet = new byte[VoiceCodec.SamplesPerPacket];
                    Buffer.BlockCopy(can.Audio, frame * packet.Length, packet, 0, packet.Length);
                    foreach (var listener in NetworkPlayer.All)
                    {
                        if (listener.Status != PlayerStatus.Alive || listener.IsEcho ||
                            !soundWorld.Hear(can.Position, listener, SoundCanConfig.PulseRadius,
                                out var point, out float gain, out bool blocked)) continue;
                        SoundCanFrameTarget(listener.Owner, can.Id, frame, packet, point, gain, blocked);
                    }
                }
                if (Time.time - can.Started >= SoundCanConfig.RecordingSeconds) RemoveSoundCan(id, false);
            }
        }

        private void SendSoundCanState(SoundCan can)
        {
            if (NetworkPlayer.TryGet(can.OwnerId, out var player))
                player.SendSoundCanState(can.Stage, can.Token, can.Stage == SoundCanStage.Placing
                    ? Mathf.Clamp01((Time.time - can.Started) / SoundCanConfig.PlacementSeconds)
                    : can.Frames / (float)SoundCanConfig.Frames);
        }

        private static void DiscardSoundCanAudio(SoundCan can)
        {
            if (can.Audio != null) Array.Clear(can.Audio, 0, can.Audio.Length);
            can.Audio = null; can.Frames = 0;
        }

        private void RemoveSoundCan(int ownerId, bool stopPlayback = true)
        {
            if (soundCans.TryGetValue(ownerId, out var placing) && placing.Stage == SoundCanStage.Placing)
                FinishSoundCanPlacement(placing, false);
            if (!soundCans.Remove(ownerId, out var can)) return;
            DiscardSoundCanAudio(can);
            for (int i = soundCanPlacements.Count - 1; i >= 0; i--)
                if (soundCanPlacements[i].Id == can.Id) soundCanPlacements.RemoveAt(i);
            if (stopPlayback && IsServerInitialized) StopSoundCanObservers(can.Id);
            if (NetworkPlayer.TryGet(ownerId, out var player)) player.SendSoundCanState(SoundCanStage.None, 0, 0);
        }

        private void ResetSoundCansServer(bool available)
        {
            foreach (var can in soundCans.Values)
            {
                if (can.Stage == SoundCanStage.Placing) FinishSoundCanPlacement(can, false);
                DiscardSoundCanAudio(can);
            }
            soundCans.Clear(); soundCanPlacements.Clear(); soundCanPickups.Clear();
            for (int i = 0; i < SoundCanConfig.PickupPositions.Length; i++) soundCanPickups.Add(available && SoundMap);
            foreach (var player in NetworkPlayer.All) player.SendSoundCanState(SoundCanStage.None, 0, 0);
            if (IsServerInitialized) StopSoundCanObservers(-1);
        }

        [TargetRpc]
        private void SoundCanFrameTarget(NetworkConnection connection, int id, int frame, byte[] packet,
            Vector3 point, float gain, bool blocked)
        {
            if (NetworkPlayer.Local == null || NetworkPlayer.Local.Status != PlayerStatus.Alive) return;
            if (recordedPlayback == null) recordedPlayback = gameObject.AddComponent<RecordedSoundPlayback>();
            recordedPlayback.Receive(id, frame, packet, point, gain, blocked);
            try { DirectionHints.OnSoundCan(id, point, blocked); } catch (Exception exception) { DirectionHints.ReportFault("SoundCanFrameTarget", exception); }
        }

        [TargetRpc]
        private void SoundCanPulseTarget(NetworkConnection connection, Vector3 point, float radius, float duration)
        {
            if (NetworkPlayer.Local != null)
                NetworkPlayer.Local.GetComponentInChildren<WalkingFootRings>()?.EmitSoundPulse(point, radius, duration);
        }

        [ObserversRpc]
        private void StopSoundCanObservers(int id)
        {
            if (recordedPlayback != null) recordedPlayback.Stop(id);
        }

        private GameObject CreateSoundCanView()
        {
            var root = new GameObject("소리통");
            root.transform.SetParent(transform, false);
            CreateBandageCube(root.transform, "녹음기", new Vector3(.16f, .18f, .09f), Vector3.zero, soundCanMaterial);
            CreateBandageCube(root.transform, "마이크", new Vector3(.1f, .04f, .1f), Vector3.up * .12f, soundCanMaterial);
            return root;
        }

        private void UpdateSoundCanViews()
        {
            if (!SoundMap) return;
            if (soundCanMaterial == null)
            {
                var shader = Shader.Find("PoolV12/BlockoutSurface") ?? Shader.Find("MARCO/WorldSpaceTexturedSurface");
                if (shader == null) return;
                soundCanMaterial = CreateBandageMaterial(shader, new Color(1f, .72f, .3f));
            }
            while (soundCanPickupViews.Count < SoundCanConfig.PickupPositions.Length) soundCanPickupViews.Add(CreateSoundCanView());
            for (int i = 0; i < soundCanPickupViews.Count; i++)
            {
                soundCanPickupViews[i].SetActive(i < soundCanPickups.Count && soundCanPickups[i]);
                soundCanPickupViews[i].transform.position = SoundCanConfig.PickupPositions[i] + Vector3.up * .1f;
            }
            foreach (var placement in soundCanPlacements)
            {
                if (!soundCanPlacedViews.TryGetValue(placement.Id, out var view))
                    soundCanPlacedViews[placement.Id] = view = CreateSoundCanView();
                view.transform.position = placement.Position;
            }
            staleSoundCanViews.Clear();
            foreach (var pair in soundCanPlacedViews)
            {
                bool found = false;
                foreach (var placement in soundCanPlacements) if (placement.Id == pair.Key) { found = true; break; }
                if (!found) staleSoundCanViews.Add(pair.Key);
            }
            foreach (int id in staleSoundCanViews) { Destroy(soundCanPlacedViews[id]); soundCanPlacedViews.Remove(id); }
        }

        private void ClearSoundCanViews()
        {
            foreach (var view in soundCanPickupViews) if (view != null) Destroy(view);
            foreach (var view in soundCanPlacedViews.Values) if (view != null) Destroy(view);
            soundCanPickupViews.Clear(); soundCanPlacedViews.Clear();
            if (recordedPlayback != null) Destroy(recordedPlayback);
            recordedPlayback = null;
            if (soundCanMaterial != null) Destroy(soundCanMaterial);
            soundCanMaterial = null;
        }
    }
}
