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
                    CheckSeekerDrone(round);
                    var type = typeof(NetworkRound).Assembly.GetType("Marco.Network.Audio.SoundWorld", true);
                    water = Activator.CreateInstance(type); query = type.GetMethod("WaterState");
                    Teleport(player, new Vector3(4.5f, -.15f, 43));
                    Require(Water(player) == 1, "Seeker shallow water incorrectly submerged");
                    previous = Time.time; until = now + 4; phase = 20; return;
                }
                if (phase == 20)
                {
                    float elapsed = Mathf.Min(.05f, Time.time-previous); previous=Time.time;
                    float direction = ((int)(Time.time/.55f)%2==0) ? 1 : -1;
                    player.GetComponent<CharacterController>().Move(Vector3.right*direction*2.5f*elapsed);
                    if (Find(round, "SurfaceSwim") is { loop: false }) splashes.Add(Find(round, "SurfaceSwim"));
                    if (now < until) return;
                    Require(splashes.Count > 0, "Seeker movement sound missing");
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
                    player.GetComponent<CharacterController>().Move(Vector3.right * (((int)(Time.time/.55f)%2==0) ? 1 : -1) * 2.5f * dt);
                                        if (phase == 4)
                        foreach (var sound in round.GetComponentsInChildren<AudioSource>())
                            if (sound.clip != null && sound.clip.name.StartsWith("SurfaceSwim"))
                            { Require(!sound.loop, "Water movement must play discrete distance cues"); splashes.Add(sound); }
                    if (now < until) return;
                    if (phase == 4)
                    {
                        Debug.Log($"[AudioWaterChecks] Shallow end position={player.transform.position}, bounds={player.GetComponent<CharacterController>().bounds}, water={Water(player)}");
                        foreach (var sound in round.GetComponentsInChildren<AudioSource>()) Debug.Log($"[AudioWaterChecks] Source={sound.name} clip={sound.clip} loop={sound.loop} volume={sound.volume}");
                        Require(splashes.Count >= 2, "Missing repeated distance cues: " + splashes.Count);
                        Debug.Log("[AudioWaterChecks] Repeated splashes=" + splashes.Count);
                        until = now + 1; phase++; return;
                    }
                    Require(Water(player) == 2 && Find(round, "SurfaceSwim") == null, "Submerged movement played surface sound");
                    Teleport(player, new Vector3(4.5f, -.15f, 43));
                    player.GetComponent<PlayerMovement>().ApplyCrouchPresentation(true);
                    Physics.SyncTransforms();
                    Require(Water(player) == 1, "Short/crouching Runner in shallow water should retain surface cue");
                    player.GetComponent<PlayerMovement>().ApplyCrouchPresentation(false);
                    Teleport(player, new Vector3(4.5f, -.8f, 51));
                    Require(Water(player) == 2, "A fully submerged listener near valve E must be excluded");
                    Teleport(player, new Vector3(7, -2.45f, 47));
                    Require(Water(player) == 2, "Circular sump must use pool surface, not lower trigger top");
                    Teleport(player, new Vector3(1, 1.05f, 40));
                    Require(Water(player) == 0, "Dry deck classified as water");
                    Debug.Log("[AudioWaterChecks] PASS: seeker movement sound, Runner repeated surface movement cues, seeker-only drone, stop, deep areas, crouch, sump, underwater valve and dry deck.");
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
        static void CheckSeekerDrone(NetworkRound round)
        {
            var playback = round.GetComponent<Marco.Network.Audio.RoundAudioPlayback>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var clock = playback.GetType().GetField("quietSince", flags);
            var fadeClock = playback.GetType().GetField("fadeStartedAt", flags);
            var tick = playback.GetType().GetMethod("TickSeekerDrone", flags);
            void Sample(float quietSeconds, float expected)
            {
                clock.SetValue(playback, Time.timeAsDouble - quietSeconds);
                tick.Invoke(playback, new object[] { true });
                var source = Find(round, "SeekerDrone");
                Require(Mathf.Abs((source == null ? 0f : source.volume) - expected) < .002f,
                    $"Unexpected drone gain after {quietSeconds} seconds");
            }
            Require(Find(round, "SeekerDrone") == null, "Drone started before 20 seconds");
            Sample(19f, 0f);
            Sample(25f, .045f);
            Sample(30f, .09f);
            Sample(45f, .12f);
            Sample(60f, .15f);
            playback.NotifyOtherSound();
            fadeClock.SetValue(playback, Time.timeAsDouble - .1);
            playback.NotifyOtherSound();
            tick.Invoke(playback, new object[] { true });
            Require(Mathf.Abs(Find(round, "SeekerDrone").volume - .075f) < .002f,
                "Repeated sounds restarted the fade");
            fadeClock.SetValue(playback, Time.timeAsDouble - .21);
            tick.Invoke(playback, new object[] { true });
            Require(Find(round, "SeekerDrone").volume == 0f, "Drone did not fade out in .2 seconds");
            Debug.Log("[AudioWaterChecks] Drone silence, gain ramp, and repeated-event fade PASS.");
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
