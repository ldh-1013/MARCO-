using System;
using System.Reflection;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    [InitializeOnLoad]
    public static class AudioWaterChecks
    {
        const string Pending = "Marco.AudioWaterChecks";
        static int phase; static double deadline, until; static float previous;
        static object water; static MethodInfo query;
        static readonly System.Collections.Generic.HashSet<AudioSource> splashes = new();
        static AudioWaterChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
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
                if (now > deadline) throw new Exception("Timeout " + phase);
                if (!EditorApplication.isPlaying) return;
                var round = NetworkRound.Instance; var player = NetworkPlayer.Local;
                if (phase == 0)
                {
                    var lan = UnityEngine.Object.FindAnyObjectByType<LanConnector>();
                    if (lan == null) return; lan.Host(); phase++; return;
                }
                if (phase == 1)
                {
                    if (round == null || player == null || !round.IsClientInitialized) return;
                    round.StartRound(); until = now + 1; phase++; return;
                }
                if (phase == 2 && now >= until)
                {
                    player.GetComponent<PlayerMovement>().enabled = false;
                    player.GetComponent<PlayerInteractor>().enabled = false;
                    Require(Find(round, "SeekerDrone") is { loop: true }, "Seeker drone missing");
                    var type = typeof(NetworkRound).Assembly.GetType("Marco.Network.Audio.SoundWorld", true);
                    water = Activator.CreateInstance(type); query = type.GetMethod("WaterState");
                    Teleport(player, new Vector3(4.5f, -.15f, 43));
                    Require(Water(player) == 1, "Seeker shallow water incorrectly submerged");
                    previous = Time.time; until = now + 2; phase = 20; return;
                }
                if (phase == 20)
                {
                    float elapsed = Mathf.Min(.05f, Time.time-previous); previous=Time.time;
                    float direction = ((int)(Time.time/.8f)%2==0) ? 1 : -1;
                    player.GetComponent<CharacterController>().Move(Vector3.right*direction*.8f*elapsed);
                    if (now < until) return;
                    Require(Find(round, "SurfaceSwim") is { loop: true }, "Seeker movement sound missing");
                    Call(round, "CheatToggleHostRole"); until=now+3; phase=3; return;
                }
                if (phase == 3 && now >= until)
                {
                    Require(player.Role == PlayerRole.Runner, "Role change failed");
                    var drone = Find(round, "SeekerDrone");
                    Require(drone == null || drone.volume < .002f, "Runner still hears seeker drone");
                    Teleport(player, new Vector3(4.5f, -.15f, 43));
                    Require(Water(player) == 1, "Shallow pool should be partially submerged");
                    splashes.Clear(); previous = Time.time; until = now + 12; phase++; return;
                }
                if (phase == 4 || phase == 6)
                {
                    float dt = Mathf.Min(.05f, Time.time - previous); previous = Time.time;
                    player.GetComponent<CharacterController>().Move(Vector3.right * (((int)(Time.time/.8f)%2==0) ? 1 : -1) * .8f * dt);
                                        if (phase == 4)
                        foreach (var sound in round.GetComponentsInChildren<AudioSource>())
                            if (sound.clip != null && sound.clip.name.StartsWith("SurfaceSwim"))
                            { Require(sound.loop, "Water must continue across step boundaries"); splashes.Add(sound); }
                    if (now < until) return;
                    if (phase == 4)
                    {
                        Debug.Log($"[AudioWaterChecks] Shallow end position={player.transform.position}, bounds={player.GetComponent<CharacterController>().bounds}, water={Water(player)}");
                        foreach (var sound in round.GetComponentsInChildren<AudioSource>()) Debug.Log($"[AudioWaterChecks] Source={sound.name} clip={sound.clip} loop={sound.loop} volume={sound.volume}");
                        Require(splashes.Count == 1, "Water loop restarted or disappeared: " + splashes.Count);
                        Debug.Log("[AudioWaterChecks] Repeated splashes=" + splashes.Count);
                        until = now + .5; phase++; return;
                    }
                    Require(Water(player) == 2 && Find(round, "SurfaceSwim") == null, "Submerged movement played surface sound");
                    Teleport(player, new Vector3(4.5f, -.15f, 43));
                    player.GetComponent<PlayerMovement>().ApplyCrouchPresentation(true);
                    Physics.SyncTransforms();
                    Require(Water(player) == 1, "Short/crouching Runner in shallow water should retain surface cue");
                    player.GetComponent<PlayerMovement>().ApplyCrouchPresentation(false);
                    Teleport(player, new Vector3(4.5f, -.15f, 51));
                    Require(Water(player) == 2, "Underwater valve E area must be excluded");
                    Teleport(player, new Vector3(7, -2.45f, 47));
                    Require(Water(player) == 2, "Circular sump must use pool surface, not lower trigger top");
                    Teleport(player, new Vector3(1, 1.05f, 40));
                    Require(Water(player) == 0, "Dry deck classified as water");
                    Debug.Log("[AudioWaterChecks] PASS: seeker movement sound, Runner 12-second single-source loop, seeker-only drone, stop, deep areas, crouch, sump, underwater valve and dry deck.");
                    Finish(0); return;
                }
                if (phase == 5 && now >= until)
                {
                    Require(Find(round, "SurfaceSwim") == null, "Water sound remained after stopping");
                    Teleport(player, new Vector3(37, -2.45f, 45));
                    previous = Time.time; until = now + .7; phase++; return;
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }
        static void Teleport(NetworkPlayer player, Vector3 point) { player.GetComponent<PlayerMovement>().Teleport(point); Physics.SyncTransforms(); }
        static int Water(NetworkPlayer player) { object[] args = { player, 0f }; return (int)query.Invoke(water, args); }
        static AudioSource Find(NetworkRound round, string prefix)
        {
            foreach (var source in round.GetComponentsInChildren<AudioSource>())
                if (source.clip != null && source.clip.name.StartsWith(prefix)) return source;
            return null;
        }
        static object Call(object target, string name) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
        static void Require(bool value, string message) { if (!value) throw new Exception("[AudioWaterChecks] " + message); }
        static void Finish(int code) { SessionState.SetBool(Pending, false); EditorApplication.update -= Tick; EditorApplication.Exit(code); }
    }
}
