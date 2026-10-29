using System;
using System.Collections.Generic;
using System.Reflection;
using Marco.Core.WinCondition;
using Marco.Network.Audio;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Valves;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    [InitializeOnLoad]
    public static class AudioMovementChecks
    {
        const string Pending = "Marco.AudioMovementChecks";
        static int phase;
        static double deadline, until, started;
        static float previousTime; static float walkingSpeed = 4.5f; static int walkingCount;
        static readonly HashSet<AudioSource> heard = new();
        static readonly List<string> failures = new();
        static AudioMovementChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
        static void Subscribe() { deadline = EditorApplication.timeSinceStartup + 90; EditorApplication.update -= Tick; EditorApplication.update += Tick; }
        public static void Run()
        {
            if (!Application.isBatchMode) throw new Exception("Separate batch process only");
            EditorSceneManager.OpenScene("Assets/Scenes/Network/NetworkPool_v1_2.unity", OpenSceneMode.Single);
            SessionState.SetBool(Pending, true); Subscribe(); EditorApplication.EnterPlaymode();
        }
        static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Timeout phase " + phase);
                if (!EditorApplication.isPlaying) return;
                var round = NetworkRound.Instance; var player = NetworkPlayer.Local;
                if (phase == 0)
                {
                    var lan = UnityEngine.Object.FindAnyObjectByType<LanConnector>();
                    if (lan == null) return;
                    lan.Host(); phase++; return;
                }
                if (phase == 1)
                {
                    if (round == null || player == null || !round.IsClientInitialized) return;
                    round.StartRound(); until = now + 1; phase++; return;
                }
                if (phase == 2 && now >= until)
                {
                    if (player.Role == PlayerRole.Seeker) Call(round, "CheatToggleHostRole");
                    player.GetComponent<PlayerInteractor>().enabled = false;
                    StartWalk(player, new Vector3(1, 1.1f, 40)); phase++; return;
                }
                if (phase == 3 || phase == 5 || phase == 7)
                {
                    float dt = Mathf.Min(.05f, Time.time - previousTime); previousTime = Time.time;
                    float sign = ((int)((now - started) / .8) % 2 == 0) ? 1 : -1;
                    player.GetComponent<CharacterController>().Move((Vector3.forward * sign * walkingSpeed + Vector3.down * 2f) * dt);
                    foreach (var source in round.GetComponentsInChildren<AudioSource>())
                        if (source.clip != null && source.clip.name.StartsWith(phase == 3 ? "Tile_" : "Concrete_")) heard.Add(source);
                    if (now < until) return;
                    string kind = phase == 3 ? "Tile" : "Concrete";
                    Debug.Log($"[AudioMovement] {kind} actual-movement clips={heard.Count}, position={player.transform.position}");
                    DiagnoseFloor(player);
                    if (heard.Count == 0) failures.Add(kind + " movement produced no audio");
                    if (phase == 5) walkingCount = heard.Count;
                    if (phase == 7 && heard.Count <= walkingCount) failures.Add("Running cadence did not increase");
                    phase++; return;
                }
                if (phase == 4) { StartWalk(player, new Vector3(56, 1.1f, 33)); phase++; return; }
                if (phase == 6) { walkingSpeed = 7.2f; StartWalk(player, new Vector3(56, 1.1f, 33)); phase++; return; }
                if (phase == 8)
                {
                    // Verify the existing random activation rule, then test D in an active round.
                    int tries = 0;
                    while (!((ValveBoard)typeof(NetworkRound).GetField("board", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(round)).Valves.ContainsKey(ValveId.D) && tries++ < 50) round.RestartRound();
                    if (player.Role == PlayerRole.Seeker) Call(round, "CheatToggleHostRole");
                    if (!ValveView.TryGet(ValveId.D, out var valve)) throw new Exception("D not found");
                    player.GetComponent<PlayerMovement>().Teleport(valve.transform.position + new Vector3(-1.2f, .1f, 0));
                    Physics.SyncTransforms();
                    Debug.Log($"[AudioMovement] D position={valve.transform.position}, player={player.transform.position}, role={player.Role}, inRange={valve.IsInRange(player.transform.position, round.InteractRange)}");
                    Call(player.GetComponent<PlayerInteractor>(), "BeginInteraction", round, round.InteractRange);
                    until = now + 1.2; phase++; return;
                }
                if (phase == 9 && now >= until)
                {
                    round.TryGetValveState(ValveId.D, out var state);
                    Debug.Log($"[AudioMovement] D after role switch + interaction: status={state.Status} progress={state.Progress}");
                    if (state.Progress < .02f) failures.Add("Active D interaction failed");
                    round.EndWork(player.PlayerId, ValveId.D);
                    if (failures.Count > 0) throw new Exception(string.Join("; ", failures));
                    Debug.Log("[AudioMovement] PASS: actual tile/concrete movement and active D interaction after Seeker->Runner switch.");
                    Finish(0);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }
        static void StartWalk(NetworkPlayer player, Vector3 point)
        {
            var movement = player.GetComponent<PlayerMovement>(); movement.Teleport(point); movement.enabled = false;
            Physics.SyncTransforms(); heard.Clear(); previousTime = Time.time;
            started = EditorApplication.timeSinceStartup; until = started + 3;
            DiagnoseFloor(player);
        }
        static void DiagnoseFloor(NetworkPlayer player)
        {
            var type = typeof(NetworkRound).Assembly.GetType("Marco.Network.Audio.SoundWorld");
            var world = Activator.CreateInstance(type); object[] args = { player, null };
            bool grounded = (bool)type.GetMethod("TryFloor").Invoke(world, args);
            string material = grounded ? (string)type.GetMethod("Surface").Invoke(world, new[] { args[1], (object)player.transform.position }) : "none";
            Debug.Log($"[AudioMovement] floorFound={grounded}, collider={args[1]}, surface={material}");
            var cc = player.GetComponent<CharacterController>();
            foreach (var hit in Physics.RaycastAll(player.transform.TransformPoint(cc.center), Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                Debug.Log($"[AudioMovement] below={hit.collider.name} distance={hit.distance:F3} point={hit.point}");
        }
        static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        static void Finish(int code) { SessionState.SetBool(Pending, false); EditorApplication.update -= Tick; EditorApplication.Exit(code); }
    }
}
