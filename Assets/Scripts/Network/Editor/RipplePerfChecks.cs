using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Marco.Network.Editor
{
    // Host frame time with 0 rings and with 30 ring requests in one frame (기획서 §15.5 1순위 리스크).
    // Measurement only: uses the ring renderer's public calls, reads two private fields, saves nothing.
    [InitializeOnLoad]
    public static class RipplePerfChecks
    {
        const string Pending = "Marco.RipplePerfChecks";
        const int Trials = 3;
        const double IdleSeconds = 3, AliveSeconds = 1.0, GapSeconds = 3;
        static int phase, trial;
        static double deadline, until;
        static int lastFrame;
        static readonly List<float> idle = new(), alive = new();
        static readonly List<double> spawnMs = new();
        static readonly List<int> spawnQueries = new(), aliveAfter = new();
        static readonly List<string> queuedFrames = new();
        static readonly List<int> ownSameFrame = new();

        static RipplePerfChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
        static void Subscribe()
        {
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new Exception("Batch only");
            EditorSceneManager.OpenScene("Assets/Scenes/Network/NetworkPool_v1_2.unity", OpenSceneMode.Single);
            SessionState.SetBool(Pending, true);
            Subscribe();
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Perf check timed out at phase " + phase);
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
                    if (player == null || round == null || !round.IsClientInitialized) return;
                    round.StartRound(); until = now + 1; phase = 2; return;
                }
                if (phase == 2 && now >= until)
                {
                    player.GetComponent<PlayerMovement>().enabled = false;
                    player.GetComponent<PlayerInteractor>().enabled = false;
                    // Stable darkness for measurements and pictures: stop the microphone early (the voice light
                    // reacts to room noise and would otherwise keep its last brightness).
                    foreach (var device in Microphone.devices) if (Microphone.IsRecording(device)) Microphone.End(device);
                    player.GetComponent<PlayerMovement>().Teleport(new Vector3(1, 1.05f, 40));
                    Physics.SyncTransforms();
                    until = now + 1.5; phase = 3; return;
                }
                var rings = player != null ? player.GetComponentInChildren<WalkingFootRings>() : null;
                if (rings == null) return;
                if (phase == 3 && now >= until)
                {
                    rings.ClearPulses();
                    idle.Clear(); lastFrame = Time.frameCount;
                    until = now + IdleSeconds; phase = 4; return;
                }
                if (phase == 4)
                {
                    Sample(idle);
                    if (now < until) return;
                    Burst(player, rings);
                    alive.Clear(); lastFrame = Time.frameCount;
                    until = now + AliveSeconds; phase = 5; return;
                }
                if (phase == 5)
                {
                    Sample(alive);
                    if (now < until) return;
                    aliveAfter.Add(ActiveCount(rings));
                    trial++;
                    if (trial < Trials) { until = now + GapSeconds; phase = 6; return; }
                    Report(rings);
                    rings.ClearPulses();
                    until = now + GapSeconds; phase = 20; return;
                }
                // Phase C direction hints (host): 30 hidden ring-display sounds at once through the real SoundTarget,
                // plus one remote speaker's blocked voice packets at 50 per second for 5 s through the voice hook.
                if (phase == 20 && now >= until)
                {
                    SetHintSwitch(true);
                    hintIdle.Clear(); lastFrame = Time.frameCount;
                    until = now + 2; phase = 21; return;
                }
                if (phase == 21)
                {
                    Sample(hintIdle);
                    if (now < until) return;
                    HiddenBurst(round, player);
                    hintLoad.Clear(); lastFrame = Time.frameCount;
                    voiceStart = now; voiceSent = 0; voiceBytes = 0; voiceLookups = -1;
                    until = now + HintSeconds; phase = 22; return;
                }
                if (phase == 22)
                {
                    Sample(hintLoad);
                    PumpVoice(player, now);
                    if (now < until) return;
                    ReportHints();
                    SetHintSwitch(false);
                    // Optional look at the rings: set MARCO_RIPPLE_SHOT to a folder outside the project.
                    shotDirectory = Environment.GetEnvironmentVariable("MARCO_RIPPLE_SHOT");
                    if (string.IsNullOrEmpty(shotDirectory)) { Finish(0); return; }
                    rings.ClearPulses();
                    until = now + GapSeconds; phase = 8; return;
                }
                if (phase == 8 && now >= until)
                {
                    Showcase(player, rings); until = now + .35; phase = 9; return;
                }
                if (phase == 9 && now >= until) { Capture(player, "ripple_early.png"); until = now + .5; phase = 10; return; }
                if (phase == 10 && now >= until) { Capture(player, "ripple_late.png"); until = now + 1.5; phase = 11; return; }
                if (phase == 11 && now >= until)
                {
                    int glows = 0;
                    foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                        if (renderer.name == "Ripple Afterglow" && renderer.gameObject.activeInHierarchy) glows++;
                    Debug.Log($"[RipplePerfChecks] RESULT own-ring afterglows alive 2.2s after the showcase: {glows}");
                    Capture(player, "ripple_afterglow.png");
                    Finish(0); return;
                }
                if (phase == 6 && now >= until) { rings.ClearPulses(); lastFrame = Time.frameCount; until = now + AliveSeconds; phase = 7; return; }
                if (phase == 7)
                {
                    Sample(idle);
                    if (now < until) return;
                    Burst(player, rings);
                    alive.Clear(); lastFrame = Time.frameCount;
                    until = now + AliveSeconds; phase = 5;
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }

        // The same 30 requests every run: 26 voice-grade rings, 3 remote sprint floor rings, 1 drowning pulse.
        static void Burst(NetworkPlayer player, WalkingFootRings rings)
        {
            Vector3 feet = player.transform.position;
            feet.y = player.GetComponent<CharacterController>().bounds.min.y + .025f;
            int before = Queries(rings);
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 30; i++)
            {
                float angle = i * Mathf.PI * 2f / 30f;
                var offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (2f + i % 5);
                if (i < 10) rings.ShowSound(feet + offset + Vector3.up * .6f, 22f, 2.5f, i % 2 == 0);
                else if (i < 20) rings.ShowSound(feet + offset + Vector3.up * .6f, 9f, 1.2f, i % 2 == 0);
                else if (i < 26) rings.ShowSound(feet + offset + Vector3.up * .6f, 4f, .6f, false);
                else if (i < 29) rings.Show(90000 + i, feet + offset, true, i % 2 == 0);
                else rings.EmitWaterPulse(feet + offset, 22f);
            }
            watch.Stop();
            spawnMs.Add(watch.Elapsed.TotalMilliseconds);
            int after = Queries(rings);
            spawnQueries.Add(before < 0 || after < 0 ? -1 : after - before);
            queuedFrames.Add(QueueStat(rings));
            // Own rings right after the burst, when this frame's raycast budget is already spent: both must
            // be created inside the call (not queued). Outside the timed/counted part above.
            int ownBefore = OwnCreated(rings);
            bool seeker = player.Role == Marco.Core.WinCondition.PlayerRole.Seeker;
            rings.ShowSound(player.transform.position + Vector3.up * .6f, 9f, 1.2f, seeker);
            rings.Show(player.PlayerId, feet, true, seeker);
            ownSameFrame.Add(OwnCreated(rings) - ownBefore);
        }

        static string shotDirectory;

        // Own voice ring (local, weakened), a Seeker shout, a Runner talk ring and a remote Sprint floor ring.
        static void Showcase(NetworkPlayer player, WalkingFootRings rings)
        {
            var body = player.transform;
            Vector3 feet = body.position;
            feet.y = player.GetComponent<CharacterController>().bounds.min.y + .025f;
            // The test spot is next to the west wall: look and place rings east, into the map.
            Vector3 forward = Vector3.right, right = Vector3.Cross(Vector3.up, forward);
            rings.ShowSound(body.position + Vector3.up * .6f, 9f, 1.2f, false);
            rings.ShowSound(feet + forward * 7f + Vector3.up * 1.5f, 22f, 2.5f, true);
            rings.ShowSound(feet + right * 5f + forward * 4f + Vector3.up * 1.5f, 9f, 1.2f, false);
            rings.Show(90123, feet + forward * 3f - right * 2f, true, false);
            Marco.Network.Escape.TemporaryExitPulseRings.ShowAtAllExits();
            int exits = 0;
            foreach (var _ in Marco.Network.Escape.ExitView.All) exits++;
            Debug.Log($"[RipplePerfChecks] exits in scene: {exits}");
        }

        static void Capture(NetworkPlayer player, string file)
        {
            var camera = player.GetComponentInChildren<Camera>();
            if (camera == null) { Debug.LogWarning("[RipplePerfChecks] no player camera for screenshot"); return; }
            Describe(player.GetComponentInChildren<WalkingFootRings>(), camera);
            var previousRotation = camera.transform.rotation;
            camera.transform.rotation = Quaternion.Euler(32f, 90f, 0f);
            var texture = new RenderTexture(1280, 720, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                System.IO.Directory.CreateDirectory(shotDirectory);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(shotDirectory, file), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                Debug.Log("[RipplePerfChecks] screenshot " + file);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                camera.transform.rotation = previousRotation;
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        // What is alive at screenshot time: name, position, shader, bounds, reach range and current alpha.
        static void Describe(WalkingFootRings rings, Camera camera)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            if (rings == null || !(typeof(WalkingFootRings).GetField("rings", flags)?.GetValue(rings) is IList list)) return;
            int exitRings = 0;
            foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (renderer.name == "Temporary Exit Pulse Ring" && renderer.gameObject.activeInHierarchy) exitRings++;
            Debug.Log($"[RipplePerfChecks] camera {camera.name} at {camera.transform.position} rings={list.Count} exitRings={exitRings}");
            foreach (var ring in list)
            {
                var type = ring.GetType();
                var go = (GameObject)type.GetField("Object").GetValue(ring);
                var mesh = (Mesh)type.GetField("Mesh").GetValue(ring);
                var renderer = (Renderer)type.GetField("Renderer").GetValue(ring);
                var block = (MaterialPropertyBlock)type.GetField("Properties").GetValue(ring);
                var shape = type.GetField("Shape")?.GetValue(ring);
                string reach = "n/a";
                if (shape != null)
                {
                    var values = (float[])shape.GetType().GetField("Reach").GetValue(shape);
                    int count = (int)shape.GetType().GetField("RayCount").GetValue(shape);
                    float min = float.MaxValue, max = 0;
                    for (int i = 0; i < count; i++) { min = Mathf.Min(min, values[i]); max = Mathf.Max(max, values[i]); }
                    reach = $"rays={count} reach={min:0.00}..{max:0.00} water={shape.GetType().GetField("Water").GetValue(shape)}";
                }
                // Quads whose two ends' wavefronts differ by ≥ JoinEnd (0.6m) at the current radius: before the
                // corner fix these drew radial lines; now the shader fades them (join = 0).
                float radius = block.GetFloat("_Radius");
                var rayData = new List<Vector4>(); var otherData = new List<Vector4>();
                mesh.GetUVs(0, rayData); mesh.GetUVs(5, otherData);
                int radialQuads = 0;
                if (otherData.Count == rayData.Count)
                    for (int v = 0; v + 3 < rayData.Count; v += 4)
                        if (Mathf.Abs(Mathf.Min(radius, rayData[v].z) - Mathf.Min(radius, otherData[v].x)) >= .6f) radialQuads++;
                reach += $" radialQuads={radialQuads}";
                var bounds = renderer.bounds;
                Debug.Log($"[RipplePerfChecks] ring {go.name} active={go.activeSelf} at {go.transform.position} shader={renderer.sharedMaterial?.shader?.name} " +
                    $"verts={mesh.vertexCount} bounds={bounds.center}/{bounds.size} {reach} color={block.GetColor("_BaseColor")} radius={block.GetFloat("_Radius")} " +
                    $"visible={GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(camera), bounds)}");
            }
        }

        static void Sample(List<float> into)
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            into.Add(Time.unscaledDeltaTime * 1000f);
        }

        static int OwnCreated(WalkingFootRings rings) =>
            typeof(WalkingFootRings).GetProperty("OwnRingsCreated", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(rings) is int count ? count : -1;

        static int ActiveCount(WalkingFootRings rings)
        {
            var field = typeof(WalkingFootRings).GetField("rings", BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(rings) is IList list ? list.Count : -1;
        }

        // Visual raycasts issued through the renderer's SoundWorld helper (-1 if not readable).
        static int Queries(WalkingFootRings rings)
        {
            var world = typeof(WalkingFootRings).GetField("world", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(rings);
            var property = world?.GetType().GetProperty("VisualQueryCount");
            return property?.GetValue(world) is int count ? count : -1;
        }

        // Optional renderer statistic: worst number of frames a ring waited in the build queue.
        static string QueueStat(WalkingFootRings rings)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var max = typeof(WalkingFootRings).GetProperty("MaxQueuedFrames", flags)?.GetValue(rings);
            var late = typeof(WalkingFootRings).GetProperty("DelayedOverTwoFrames", flags)?.GetValue(rings);
            return max == null ? "n/a" : $"{max} (rings waiting > 2 frames so far: {late})";
        }

        static string Stats(List<float> values)
        {
            if (values.Count == 0) return "no frames";
            var sorted = new List<float>(values);
            sorted.Sort();
            double sum = 0;
            foreach (float v in sorted) sum += v;
            float p95 = sorted[Mathf.Min(sorted.Count - 1, Mathf.CeilToInt(sorted.Count * .95f) - 1)];
            return $"frames={sorted.Count} mean={sum / sorted.Count:0.00}ms p95={p95:0.00}ms max={sorted[sorted.Count - 1]:0.00}ms";
        }

        static void Report(WalkingFootRings rings)
        {
            Debug.Log($"[RipplePerfChecks] RESULT idle(0 rings): {Stats(idle)}");
            Debug.Log($"[RipplePerfChecks] RESULT alive(30 requests): {Stats(alive)}");
            Debug.Log($"[RipplePerfChecks] RESULT spawn ms per trial: {string.Join(", ", spawnMs.ConvertAll(v => v.ToString("0.00")))}");
            Debug.Log($"[RipplePerfChecks] RESULT visual raycasts per trial: {string.Join(", ", spawnQueries)}");
            Debug.Log($"[RipplePerfChecks] RESULT active rings 1s after spawn: {string.Join(", ", aliveAfter)}");
            Debug.Log($"[RipplePerfChecks] RESULT max queued frames: {string.Join(", ", queuedFrames)}");
            Debug.Log($"[RipplePerfChecks] RESULT queue after all trials: {QueueStat(rings)}");
            var maxOwnWait = typeof(WalkingFootRings).GetProperty("MaxOwnWaitFrames", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(rings);
            Debug.Log($"[RipplePerfChecks] RESULT own rings created in the request frame (of 2, budget spent): {string.Join(", ", ownSameFrame)}; own ring max wait frames: {maxOwnWait ?? "n/a"}");
            if (maxOwnWait is int wait && wait != 0) throw new Exception("Own ring waited " + wait + " frames");
            foreach (int created in ownSameFrame) if (created != 2) throw new Exception("Own ring was not created in the request frame");
            Debug.Log("[RipplePerfChecks] PASS: measurement finished (batch editor play mode; GPU cost is not representative). No scene saved.");
        }

        // ---- Phase C direction hint load (reflection: measured before and after the hint system exists) ----
        const double HintSeconds = 5;
        static readonly List<float> hintIdle = new(), hintLoad = new();
        static double voiceStart;
        static int voiceSent, hiddenSent, shownBefore, lookupsBefore, voiceLookups;
        static long voiceBytes;
        static Action<int, Vector3, bool> voiceHook;
        static Type HintType => Type.GetType("Marco.Network.Vision.DirectionHints, Marco.Network");

        static void SetHintSwitch(bool on) =>
            Type.GetType("Marco.Network.Vision.RippleClientFeatures, Marco.Network")?.GetField("DirectionHint")?.SetValue(null, on);

        static int HintCounter(string name) =>
            HintType?.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null) is int value ? value : -1;

        static void HiddenBurst(NetworkRound round, NetworkPlayer player)
        {
            var target = typeof(NetworkRound).GetMethod("SoundTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            shownBefore = HintCounter("Shown");
            for (int i = 0; i < 30; i++)
            {
                float angle = i * Mathf.PI * 2f / 30f;
                var point = player.transform.position + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * 2f;
                target.Invoke(round, new object[] { player.Owner, "", point, .5f, 1f, -1, true, -1, false, true, 0f, 2.5f });
            }
            hiddenSent = 30;
            var method = HintType?.GetMethod("OnVoice", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            voiceHook = method != null ? (Action<int, Vector3, bool>)Delegate.CreateDelegate(typeof(Action<int, Vector3, bool>), method) : null;
            if (voiceHook != null) voiceHook(77, player.transform.position + Vector3.right * 2f, true); // warm-up: JIT and the speaker's state
        }

        // Blocked voice packets of one remote speaker at 50 per second; bytes allocated inside the hook are summed.
        static void PumpVoice(NetworkPlayer player, double now)
        {
            if (voiceHook == null) return;
            if (voiceLookups < 0) { voiceLookups = 0; lookupsBefore = HintCounter("Lookups"); }
            int due = (int)((now - voiceStart) * 50);
            var point = player.transform.position + Vector3.right * 2f;
            while (voiceSent < due)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                voiceHook(77, point, true);
                voiceBytes += GC.GetAllocatedBytesForCurrentThread() - before;
                voiceSent++;
            }
        }

        static void ReportHints()
        {
            long probe = GC.GetAllocatedBytesForCurrentThread();
            var sink = new byte[1024];
            bool counterWorks = GC.GetAllocatedBytesForCurrentThread() - probe >= 1024 && sink.Length == 1024;
            Debug.Log($"[RipplePerfChecks] RESULT hint idle (switch on, nothing hidden): {Stats(hintIdle)}");
            Debug.Log($"[RipplePerfChecks] RESULT hint load (30 hidden SoundTargets at once + blocked voice 50/s for {HintSeconds:0}s): {Stats(hintLoad)}");
            if (HintType == null) { Debug.Log("[RipplePerfChecks] RESULT hint system: absent (baseline — SoundTarget cost only)"); return; }
            int shown = HintCounter("Shown") - shownBefore, lookups = HintCounter("Lookups") - lookupsBefore;
            float perCall = voiceSent > 0 ? lookups / (float)voiceSent : 0;
            Debug.Log($"[RipplePerfChecks] RESULT hints lit by the hidden burst + voice: {shown} (hidden sent {hiddenSent}, merged by octant and source)");
            Debug.Log($"[RipplePerfChecks] RESULT voice hook: {voiceSent} calls, allocated {voiceBytes} bytes (counter works: {counterWorks}), dictionary lookups per call {perCall:0.00}");
            if (counterWorks && voiceBytes != 0) throw new Exception($"Voice hint path allocated {voiceBytes} bytes over {voiceSent} calls (must be 0)");
            if (voiceSent > 0 && perCall > 1.0001f) throw new Exception($"Voice hint path did {perCall:0.00} dictionary lookups per call (must be ≤ 1)");
        }

        static void Finish(int code)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(code);
        }
    }
}
