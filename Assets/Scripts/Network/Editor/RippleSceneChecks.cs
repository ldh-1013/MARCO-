using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Marco.Network.Escape;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Valves;
using Marco.Network.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    // The ripple work must run in the main scene NetworkPool_v1_2. On a LAN host in that scene this checks that
    // every part it relies on is found and working: the Resources ring shader (a real ring is drawn with it),
    // the door list read from the map's PoolV12Part Door / ExternalDoor parts (the map importer's copy of
    // Layout_v1_2.json), the RippleServerFeatures switches, and the scene objects the server pulses use
    // (exits, drain hatches, valves, round audio). Read-only: no scene or prefab is saved.
    [InitializeOnLoad]
    public static class RippleSceneChecks
    {
        const string Pending = "Marco.RippleSceneChecks", Scene = "Assets/Scenes/Network/NetworkPool_v1_2.unity";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int phase;
        static double deadline, until;
        static readonly List<string> results = new(), failures = new(), errors = new();

        static RippleSceneChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
        static void Subscribe()
        {
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= Collect;
            Application.logMessageReceived += Collect;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new Exception("Batch only");
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            SessionState.SetBool(Pending, true);
            Subscribe();
            EditorApplication.EnterPlaymode();
        }

        static void Collect(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (errors.Count < 40) errors.Add(type + ": " + message.Split('\n')[0]);
        }

        static void Expect(bool condition, string item, string detail)
        {
            results.Add((condition ? "ok   " : "FAIL ") + item + ": " + detail);
            if (!condition) failures.Add(item + ": " + detail);
        }

        static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Scene check timed out at phase " + phase);
                if (!EditorApplication.isPlaying) return;
                var round = NetworkRound.Instance;
                var player = NetworkPlayer.Local;
                if (phase == 0)
                {
                    var lan = UnityEngine.Object.FindAnyObjectByType<LanConnector>();
                    Expect(lan != null, "LanConnector", lan != null ? "found" : "missing");
                    if (lan == null) { phase = 9; return; }
                    lan.Host(); phase = 1; return;
                }
                if (phase == 1)
                {
                    if (player == null || round == null || !round.IsClientInitialized) return;
                    round.StartRound(); until = now + 1.5; phase = 2; return;
                }
                if (phase == 2 && now >= until)
                {
                    foreach (var device in Microphone.devices) if (Microphone.IsRecording(device)) Microphone.End(device);
                    // NetworkPool_v1_2 is the scene that runs; its MapSceneLoader adds the map scene and makes it active.
                    var loaded = new List<string>();
                    for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                        loaded.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path);
                    var main = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(Scene);
                    Expect(main.isLoaded, "Main scene loaded", $"loaded: {string.Join(", ", loaded)}; active (map, via MapSceneLoader): {UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}");
                    StaticParts();
                    var rings = player.GetComponentInChildren<WalkingFootRings>();
                    Expect(rings != null, "WalkingFootRings on the local player", rings != null ? "found" : "missing");
                    if (rings != null) { rings.ClearPulses(); rings.ShowSound(player.transform.position + Vector3.up * 1.5f, 12f, 2.5f, false); }
                    until = now + .5; phase = 3; return;
                }
                if (phase == 3 && now >= until)
                {
                    var rings = player.GetComponentInChildren<WalkingFootRings>();
                    string shaderName = "none";
                    int wallStrips = 0;
                    if (rings != null)
                        foreach (var ring in (IList)typeof(WalkingFootRings).GetField("rings", Flags).GetValue(rings))
                        {
                            var type = ring.GetType();
                            if (!(bool)type.GetField("Sound").GetValue(ring)) continue;
                            var renderer = (Renderer)type.GetField("Renderer").GetValue(ring);
                            if (renderer != null && renderer.sharedMaterial != null) shaderName = renderer.sharedMaterial.shader.name;
                            if ((bool)type.GetField("HasWall").GetValue(ring)) wallStrips++;
                        }
                    Expect(shaderName == "Marco/RippleRing", "Ring drawn with the Resources shader", $"sound ring shader = {shaderName}, wall reveal strips = {wallStrips}");
                    phase = 9; return;
                }
                if (phase == 9)
                {
                    foreach (var line in results) Debug.Log("[RippleSceneChecks] RESULT " + line);
                    foreach (var line in errors) Debug.Log("[RippleSceneChecks] LOG " + line);
                    if (errors.Count > 0) failures.Add($"{errors.Count} exception/error log(s)");
                    if (failures.Count > 0) throw new Exception("FAIL: " + string.Join(" | ", failures));
                    Debug.Log("[RippleSceneChecks] PASS in " + Scene + ". No scene saved.");
                    Finish(0);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }

        static void StaticParts()
        {
            var shader = Resources.Load<Shader>("MarcoRipple/RippleRing");
            Expect(shader != null && shader.isSupported, "Resources shader MarcoRipple/RippleRing",
                shader == null ? "not found" : $"{shader.name}, supported={shader.isSupported}");

            int doorParts = 0, externalParts = 0;
            foreach (var component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (component == null || component.GetType().Name != "PoolV12Part") continue;
                string category = component.GetType().GetField("category")?.GetValue(component) as string;
                if (category == "Door") doorParts++;
                if (category == "ExternalDoor") externalParts++;
            }
            var doorsType = typeof(WalkingFootRings).Assembly.GetType("Marco.Network.Vision.RippleDoors");
            var all = doorsType?.GetProperty("All", Flags)?.GetValue(null) as ICollection;
            int doors = all?.Count ?? -1;
            Expect(doorParts + externalParts > 0 && doors > 0, "Door list from the map parts",
                $"PoolV12Part Door {doorParts}, ExternalDoor {externalParts} → RippleDoors probed {doors}");

            var switches = new List<string>();
            foreach (var field in typeof(RippleServerFeatures).GetFields(Flags))
                if (field.FieldType == typeof(bool)) switches.Add(field.Name + "=" + field.GetValue(null));
            Expect(switches.Count == 11 && !(bool)typeof(RippleServerFeatures).GetField("VoiceActivityGate").GetValue(null),
                "RippleServerFeatures switches", string.Join(", ", switches));

            int exits = 0, hatches = 0, valves = 0;
            foreach (var e in ExitView.All) if (e != null) exits++;
            foreach (var h in DrainHatchView.All) if (h != null) hatches++;
            foreach (var v in ValveView.All) if (v != null) valves++;
            var playback = UnityEngine.Object.FindAnyObjectByType<Marco.Network.Audio.RoundAudioPlayback>();
            Expect(exits >= 2 && hatches >= 2 && valves >= 5 && playback != null, "Scene objects the server pulses use",
                $"exits {exits}, drain hatches {hatches}, valves {valves}, RoundAudioPlayback {(playback != null ? "found" : "missing")}");
        }

        static void Finish(int code)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Collect;
            EditorApplication.Exit(code);
        }
    }
}
