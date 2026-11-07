using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Marco.Core.WinCondition;
using Marco.Network.Escape;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Valves;
using Marco.Network.Vision;
using Marco.Network.Voice;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Server-side pulse items (기획서 대조 S1~S9, D9) on a real LAN host. Each item is tested only when
    // its server method exists, so the check runs after every commit. With MARCO_SWITCHES_OFF=1 it expects
    // the pre-work behaviour instead (all RippleServerFeatures switches compiled false). Server-side
    // exceptions and errors during the run are collected and fail the check. No scene or prefab is saved.
    [InitializeOnLoad]
    public static class RippleServerChecks
    {
        const string Pending = "Marco.RippleServerChecks";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int phase, step;
        static double deadline, until, injectUntil, lastInject;
        static bool switchesOff;
        static readonly List<string> results = new(), failures = new(), errors = new();
        static Vector3 farSpot;
        static int ownRingsBefore, serverPulsesSeen, flickerFrame;
        static ValveId testValve;
        static float lastServerNext;
        static VoiceGrade[] flicker;

        static RippleServerChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
        static void Subscribe()
        {
            deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= Collect;
            Application.logMessageReceived += Collect;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new Exception("Batch only");
            EditorSceneManager.OpenScene("Assets/Scenes/Network/NetworkPool_v1_2.unity", OpenSceneMode.Single);
            SessionState.SetBool(Pending, true);
            Subscribe();
            EditorApplication.EnterPlaymode();
        }

        static void Collect(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (errors.Count < 40) errors.Add(type + ": " + message.Split('\n')[0]);
        }

        static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Server check timed out at phase " + phase + " step " + step);
                if (!EditorApplication.isPlaying) return;
                var round = NetworkRound.Instance;
                var player = NetworkPlayer.Local;
                if (phase == 0)
                {
                    switchesOff = Environment.GetEnvironmentVariable("MARCO_SWITCHES_OFF") == "1";
                    var lan = UnityEngine.Object.FindAnyObjectByType<LanConnector>();
                    if (lan == null) return;
                    lan.Host(); phase = 1; return;
                }
                if (phase == 1)
                {
                    if (player == null || round == null || !round.IsClientInitialized) return;
                    round.StartRound(); until = now + 1.5; phase = 2; return;
                }
                if (phase == 2 && now >= until)
                {
                    player.GetComponent<PlayerMovement>().enabled = false;
                    player.GetComponent<PlayerInteractor>().enabled = false;
                    foreach (var device in Microphone.devices) if (Microphone.IsRecording(device)) Microphone.End(device);
                    // Runner listener: perceived radius equals the base radius (Seeker hears × 1.2).
                    if (player.Role == PlayerRole.Seeker) typeof(NetworkRound).GetMethod("CheatToggleHostRole", Flags)?.Invoke(round, null);
                    farSpot = player.transform.position;
                    StaticChecks();
                    step = 0; until = now + 1.0; phase = 3; return;
                }
                if (phase == 3 && now >= until)
                {
                    if (RunStep(round, player, now)) return;
                    phase = 4; return;
                }
                if (phase == 4)
                {
                    foreach (var line in results) Debug.Log("[RippleServerChecks] RESULT " + line);
                    foreach (var line in errors) Debug.Log("[RippleServerChecks] SERVER-LOG " + line);
                    if (errors.Count > 0) failures.Add($"{errors.Count} exception/error log(s) during the run");
                    if (failures.Count > 0) throw new Exception("FAIL: " + string.Join(" | ", failures));
                    Debug.Log($"[RippleServerChecks] PASS ({(switchesOff ? "switches OFF — pre-work behaviour" : "switches ON")}). No scene saved.");
                    Finish(0);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }

        static void Expect(bool condition, string item, string detail)
        {
            results.Add((condition ? "ok   " : "FAIL ") + item + ": " + detail);
            if (!condition) failures.Add(item + ": " + detail);
        }

        // A switch that does not exist yet means the item is not implemented: expect the pre-work behaviour.
        static bool On(string name) =>
            !switchesOff && typeof(RippleServerFeatures).GetField(name, Flags)?.GetValue(null) is bool value && value;

        static bool Has(string method) => typeof(NetworkRound).GetMethod(method, Flags) != null;
        static object Invoke(NetworkRound round, string method, params object[] args) =>
            typeof(NetworkRound).GetMethod(method, Flags).Invoke(round, args);

        // Policies and spec constants that need no scene.
        static void StaticChecks()
        {
            var policy = typeof(NetworkRound).GetMethod("CanReceiveSound", Flags);
            bool others = (bool)policy.Invoke(null, new object[] { "Breath_Runner", 7, 8 });
            bool self = (bool)policy.Invoke(null, new object[] { "Breath_Runner", 7, 7 });
            bool expectOthers = On("BreathAudible");
            Expect(self && others == expectOthers, "S1 breath audience", $"self={self} others={others} (expected others={expectOthers})");
            var radius = typeof(PulseSpec).GetMethod("BreathRadius", Flags);
            if (radius != null)
            {
                var values = new float[6];
                for (int i = 0; i < values.Length; i++) values[i] = (float)radius.Invoke(null, new object[] { i });
                Expect(Mathf.Approximately(values[0], 3) && Mathf.Approximately(values[1], 5) && Mathf.Approximately(values[2], 7) &&
                       Mathf.Approximately(values[3], 9) && Mathf.Approximately(values[5], 9),
                    "S1 breath radius", "3m → +2m per 15 s → 9m max: " + string.Join("/", values));
            }
            var gate = Type.GetType("Marco.Network.Voice.VoicePulseGate, Marco.Network");
            if (gate != null) SimulateVoiceGate(gate);
            var activity = Type.GetType("Marco.Network.Voice.VoiceActivityGate, Marco.Network");
            if (activity != null) SimulateActivityGate(activity);
        }

        // Boundary flicker at 20 ms frames for 10 s, three patterns, through the shared voice pulse gate.
        static void SimulateVoiceGate(Type gateType)
        {
            foreach (int period in new[] { 1, 3, 10 })
            {
                var gate = Activator.CreateInstance(gateType);
                var should = gateType.GetMethod("ShouldPulse");
                int pulses = 0;
                for (int frame = 0; frame < 500; frame++)
                {
                    var grade = (frame / period) % 2 == 0 ? VoiceGrade.Whisper : VoiceGrade.Talk;
                    float duration = grade == VoiceGrade.Whisper ? .6f : 1.2f;
                    if ((bool)should.Invoke(gate, new object[] { grade, frame * .02f, duration })) pulses++;
                }
                float rate = pulses / 10f;
                Expect(rate <= 7f, "S9 gate (simulated)", $"Whisper↔Talk every {period * 20} ms: {rate:0.0} pulses/s per speaker (≤ 7)");
            }
        }

        // D1 gate (switch off by default) on synthetic 20 ms / 48 kHz frames: steady fan noise must stop
        // opening it once the floor has learned the room, a click must never open it, a voice-band tone must.
        static void SimulateActivityGate(Type gateType)
        {
            var random = new System.Random(7);
            float[] Frame(Func<int, float> sample) { var f = new float[960]; for (int i = 0; i < f.Length; i++) f[i] = sample(i); return f; }
            var gate = Activator.CreateInstance(gateType);
            var accept = gateType.GetMethod("Accept");
            float t = 0; int openNoise = 0, frames = 0;
            for (int n = 0; n < 500; n++, t += .02f)  // 10 s fan-like noise at about -42 dBFS
            {
                bool open = (bool)accept.Invoke(gate, new object[] { Frame(_ => (float)(random.NextDouble() * 2 - 1) * .0137f), t });
                if (n >= 250) { frames++; if (open) openNoise++; }
            }
            bool click = false;
            for (int n = 0; n < 5; n++, t += .02f)    // one 20 ms click, then quiet
            {
                int frame = n;
                click |= (bool)accept.Invoke(gate, new object[] { Frame(i => frame == 0 ? (i % 2 == 0 ? .3f : -.3f) : 0f), t });
            }
            float openedAfter = -1;
            for (int n = 0; n < 50; n++, t += .02f)   // 300 Hz voice-band tone at about -25 dBFS
            {
                int frame = n;
                bool open = (bool)accept.Invoke(gate, new object[] { Frame(i => .08f * Mathf.Sin(2 * Mathf.PI * 300f * (frame * 960 + i) / 48000f)), t });
                if (open && openedAfter < 0) openedAfter = n * .02f;
            }
            Expect(openNoise == 0 && !click && openedAfter >= .14f && openedAfter <= .3f, "D1 gate (simulated; switch off in game)",
                $"steady noise open frames after 5 s: {openNoise}/{frames}, click opened: {click}, tone opened after {openedAfter:0.00}s");
        }

        // Live steps; returns true while a step is still running.
        static bool RunStep(NetworkRound round, NetworkPlayer player, double now)
        {
            var rings = player.GetComponentInChildren<WalkingFootRings>();
            switch (step)
            {
                case 0: // S2 — valve ring radius: A 6m, the others 12m. Only an active valve may rotate.
                    {
                        if (Has("ValveSoundRadius"))
                        {
                            float a = (float)Invoke(round, "ValveSoundRadius", ValveId.A), b = (float)Invoke(round, "ValveSoundRadius", ValveId.B);
                            Expect(Mathf.Approximately(a, On("ValveAMachineRadius") ? 6f : 12f) && Mathf.Approximately(b, 12f), "S2 valve radius rule", $"A {a}m, B {b}m");
                        }
                        var board = (ValveBoard)typeof(NetworkRound).GetField("board", Flags).GetValue(round);
                        bool found = false;
                        foreach (var id in new[] { ValveId.A, ValveId.C, ValveId.D, ValveId.B, ValveId.E })
                            if (board != null && board.Valves.ContainsKey(id)) { testValve = id; found = true; break; }
                        if (!found || !ValveView.TryGet(testValve, out var valve) || !Spot(valve.transform.position, 3f, out var near))
                        { results.Add("skip S2 live: no active valve with an open spot"); step = 2; return true; }
                        rings.ClearPulses();
                        player.GetComponent<PlayerMovement>().Teleport(near);
                        Physics.SyncTransforms();
                        Invoke(round, "ValveAudio", testValve, ValveSoundEvent.RotatingStarted);
                        until = now + .8; step = 1; return true;
                    }
                case 1:
                    if (now < until) return true;
                    {
                        ValveView.TryGet(testValve, out var valveView);
                        float radius = RingRadiusAt(rings, valveView.transform.position);
                        // B and E are underwater: the ring crosses the water surface (× 0.5) to a deck listener.
                        float expected = testValve == ValveId.A && On("ValveAMachineRadius") ? 6f : 12f;
                        bool underwater = testValve == ValveId.B || testValve == ValveId.E;
                        Expect(underwater ? radius > 0 && radius <= expected : Mathf.Approximately(radius, expected),
                            "S2 valve ring live", $"active valve {testValve}: ring {radius}m (expected {expected}m{(underwater ? " or less through water" : "")})");
                        Invoke(round, "ValveAudio", testValve, ValveSoundEvent.RotatingStopped);
                    }
                    step = 2; return true;
                case 2: // S4 — tag pulse emission (Echoed path method)
                    if (!Has("EmitTagPulse")) { results.Add("skip S4: not implemented"); step = 4; return true; }
                    rings.ClearPulses();
                    Invoke(round, "EmitTagPulse", player);
                    until = now + .5; step = 3; return true;
                case 3:
                    if (now < until) return true;
                    Expect(Mathf.Approximately(RingRadiusAt(rings, player.transform.position), On("TagPulse") ? 22f : 0f), "S4 tag pulse", $"ring {RingRadiusAt(rings, player.transform.position)}m at the tag point");
                    step = 4; return true;
                case 4: // S5 / S8 — exit pulses near and far
                    {
                        ExitView exit = null;
                        foreach (var candidate in ExitView.All) { exit = candidate; break; }
                        if (exit == null || !Spot(exit.transform.position, 4f, out var nearExit)) { results.Add("skip S5/S8: no exit spot"); step = 10; return true; }
                        rings.ClearPulses();
                        player.GetComponent<PlayerMovement>().Teleport(nearExit);
                        Physics.SyncTransforms();
                        if (Has("EmitGatePulses")) Invoke(round, "EmitGatePulses");
                        until = now + .5; step = 5; return true;
                    }
                case 5:
                    if (now < until) return true;
                    if (Has("EmitGatePulses"))
                        Expect(On("GatePulse") ? ExitRings(rings) >= 1 : ExitRings(rings) == 0,
                            "S5 gate pulse", $"{ExitRings(rings)} ring(s) at exits near the listener");
                    else results.Add("skip S5: not implemented");
                    rings.ClearPulses();
                    TempExitRingsOff();
                    Invoke(round, "EmitExitPulses");
                    until = now + .5; step = 6; return true;
                case 6:
                    if (now < until) return true;
                    {
                        bool judged = On("ExitPulseJudged");
                        int temporary = TempExitRings();
                        Expect(judged ? temporary == 0 && ExitRings(rings) >= 1 : temporary >= 1,
                            "S8 exit pulse near", $"judged path={judged}, listener rings at exits={ExitRings(rings)}, temporary all-client rings={temporary}{TempInfo()}");
                        rings.ClearPulses();
                        TempExitRingsOff();
                        bool far = true;
                        foreach (var e in ExitView.All) if (Vector3.Distance(e.transform.position, farSpot) < 27f) far = false;
                        if (!far) { results.Add("skip S8 far: spawn spot within 27m of an exit"); step = 8; return true; }
                        player.GetComponent<PlayerMovement>().Teleport(farSpot);
                        Physics.SyncTransforms();
                        Invoke(round, "EmitExitPulses");
                        until = now + .5; step = 7; return true;
                    }
                case 7:
                    if (now < until) return true;
                    {
                        bool judged = On("ExitPulseJudged");
                        int temporary = TempExitRings();
                        Expect(judged ? ExitRings(rings) == 0 && temporary == 0 : temporary >= 1,
                            "S8 exit pulse far (>22m)", $"judged path={judged}, listener rings={ExitRings(rings)}, temporary rings={temporary}");
                        TempExitRingsOff();
                    }
                    step = 8; return true;
                case 8: // S6 / S7 — drain hatch pulses
                    if (!Has("EmitSurvivorPulse") && !Has("EmitDrainWorkPulse")) { results.Add("skip S6/S7: not implemented"); step = 10; return true; }
                    if (!DrainHatchView.TryGet(DrainHatchId.First, out var hatch) || !Spot(hatch.transform.position, 3f, out var nearHatch))
                    { results.Add("skip S6/S7: no hatch spot"); step = 10; return true; }
                    rings.ClearPulses();
                    player.GetComponent<PlayerMovement>().Teleport(nearHatch);
                    Physics.SyncTransforms();
                    if (Has("EmitSurvivorPulse")) Invoke(round, "EmitSurvivorPulse", DrainHatchId.First);
                    until = now + .5; step = 9; return true;
                case 9:
                    if (now < until) return true;
                    {
                        DrainHatchView.TryGet(DrainHatchId.First, out var hatchView);
                        if (Has("EmitSurvivorPulse"))
                            Expect(!On("SurvivorPulse") ? RingRadiusAt(rings, hatchView.transform.position) == 0 : RingRadiusAt(rings, hatchView.transform.position) > 0 && RingRadiusAt(rings, hatchView.transform.position) <= 22f, "S6 survivor pulse",
                                $"ring {RingRadiusAt(rings, hatchView.transform.position)}m at the active hatch (0 = none: hatch out of sight or switch off)");
                        rings.ClearPulses();
                        if (Has("EmitDrainWorkPulse")) Invoke(round, "EmitDrainWorkPulse", DrainHatchId.First);
                    }
                    until = now + .5; step = 91; return true;
                case 91:
                    if (now < until) return true;
                    if (Has("EmitDrainWorkPulse"))
                    {
                        DrainHatchView.TryGet(DrainHatchId.First, out var hatchView);
                        float radius = RingRadiusAt(rings, hatchView.transform.position);
                        Expect(!On("DrainWorkPulse") ? radius == 0 : radius > 0 && radius <= 12f, "S7 drain work pulse", $"ring {radius}m (12m, ×0.5 per wall/water surface)");
                    }
                    step = 10; return true;
                case 10: // S9 — live boundary flicker through the real client and server voice paths
                    {
                        var voice = player.GetComponent<PlayerVoice>();
                        if (voice == null) { results.Add("skip S9 live: no PlayerVoice"); step = 12; return true; }
                        player.GetComponent<PlayerMovement>().Teleport(farSpot);
                        Physics.SyncTransforms();
                        rings.ClearPulses();
                        flicker = new VoiceGrade[500];
                        for (int i = 0; i < flicker.Length; i++) flicker[i] = (i / 3) % 2 == 0 ? VoiceGrade.Whisper : VoiceGrade.Talk;
                        ownRingsBefore = OwnRings(rings);
                        serverPulsesSeen = 0; flickerFrame = 0;
                        lastServerNext = ServerNext(voice);
                        lastInject = 0; injectUntil = now + 10.0;
                        step = 11; return true;
                    }
                case 11:
                    {
                        var voice = player.GetComponent<PlayerVoice>();
                        float next = ServerNext(voice);
                        if (!Mathf.Approximately(next, lastServerNext)) { serverPulsesSeen++; lastServerNext = next; }
                        if (now < injectUntil)
                        {
                            if (now - lastInject >= .02 && flickerFrame < flicker.Length)
                            {
                                lastInject = now;
                                var samples = new float[VoiceCodec.InputSamplesPerPacket];
                                for (int i = 0; i < samples.Length; i++) samples[i] = .03f * Mathf.Sin(i * .2f);
                                typeof(PlayerVoice).GetMethod("OnVoiceSamplesReady", Flags).Invoke(voice, new object[] { samples, flicker[flickerFrame++] });
                            }
                            return true;
                        }
                        float seconds = flickerFrame * .02f;
                        float client = (OwnRings(rings) - ownRingsBefore) / Mathf.Max(seconds, .01f);
                        float server = serverPulsesSeen / Mathf.Max(seconds, .01f);
                        Expect(client <= 7f && server <= 7f, "S9 live flicker (60 ms Whisper↔Talk)",
                            $"{flickerFrame} frames: own rings {client:0.0}/s, server pulses {server:0.0}/s per speaker (≤ 7)");
                    }
                    step = 12; return true;
                default:
                    return false;
            }
        }

        // An open floor spot at `distance` from target with a clear line to it.
        static bool Spot(Vector3 target, float distance, out Vector3 spot)
        {
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f;
                var candidate = target + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                if (!Physics.Raycast(candidate + Vector3.up * 2.5f, Vector3.down, out var floor, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (floor.normal.y < .7f || Physics.CheckSphere(floor.point + Vector3.up * 1f, .35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var eye = floor.point + Vector3.up * 1.5f;
                var towards = (target - eye).normalized;
                if (Physics.Linecast(eye, target - towards * .4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                spot = floor.point + Vector3.up * 1.05f;
                return true;
            }
            spot = default;
            return false;
        }

        static IList Rings(WalkingFootRings rings) => (IList)typeof(WalkingFootRings).GetField("rings", Flags).GetValue(rings);

        static float RingRadiusAt(WalkingFootRings rings, Vector3 point)
        {
            float found = 0;
            foreach (var ring in Rings(rings))
            {
                var type = ring.GetType();
                if (!(bool)type.GetField("Sound").GetValue(ring)) continue;
                var go = (GameObject)type.GetField("Object").GetValue(ring);
                if (Vector3.Distance(go.transform.position, point) < .3f) found = (float)type.GetField("Radius").GetValue(ring);
            }
            return found;
        }

        static int ExitRings(WalkingFootRings rings)
        {
            int count = 0;
            foreach (var exit in ExitView.All)
                if (RingRadiusAt(rings, exit.transform.position) > 0) count++;
            return count;
        }

        static int TempExitRings()
        {
            int count = 0;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (renderer.name == "Temporary Exit Pulse Ring" && renderer.gameObject.activeInHierarchy) count++;
            return count;
        }

        static string TempInfo()
        {
            var root = GameObject.Find("Temporary Exit Pulse Rings");
            if (root == null) return " (no temporary ring root)";
            var pending = root.GetComponent<TemporaryExitPulseRings>();
            var list = typeof(TemporaryExitPulseRings).GetField("pending", Flags)?.GetValue(pending) as IList;
            return $" (root children={root.transform.childCount}, pending={list?.Count})";
        }

        static void TempExitRingsOff()
        {
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (renderer.name == "Temporary Exit Pulse Ring") renderer.gameObject.SetActive(false);
        }

        static int OwnRings(WalkingFootRings rings) =>
            typeof(WalkingFootRings).GetProperty("OwnRingsCreated", Flags)?.GetValue(rings) is int count ? count : 0;

        static float ServerNext(PlayerVoice voice) =>
            typeof(PlayerVoice).GetField("nextServerPulse", Flags)?.GetValue(voice) is float next ? next : 0;

        static void Finish(int code)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= Collect;
            EditorApplication.Exit(code);
        }
    }
}
