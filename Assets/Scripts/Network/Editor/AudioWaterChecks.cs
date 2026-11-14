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
                    CheckDiveMuffle(round);
                    CheckKnockAsMoan(round);
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
                    Require(Water(player) == 2, "A listener whose eyes are below the pool surface must count as submerged (depth only, not valve proximity)");
                    Teleport(player, new Vector3(7, -2.45f, 47));
                    Require(Water(player) == 2, "Circular sump must use pool surface, not lower trigger top");
                    Teleport(player, new Vector3(1, 1.05f, 40));
                    Require(Water(player) == 0, "Dry deck classified as water");
                    Debug.Log("[AudioWaterChecks] PASS: seeker movement sound, Runner repeated surface movement cues, seeker-only drone, stop, deep areas, crouch, sump, submerged by depth and dry deck.");
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

            // D1-D4 (§17, 11-14): which received sounds end the drone. The client bodies of the RPCs run directly.
            MethodInfo Logic(string rpc)
            {
                foreach (var m in typeof(NetworkRound).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (m.Name.StartsWith("RpcLogic___" + rpc)) return m;
                throw new Exception("[AudioWaterChecks] no client logic for " + rpc);
            }
            var local = NetworkPlayer.Local;
            Vector3 near = local.transform.position + Vector3.right * 2f;
            bool Ends(string label, Action deliver)
            {
                clock.SetValue(playback, Time.timeAsDouble - 60f);
                fadeClock.SetValue(playback, -1d);
                tick.Invoke(playback, new object[] { true });
                Require(Mathf.Abs(Find(round, "SeekerDrone").volume - .15f) < .002f, label + ": drone not at full before the event");
                deliver();
                bool reset = Time.timeAsDouble - (double)clock.GetValue(playback) < .05;
                fadeClock.SetValue(playback, Time.timeAsDouble - .21);
                tick.Invoke(playback, new object[] { true });
                var drone = Find(round, "SeekerDrone");
                bool off = drone == null || drone.volume == 0f;
                Debug.Log($"[AudioWaterChecks] {label}: timer reset {reset}, off after .2s {off}");
                return reset && off;
            }
            Require(Ends("D1 duck landing", () => Logic("DecoyPulseTarget").Invoke(round,
                new object[] { local.Owner, near, "Tile_0", 1f, false, 3f })), "D1: a duck landing did not end the drone");
            Require(Ends("D2 recorder playback", () => Logic("SoundCanFrameTarget").Invoke(round,
                new object[] { local.Owner, 9999, 0, new byte[Marco.Network.Voice.VoiceCodec.SamplesPerPacket], near, 1f, false })), "D2: recorder playback did not end the drone");
            Require(Ends("D3 footstep (existing)", () => Logic("MovementStepTarget").Invoke(round,
                new object[] { local.Owner, local.PlayerId + 100, "Tile_0", near, .7f, .4f, false, false, near, false })), "D3: a footstep no longer ends the drone");
            round.GetComponent<Marco.Network.Audio.RecordedSoundPlayback>()?.Stop(-1);
            // D4: the valve-open cue is map-wide 2D, not a hearing result (10-26) — the drone stays (스펙 해석, 11-14).
            clock.SetValue(playback, Time.timeAsDouble - 60f);
            fadeClock.SetValue(playback, -1d);
            tick.Invoke(playback, new object[] { true });
            Logic("ValveOpenedObservers").Invoke(round, new object[0]);
            tick.Invoke(playback, new object[] { true });
            Require(Mathf.Abs(Find(round, "SeekerDrone").volume - .15f) < .002f && (double)fadeClock.GetValue(playback) < 0,
                "D4: the valve-open cue ended the drone");
            Debug.Log("[AudioWaterChecks] D1-D4 drone release PASS.");
        }
        // W1-W4 (§17 잠수 로우패스, 11-14): one cutoff rule for the round sounds, the shout/scream one-shots and the recorder.
        static void CheckDiveMuffle(NetworkRound round)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var local = NetworkPlayer.Local;
            var movement = local.GetComponent<PlayerMovement>();
            var swimming = typeof(PlayerMovement).GetField("swimming", any);
            var surface = typeof(PlayerMovement).GetField("waterSurface", any);
            object swimmingBefore = swimming.GetValue(movement), surfaceBefore = surface.GetValue(movement);
            void Dive(bool on)
            {
                swimming.SetValue(movement, on ? true : swimmingBefore);
                surface.SetValue(movement, on ? movement.transform.position.y + 10f : surfaceBefore);
            }
            var followUpdate = typeof(Marco.Network.Audio.RoundAudioPlayback).GetMethod("UpdateFollowed", any);
            var followed = (System.Collections.IList)typeof(Marco.Network.Audio.RoundAudioPlayback).GetField("followed", any).GetValue(null);
            MethodInfo vocalLogic = null;
            foreach (var m in typeof(PlayerMovement).GetMethods(any)) if (m.Name.StartsWith("RpcLogic___AbilitySoundTarget")) vocalLogic = m;
            Require(vocalLogic != null, "W: no client logic for AbilitySoundTarget");
            Vector3 near = local.transform.position + Vector3.right * 2f;
            try
            {
                Dive(true);
                Require(Marco.Network.Audio.RoundAudioPlayback.LocalDiving, "W1: forced dive not seen");
                // W1: a scream one-shot made while diving gets exactly one filter at 650 Hz.
                vocalLogic.Invoke(movement, new object[] { local.Owner, near, 9f, 1f, 1f, false, true, false, -1, -1 });
                GameObject vocal = null;
                foreach (var go in UnityEngine.Object.FindObjectsByType<AudioSource>()) if (go.name == "Ability vocal") vocal = go.gameObject;
                Require(vocal != null, "W1: no scream one-shot");
                var filters = vocal.GetComponents<AudioLowPassFilter>();
                Require(filters.Length == 1 && filters[0].enabled && Mathf.Approximately(filters[0].cutoffFrequency, 650f), "W1: scream not muffled at 650 Hz while diving");
                // W2: the recorder playback while diving.
                var recorder = round.gameObject.GetComponent<Marco.Network.Audio.RecordedSoundPlayback>() ?? round.gameObject.AddComponent<Marco.Network.Audio.RecordedSoundPlayback>();
                recorder.Receive(4242, 0, new byte[Marco.Network.Voice.VoiceCodec.SamplesPerPacket], near, 1f, false);
                AudioLowPassFilter RecorderFilter() { var playing = (System.Collections.IDictionary)typeof(Marco.Network.Audio.RecordedSoundPlayback).GetField("playing", any).GetValue(recorder); var entry = playing.Contains(4242) ? playing[4242] : null; return entry?.GetType().GetField("Filter").GetValue(entry) as AudioLowPassFilter; }
                var recFilter = RecorderFilter();
                Require(recFilter != null && recFilter.enabled && Mathf.Approximately(recFilter.cutoffFrequency, 650f), "W2: recorder not muffled at 650 Hz while diving");
                // W3: surfacing clears both, nothing left behind.
                Dive(false);
                followUpdate.Invoke(null, null);
                typeof(Marco.Network.Audio.RecordedSoundPlayback).GetMethod("Update", any).Invoke(recorder, null);
                Require(!filters[0].enabled, "W3: scream filter stayed on after surfacing");
                Require(RecorderFilter() is { enabled: false }, "W3: recorder filter stayed on after surfacing");
                // W4: a destroyed one-shot leaves the follow list.
                int before = followed.Count;
                UnityEngine.Object.DestroyImmediate(vocal);
                followUpdate.Invoke(null, null);
                Require(followed.Count == before - 1, $"W4: follow list kept a destroyed one-shot ({before} -> {followed.Count})");
                recorder.Stop(-1);
                Debug.Log("[AudioWaterChecks] W1-W4 dive low-pass PASS: scream and recorder 650 Hz while diving, off after surfacing, one filter, no leftover.");
            }
            finally { Dive(false); }
        }
        // K1 · K2 · K5 · K6 (§5.1 구분 불가 그룹, 11-14): a knock arriving at a listener plays exactly like a moan.
        static void CheckKnockAsMoan(NetworkRound round)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var playback = round.GetComponent<Marco.Network.Audio.RoundAudioPlayback>();
            var local = NetworkPlayer.Local;
            MethodInfo target = null;
            foreach (var m in typeof(NetworkRound).GetMethods(any)) if (m.Name.StartsWith("RpcLogic___SoundTarget")) target = m;
            Require(target != null, "K: no client logic for SoundTarget");
            var voices = (System.Collections.IList)playback.GetType().GetField("voices", any).GetValue(playback);
            var movement = local.GetComponent<PlayerMovement>();
            var swimming = typeof(PlayerMovement).GetField("swimming", any);
            var surface = typeof(PlayerMovement).GetField("waterSurface", any);
            object swimmingBefore = swimming.GetValue(movement), surfaceBefore = surface.GetValue(movement);
            Vector3 point = local.transform.position + new Vector3(3f, 0f, 2f);
            string Arrive(string clip, bool muffled)
            {
                int before = voices.Count;
                // Same values the server sends for both (EmitSound: volume 0.65 × gain, 1.2 s × Seeker 1.5, 9 m ring).
                target.Invoke(round, new object[] { local.Owner, clip, point, .65f * .5f, 1.8f, -1, muffled, -1, false, true,
                    muffled ? 0f : 9f, 1.8f, -1, -1 });
                Require(voices.Count == before + 1, $"K: {clip} did not start a sound");
                var voice = voices[voices.Count - 1];
                var s = (AudioSource)voice.GetType().GetField("Source").GetValue(voice);
                var f = (AudioLowPassFilter)voice.GetType().GetField("Filter").GetValue(voice);
                string name = s.clip != null ? s.clip.name : "null";
                return $"{(name.StartsWith("Moan_") ? "Moan_x" : name)}|vol {s.volume:0.###}|blend {s.spatialBlend}|min {s.minDistance}|max {s.maxDistance}|rolloff {s.rolloffMode}|" +
                       $"doppler {s.dopplerLevel}|spread {s.spread}|loop {s.loop}|at {s.transform.position}|filter {f.enabled}/{f.cutoffFrequency}";
            }
            try
            {
                foreach (bool dive in new[] { false, true })
                {
                    swimming.SetValue(movement, dive ? true : swimmingBefore);
                    surface.SetValue(movement, dive ? movement.transform.position.y + 10f : surfaceBefore);
                    foreach (bool muffled in new[] { false, true })
                    {
                        string knock = Arrive("Knock", muffled), moan = Arrive("Moan_1", muffled);
                        Debug.Log($"[AudioWaterChecks] K2 dive={dive} muffled={muffled} | knock {knock} | moan {moan}");
                        Require(knock.StartsWith("Moan_x|"), "K1: a knock did not play a moan clip: " + knock);
                        Require(knock == moan, $"K2/K6: knock and moan differ (dive={dive}, muffled={muffled})");
                    }
                }
            }
            finally
            {
                swimming.SetValue(movement, swimmingBefore);
                surface.SetValue(movement, surfaceBefore);
                playback.Clear();
            }
            // K5: a knock arriving ends the Seeker drone like any other hearing result (existing behaviour kept).
            var clock = playback.GetType().GetField("quietSince", any);
            var fadeClock = playback.GetType().GetField("fadeStartedAt", any);
            var tick = playback.GetType().GetMethod("TickSeekerDrone", any);
            clock.SetValue(playback, Time.timeAsDouble - 60f); fadeClock.SetValue(playback, -1d);
            tick.Invoke(playback, new object[] { true });
            target.Invoke(round, new object[] { local.Owner, "Knock", point, .325f, 1.8f, -1, false, -1, false, true, 9f, 1.8f, -1, -1 });
            bool reset = Time.timeAsDouble - (double)clock.GetValue(playback) < .05;
            fadeClock.SetValue(playback, Time.timeAsDouble - .21);
            tick.Invoke(playback, new object[] { true });
            var drone = Find(round, "SeekerDrone");
            Require(reset && (drone == null || drone.volume == 0f), "K5: a knock did not end the drone");
            playback.Clear();
            Debug.Log("[AudioWaterChecks] K1 K2 K5 K6 knock-as-moan PASS: same clip set, volume, 3D settings, wall and dive filters; drone ends.");
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
