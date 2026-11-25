using FishNet.Object;
using Marco.Core.WinCondition;
using Marco.Network.Round;
using Marco.Network.Vision;
using Marco.Network.Voice;
using UnityEngine;

namespace Marco.Network.Player
{
    public sealed partial class PlayerInteractor
    {
        private MicrophoneVisionController soundCanMicrophone;
        private int soundCanCaptureToken = -1, soundCanCaptureFrame;
        private bool soundCanCancelPending;
        public string SoundCanHint { get; private set; } = "";

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;
            soundCanMicrophone = GetComponentInChildren<MicrophoneVisionController>(true);
            if (soundCanMicrophone != null) soundCanMicrophone.MicrophoneSamplesReady += RecordSoundCanSamples;
        }

        private void StopSoundCanCapture()
        {
            if (soundCanMicrophone != null) soundCanMicrophone.MicrophoneSamplesReady -= RecordSoundCanSamples;
            soundCanMicrophone = null;
            soundCanCaptureToken = -1; soundCanCaptureFrame = 0; soundCanCancelPending = false;
        }

        private void CancelSoundCanCapture()
        {
            if (!IsOwner || !IsClientInitialized || player == null || soundCanCancelPending) return;
            if (player.IsPlacingSoundCan || player.SoundCanStage == SoundCanStage.Placing)
            { soundCanCancelPending = true; CancelSoundCanPlacementServer(); return; }
            if (player.SoundCanStage != SoundCanStage.Recording) return;
            soundCanCancelPending = true;
            CancelSoundCanServer(player.SoundCanToken);
        }

        private void TickSoundCanInput(NetworkRound round, bool locked)
        {
            bool eligible = locked && selectedItemSlot == 4 && player.Role == PlayerRole.Runner &&
                player.Status == PlayerStatus.Alive && !workingBandage && !workingTreatment &&
                workingValve == null && workingDrain == null && !player.HasHealingAction &&
                !round.IsStartLocked(player.Role) && round.Mode != RoundMode.NotStarted &&
                round.Result == RoundResult.InProgress && !GetComponent<PlayerMovement>().IsSwimming;
            if (player.IsPlacingSoundCan || player.SoundCanStage == SoundCanStage.Placing)
            {
                if (!eligible || Input.GetKeyDown(KeyCode.G)) CancelSoundCanCapture();
                return;
            }
            if (player.SoundCanStage == SoundCanStage.Recording)
            {
                if (!eligible || Input.GetKey(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.G)) CancelSoundCanCapture();
                return;
            }
            soundCanCancelPending = false;
            if (!eligible || player.IsTreatmentLocked || !Input.GetKeyDown(KeyCode.G)) return;
            SoundCanHint = "";
            switch (player.SoundCanStage)
            {
                case SoundCanStage.Empty:
                    if (Input.GetKey(KeyCode.LeftAlt) || soundCanMicrophone == null || !soundCanMicrophone.HasMicrophoneInput)
                    { SoundCanHint = "마이크를 켜고 음소거를 해제해 주세요"; return; }
                    BeginSoundCanServer(); break;
                case SoundCanStage.Recorded: PlaceSoundCanServer(); break;
                case SoundCanStage.Placed: PlaySoundCanServer(); break;
            }
        }

        // The microphone raises this for every unmuted 20 ms frame, including silence.
        // Voice/VAD forwarding is unchanged; recording does not hide live speech from nearby players.
        private void RecordSoundCanSamples(float[] samples)
        {
            if (!IsOwner || player.SoundCanStage != SoundCanStage.Recording || soundCanCancelPending) return;
            if (player.SoundCanToken != soundCanCaptureToken)
            { soundCanCaptureToken = player.SoundCanToken; soundCanCaptureFrame = 0; }
            if (soundCanCaptureFrame >= SoundCanConfig.Frames) return;
            var packet = VoiceCodec.Encode(samples);
            if (packet == null) return;
            SoundCanFrameServer(soundCanCaptureToken, soundCanCaptureFrame++, packet);
        }

        [ServerRpc]
        private void BeginSoundCanServer() => NetworkRound.Instance?.TryBeginSoundCanRecording(OwnerId);
        [ServerRpc]
        private void SoundCanFrameServer(int token, int frame, byte[] packet) =>
            NetworkRound.Instance?.TryAppendSoundCanFrame(OwnerId, token, frame, packet);
        [ServerRpc]
        private void CancelSoundCanServer(int token) => NetworkRound.Instance?.TryCancelSoundCanRecording(OwnerId, token);
        [ServerRpc]
        private void PlaceSoundCanServer() => NetworkRound.Instance?.TryPlaceSoundCan(OwnerId);
        [ServerRpc]
        private void CancelSoundCanPlacementServer() => NetworkRound.Instance?.TryCancelSoundCanPlacement(OwnerId);
        [ServerRpc]
        private void PlaySoundCanServer() => NetworkRound.Instance?.TryPlaySoundCan(OwnerId);
        [ServerRpc]
        private void PickupSoundCanServer(int index) => NetworkRound.Instance?.TryPickupSoundCan(OwnerId, index);
    }
}
