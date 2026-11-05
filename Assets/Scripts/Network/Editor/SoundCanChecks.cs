using System;
using System.Collections;
using System.Reflection;
using Marco.Core.WinCondition;
using Marco.Network.Audio;
using Marco.Network.Character;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Voice;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Marco.Network.Vision;

namespace Marco.Network.Editor
{
    [InitializeOnLoad]
    public static class SoundCanChecks
    {
        private const string Pending = "Marco.SoundCanChecks";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static int phase, token;
        private static double deadline, until;
        private static byte[] stored, expectedPacket;
        private static Vector3 handBefore;
        private static Quaternion fingerDuring;
        private static float maxFingerDelta;
        private static readonly Vector3 TestPosition = new(2000, 1.05f, 2000);

        static SoundCanChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
        private static void Subscribe()
        { deadline = EditorApplication.timeSinceStartup + 110; EditorApplication.update -= Tick; EditorApplication.update += Tick; }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new Exception("Separate batch editor only");
            EditorSceneManager.OpenScene("Assets/Scenes/Network/NetworkPool_v1_2.unity", OpenSceneMode.Single);
            SessionState.SetBool(Pending, true); Subscribe(); EditorApplication.EnterPlaymode();
        }
        private static object Field(object target, string name) => target.GetType().GetField(name, Flags).GetValue(target);
        private static object Can(NetworkRound round, int id) => ((IDictionary)Field(round, "soundCans"))[id];
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Cleared(byte[] bytes) { foreach (byte value in bytes) Check(value == 0, "Recording bytes survived cleanup"); }

        private static int Placements(NetworkRound round)
        { var value = Field(round, "soundCanPlacements"); return (int)value.GetType().GetProperty("Count").GetValue(value); }

        private static void EvaluatePose(NetworkPlayer player)
        {
            // Editor update may run before the frame's LateUpdate; sample the final rendered pose.
            typeof(CharacterMotion).GetMethod("LateUpdate", Flags).Invoke(player.GetComponent<CharacterMotion>(), null);
            typeof(FirstPersonBody).GetMethod("LateUpdate", Flags).Invoke(player.GetComponent<FirstPersonBody>(), null);
        }

