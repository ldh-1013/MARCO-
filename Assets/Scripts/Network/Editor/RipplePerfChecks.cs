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

        static RipplePerfChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
        static void Subscribe()
        {
            deadline = EditorApplication.timeSinceStartup + 150;
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
                    Spawn(player, rings);
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
                    Report();
                    Finish(0);
                    return;
                }
                if (phase == 6 && now >= until) { rings.ClearPulses(); lastFrame = Time.frameCount; until = now + AliveSeconds; phase = 7; return; }
                if (phase == 7)
                {
                    Sample(idle);
                    if (now < until) return;
                    Spawn(player, rings);
                    alive.Clear(); lastFrame = Time.frameCount;
                    until = now + AliveSeconds; phase = 5;
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }

        // The same 30 requests every run: 26 voice-grade rings, 3 remote sprint floor rings, 1 drowning pulse.
        static void Spawn(NetworkPlayer player, WalkingFootRings rings)
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
        }

        static void Sample(List<float> into)
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            into.Add(Time.unscaledDeltaTime * 1000f);
        }

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
            var property = typeof(WalkingFootRings).GetProperty("MaxQueuedFrames", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return property?.GetValue(rings)?.ToString() ?? "n/a";
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

        static void Report()
        {
            Debug.Log($"[RipplePerfChecks] RESULT idle(0 rings): {Stats(idle)}");
            Debug.Log($"[RipplePerfChecks] RESULT alive(30 requests): {Stats(alive)}");
            Debug.Log($"[RipplePerfChecks] RESULT spawn ms per trial: {string.Join(", ", spawnMs.ConvertAll(v => v.ToString("0.00")))}");
            Debug.Log($"[RipplePerfChecks] RESULT visual raycasts per trial: {string.Join(", ", spawnQueries)}");
            Debug.Log($"[RipplePerfChecks] RESULT active rings 1s after spawn: {string.Join(", ", aliveAfter)}");
            Debug.Log($"[RipplePerfChecks] RESULT max queued frames: {string.Join(", ", queuedFrames)}");
            Debug.Log("[RipplePerfChecks] PASS: measurement finished (batch editor play mode; GPU cost is not representative). No scene saved.");
        }

        static void Finish(int code)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(code);
        }
    }
}
