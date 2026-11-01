using System;
using System.IO;
using System.Reflection;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEditor;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Exercise the real client presentation against a local host in a disposable Play session.
    [InitializeOnLoad]
    public static class WaterReactionChecks
    {
        const string Pending = "Marco.WaterReactionChecks";
        static int phase, baseline;
        static double deadline, until;
        static float previous;
        static bool heardEntry;
        static readonly MethodInfo Present = typeof(PlayerMovement).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        static WaterReactionChecks()
        {
            if (SessionState.GetBool(Pending, false)) Subscribe();
        }
        static void Subscribe()
        {
            deadline = EditorApplication.timeSinceStartup + 75;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        [MenuItem("Tools/MARCO/Validate Player Water Reactions")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "NetworkPool_v1_2")
            { Debug.LogWarning("Open NetworkPool_v1_2 in Edit mode first."); return; }
            phase = 0; heardEntry = false;
            SessionState.SetBool(Pending, true);
            Subscribe(); EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Timeout at phase " + phase);
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
                if (player == null) return;
                var movement = player.GetComponent<PlayerMovement>();
                var controller = player.GetComponent<CharacterController>();
                if (phase == 2)
                {
                    if (now < until) return;
                    movement.enabled = false;
                    player.GetComponent<PlayerInteractor>().enabled = false;
                    controller.height = .7f; controller.center = Vector3.zero;
                    movement.Teleport(new Vector3(20, .6f, 47));
                    Physics.SyncTransforms(); Present.Invoke(movement, null);
                    Require(Count == 0, "Spawn should not emit a splash");
                    previous = Time.time; until = now + 1; phase = 3; return;
                }
                float dt = Mathf.Clamp(Time.time - previous, 0, .05f); previous = Time.time;
                if (phase == 3) controller.Move(Vector3.down * dt);
                if (phase == 4 || phase == 7) controller.Move(Vector3.right * dt);
                if (phase == 6) controller.Move(Vector3.down * dt * 2);
                Physics.SyncTransforms(); Present.Invoke(movement, null);
                foreach (var sound in round.GetComponentsInChildren<AudioSource>())
                    if (sound.clip != null && sound.clip.name == "WaterEntry" && sound.volume > 0) heardEntry = true;
                if (now < until) return;
                switch (phase)
                {
                    case 3:
                        Require(Count > 0, "Entering water did not emit a ring");
                        Require(heardEntry, "Entry audio was not played");
                        Require(UnityEngine.Object.FindAnyObjectByType<Marco.Network.Audio.PoolWaterEffects>() != null, "Spray manager missing");
                        baseline = Count; phase = 4; until = now + .6; break;
                    case 4:
                        Require(Count > baseline, "Moving at surface did not leave a wake");
                        baseline = Count; phase = 5; until = now + .4; break;
                    case 5:
                        Require(Count == baseline, "Stationary player emitted continuous waves");
                        phase = 6; until = now + .9; break;
                    case 6:
                        baseline = Count; phase = 7; until = now + .5; break;
                    case 7:
                        Require(Count == baseline, "Deep swimming emitted surface wakes");
                        movement.Teleport(new Vector3(20, -.1f, 47));
                        Physics.SyncTransforms(); Present.Invoke(movement, null);
                        Require(Count == baseline, "Teleport emitted an entry splash");
                        Finish("PASS: actual client entry ring/audio, moving wake, stationary/deep suppression and teleport suppression.");
                        break;
                }
            }
            catch (Exception e) { Finish("FAIL: " + e); }
        }
        static int Count => Shader.GetGlobalInteger("_MarcoWaterRippleCount");
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static void Finish(string result)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            Directory.CreateDirectory("Logs/PoolWater");
            File.WriteAllText("Logs/PoolWater/player-result.txt", result);
            if (result.StartsWith("PASS")) Debug.Log("[WaterReactions] " + result);
            else Debug.LogError("[WaterReactions] " + result);
            EditorApplication.ExitPlaymode();
        }
    }
}