        private static void Capture(NetworkPlayer player, string name)
        {
            string directory = Environment.GetEnvironmentVariable("MARCO_SOUND_CAN_PREVIEW_DIR");
            if (string.IsNullOrEmpty(directory) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            EvaluatePose(player);
            System.IO.Directory.CreateDirectory(directory);
            var camera = player.GetComponentInChildren<Camera>();
            var vision = camera.GetComponent<MicrophoneVisionController>();
            if (vision != null) vision.SetDevelopmentFullVision(true);
            var texture = RenderTexture.GetTemporary(960, 600, 24);
            var previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
                RenderTexture.active = texture;
                var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); image.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(texture); }
        }

        private static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("SoundCan timeout phase " + phase);
                if (!EditorApplication.isPlaying) return;
                var round = NetworkRound.Instance;
                var player = NetworkPlayer.Local;
                if (phase == 0)
                {
                    var lan = UnityEngine.Object.FindAnyObjectByType<LanConnector>();
                    if (lan == null) return;
                    lan.Host(); phase = 1; return;
                }
                if (phase == 1)
                {
                    if (round == null || player == null || !round.IsClientInitialized) return;
                    round.StartRound(); until = now + 1; phase = 2; return;
                }
                if (phase == 2 && now >= until)
                { player.SetRole(PlayerRole.Runner); until = now + .5; phase = 3; return; }
                if (phase == 3 && now >= until)
                {
                    Check(!round.TryPickupSoundCan(player.PlayerId, 0), "Briefing pickup accepted");
                    ((RoundStartSchedule)Field(round, "startSchedule")).Tick(40);
                    var interactor = player.GetComponent<PlayerInteractor>();
                    interactor.enabled = false;
                    // Feed deterministic microphone frames through the real capture->ServerRpc path.
                    typeof(PlayerInteractor).GetMethod("StopSoundCanCapture", Flags).Invoke(interactor, null);
                    player.GetComponent<PlayerMovement>().enabled = false;
                    Check(!round.TryPickupSoundCan(player.PlayerId, -1), "Invalid pickup accepted");
                    Check(!round.TryPickupSoundCan(player.PlayerId, 999), "Invalid pickup accepted");
                    Check(!round.TryPickupSoundCan(player.PlayerId, 0), "Remote pickup accepted");
                    player.GetComponent<PlayerMovement>().Teleport(SoundCanConfig.PickupPositions[0] + Vector3.up * .05f);
                    Physics.SyncTransforms();
                    Check(round.TryPickupSoundCan(player.PlayerId, 0), "Pickup failed");
                    Check(!round.TryPickupSoundCan(player.PlayerId, 0), "Duplicate pickup accepted");
                    Check(!round.TryPlaceSoundCan(player.PlayerId), "Unrecorded device installed");
                    Check(round.TryBeginSoundCanRecording(player.PlayerId), "Record start failed");
                    var can = Can(round, player.PlayerId);
                    token = (int)Field(can, "Token");
                    stored = (byte[])Field(can, "Audio");
                    Check(!round.TryAppendSoundCanFrame(player.PlayerId, token, 0, new byte[1]), "Malformed packet accepted");
                    Check(!round.TryAppendSoundCanFrame(player.PlayerId, token + 1, 0, new byte[320]), "Forged token accepted");
                    Check(!round.TryAppendSoundCanFrame(player.PlayerId, token, 1, new byte[320]), "Out of order frame accepted");
                    Check(round.TryAppendSoundCanFrame(player.PlayerId, token, 0, new byte[320]), "First frame failed");
                    Check(!round.TryAppendSoundCanFrame(player.PlayerId, token, 0, new byte[320]), "Duplicate frame accepted");
                    Check(round.TryCancelSoundCanRecording(player.PlayerId, token), "Cancel failed");
                    Cleared(stored);
                    Check(round.TryBeginSoundCanRecording(player.PlayerId), "Re-record failed");
                    Check(!round.TryAppendSoundCanFrame(player.PlayerId, token, 0, new byte[320]), "Old-session frame accepted");
                    token = (int)Field(Can(round, player.PlayerId), "Token");
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    floor.transform.position = TestPosition + new Vector3(0, -1.55f, 0);
                    floor.transform.localScale = new Vector3(40, 1, 40);
                    player.GetComponent<PlayerMovement>().Teleport(TestPosition);
                    Physics.SyncTransforms();
                    until = now + .4; phase = 4; return;
                }
                if (phase == 4 && now >= until)
                {
                    Check(player.SoundCanStage == SoundCanStage.Recording && player.SoundCanToken == token, "Owner state RPC failed");
                    var samples = new float[VoiceCodec.InputSamplesPerPacket];
                    for (int i = 0; i < samples.Length; i++) samples[i] = .25f * Mathf.Sin(i * 2f * Mathf.PI * 440f / 48000f);
                    expectedPacket = VoiceCodec.Encode(samples);
                    var method = typeof(PlayerInteractor).GetMethod("RecordSoundCanSamples", Flags);
                    for (int i = 0; i < SoundCanConfig.Frames; i++) method.Invoke(player.GetComponent<PlayerInteractor>(), new object[] { samples });
                    Check(!round.TryPlaceSoundCan(player.PlayerId), "Client bypassed 3-second recording gate");
                    until = now + 3.3; phase = 5; return;
                }
                if (phase == 5 && now >= until)
                {
                    var can = Can(round, player.PlayerId);
                    Check(player.SoundCanStage == SoundCanStage.Recorded, "3-second recording did not complete");
                    Check((int)Field(can, "Frames") == 150, "Wrong frame count");
                    stored = (byte[])Field(can, "Audio");
                    Check(stored.Length == 48000, "Wrong recording byte count");
                    for (int i = 0; i < expectedPacket.Length; i++) Check(stored[i] == expectedPacket[i], "Capture data changed");
                    player.GetComponent<PlayerMovement>().Teleport(TestPosition + Vector3.right * 100);
                    Physics.SyncTransforms();
                    Check(!round.TryPlaceSoundCan(player.PlayerId), "Placement without floor accepted");
                    player.GetComponent<PlayerMovement>().Teleport(TestPosition);
                    Physics.SyncTransforms();
                    var body = player.GetComponent<CharacterBody>();
                    handBefore = body.ResolveBone(HumanBodyBones.LeftHand).position;
                    Check(round.TryPlaceSoundCan(player.PlayerId), "Floor installation failed");
                    Check(!round.TryPlaceSoundCan(player.PlayerId), "Duplicate installation accepted");
                    Check(player.IsPlacingSoundCan && player.IsCrouching && player.IsTreatmentLocked, "Placement did not kneel/lock");
                    Check(Placements(round) == 0, "Device appeared before animation finished");
                    Check(!round.TryPlaySoundCan(player.PlayerId), "Replay accepted during placement");
                    Check(round.TryCancelSoundCanPlacement(player.PlayerId), "Placement cancellation failed");
                    Check(!player.IsPlacingSoundCan && !player.IsTreatmentLocked && !player.IsCrouching, "Cancel failed to restore standing state");
                    Check((SoundCanStage)Field(can, "Stage") == SoundCanStage.Recorded && ReferenceEquals(stored, Field(can, "Audio")), "Cancel consumed recording");
                    player.SetCrouching(true);
                    Check(round.TryPlaceSoundCan(player.PlayerId), "Crouched placement failed");
                    Check(round.TryCancelSoundCanPlacement(player.PlayerId) && player.IsCrouching, "Cancel lost prior crouch");
                    player.SetCrouching(false);
                    player.GetComponent<PlayerMovement>().enabled = true;
                    var light = new GameObject("Placement verification light").AddComponent<Light>();
                    light.type = LightType.Directional; light.intensity = 2;
                    light.transform.rotation = Quaternion.Euler(45, -30, 0);
                    Capture(player, "01-standing");
                    Check(round.TryPlaceSoundCan(player.PlayerId), "Animated placement failed");
                    until = EditorApplication.timeSinceStartup + .55; phase = 50; return;
                }
                if (phase == 50 && now >= until)
                {
                    EvaluatePose(player);
                    Check(player.IsPlacingSoundCan && Placements(round) == 0, "Placement finished too early");
                    var motion = player.GetComponent<CharacterMotion>();
                    Check(motion.PlacementPoseWeight > .9f, "Placement pose not blended in");
                    var body = player.GetComponent<CharacterBody>();
                    Check(Vector3.Distance(handBefore, body.ResolveBone(HumanBodyBones.LeftHand).position) > .1f, "Hands did not reach down");
                    var finger = body.ResolveBone(HumanBodyBones.LeftIndexProximal);
                    Check(finger != null, "Runner has no finger articulation");
                    fingerDuring = finger.rotation;
                    maxFingerDelta = 0;
                    Debug.Log($"[SoundCanPose] lower progress={player.SoundCanPlacementProgress:F3}, pose={Field(motion, "placementProgress")}, hand={body.ResolveBone(HumanBodyBones.LeftHand).position}, camera={player.GetComponentInChildren<Camera>().transform.position}");
                    Capture(player, "02-kneel-and-lower");
                    until = EditorApplication.timeSinceStartup + .65; phase = 51; return;
                }
                if (phase == 51)
                {
                    EvaluatePose(player);
                    Check(player.IsPlacingSoundCan, "Placement ended before hand work");
                    var finger = player.GetComponent<CharacterBody>().ResolveBone(HumanBodyBones.LeftIndexProximal);
                    maxFingerDelta = Mathf.Max(maxFingerDelta, Quaternion.Angle(fingerDuring, finger.rotation));
                    if (now < until) return;
                    Debug.Log($"[SoundCanPose] work progress={player.SoundCanPlacementProgress:F3}, maxFingerDelta={maxFingerDelta:F2}");
                    Check(maxFingerDelta > 1f, "Fingers did not fidget");
                    Capture(player, "03-hand-work");
                    until = EditorApplication.timeSinceStartup + 1.1; phase = 52; return;
                }
                if (phase == 52 && now >= until)
                {
                    var can = Can(round, player.PlayerId);
                    Check(player.SoundCanStage == SoundCanStage.Placed && Placements(round) == 1, "Animated placement did not commit");
                    Check(!player.IsPlacingSoundCan && !player.IsTreatmentLocked && !player.IsCrouching, "Completion left actor locked/crouched");
                    Capture(player, "04-installed");
                    player.GetComponent<PlayerMovement>().enabled = false;
                    Vector3 position = (Vector3)Field(can, "Position");
                    Check(Mathf.Abs(position.y - .1f) < .01f, "Device not placed on floor");
                    player.GetComponent<PlayerMovement>().Teleport(position + new Vector3(0, 0, 5));
                    Physics.SyncTransforms();
                    Check(round.TryPlaySoundCan(player.PlayerId), "Remote replay failed");
                    Check(!round.TryPlaySoundCan(player.PlayerId), "Replay spam accepted");
                    until = now + .5; phase = 6; return;
                }
                if (phase == 6 && now >= until)
                {
                    var playback = (RecordedSoundPlayback)Field(round, "recordedPlayback");
                    Check(playback != null, "No audible playback received");
                    var streams = (IDictionary)Field(playback, "playing");
                    Check(streams.Count == 1, "Expected one recording stream");
                    foreach (object stream in streams.Values)
                    {
                        var clip = (AudioClip)Field(stream, "Clip");
                        Check(clip.samples == 48000 && clip.frequency == 16000, "Replay clip format mismatch");
                        var audio = new float[320]; clip.GetData(audio, 0);
                        double inputEnergy = 0, outputEnergy = 0, correlation = 0;
                        for (int i = 0; i < audio.Length; i++)
                        {
                            float input = VoiceCodec.Decode(expectedPacket[i]);
                            inputEnergy += input * input; outputEnergy += audio[i] * audio[i]; correlation += input * audio[i];
                            Check(Mathf.Abs(audio[i]) <= 1f, "Amplified replay clipped outside PCM range");
                        }
                        Check(outputEnergy > inputEnergy * 4, "Quiet recording did not receive at least 2x RMS amplification");
                        Check(correlation / Math.Sqrt(inputEnergy * outputEnergy) > .98, "Amplified replay lost the recorded waveform");
                    }
                    var can = Can(round, player.PlayerId);
                    player.GetComponent<PlayerMovement>().Teleport((Vector3)Field(can, "Position") + Vector3.right * 20);
                    Physics.SyncTransforms();
                    until = now + .5; phase = 60; return;
                }
                if (phase == 60 && now >= until)
                {
                    var playback = (RecordedSoundPlayback)Field(round, "recordedPlayback");
                    Check(((IDictionary)Field(playback, "playing")).Count == 0, "Out-of-range listener kept receiving voice");
                    var can = Can(round, player.PlayerId);
                    player.GetComponent<PlayerMovement>().Teleport((Vector3)Field(can, "Position") + Vector3.forward * 5);
                    Physics.SyncTransforms();
                    until = now + 3; phase = 7; return;
                }
                if (phase == 7 && now >= until)
                {
                    Check(((IDictionary)Field(round, "soundCans")).Count == 0, "Replay did not consume device");
                    Check(player.SoundCanStage == SoundCanStage.None, "Owner remote not cleared");
                    Cleared(stored);
                    // The map now has one recorder; reset its availability for the cleanup trial.
                    typeof(NetworkRound).GetMethod("ResetSoundCansServer", Flags).Invoke(round, new object[] { true });
                    player.GetComponent<PlayerMovement>().Teleport(SoundCanConfig.PickupPositions[0] + Vector3.up * .05f);
                    Physics.SyncTransforms();
                    Check(round.TryPickupSoundCan(player.PlayerId, 0), "Second pickup failed");
                    Check(round.TryBeginSoundCanRecording(player.PlayerId), "Second recording failed");
                    var can = Can(round, player.PlayerId);
                    token = (int)Field(can, "Token");
                    Check(round.TryAppendSoundCanFrame(player.PlayerId, token, 0, expectedPacket), "Second recording frame failed");
                    stored = (byte[])Field(can, "Audio");
                    player.SetStatus(PlayerStatus.Tagged);
                    until = now + .4; phase = 8; return;
                }
                if (phase == 8 && now >= until)
                {
                    Check(((IDictionary)Field(round, "soundCans")).Count == 0, "Death retained recording");
                    Cleared(stored);
                    typeof(NetworkRound).GetMethod("ResetRound", Flags).Invoke(round, null);
                    Check(!round.TryBeginSoundCanRecording(player.PlayerId), "Lobby recording accepted");
                    var placements = Field(round, "soundCanPlacements");
                    Check((int)placements.GetType().GetProperty("Count").GetValue(placements) == 0, "Reset retained installed objects");
                    Debug.Log("[SoundCanChecks] PASS: 2s kneeling installation, hand reach/finger motion, delayed spawn, cancel/crouch restoration; capture->RPC->3s recording->remote replay, waveform/range, validation and memory cleanup.");
                    Finish(0);
                }
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(1); }
        }

        private static void Finish(int code)
        { SessionState.SetBool(Pending, false); EditorApplication.update -= Tick; EditorApplication.Exit(code); }
    }
}
