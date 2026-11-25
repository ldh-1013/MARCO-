using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FishNet.Connection;
using Marco.Core.WinCondition;
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
    // Phase C (기획서 §5.4 방향 힌트): ring-display sounds whose ring is hidden by occlusion light a screen-edge
    // hint instead. Runs on a LAN host in the main scene NetworkPool_v1_2 with the host player as the listener and
    // sends real SoundTarget calls from the server. The hint system is found by reflection, so the same check runs
    // before it exists (baseline: hidden rings stay hidden, no hint), before it is wired (hint count stays 0) and
    // after (switch on: hints; switch off: none). Server exceptions and errors fail the check. No scene is saved.
    [InitializeOnLoad]
    public static class DirectionHintChecks
    {
        const string Pending = "Marco.DirectionHintChecks", Scene = "Assets/Scenes/Network/NetworkPool_v1_2.unity";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int phase, step, shownBefore, callsBefore, faultLogs;
        static double deadline, until;
        static bool switchOn, expectFault;
        static readonly List<string> results = new(), failures = new(), errors = new();
        static Vector3 sourcePoint, hostSpot;
        static ValveId valveId;
        static int expectedOctant;

        static Type Hints => Type.GetType("Marco.Network.Vision.DirectionHints, Marco.Network");
        static Type Features => Type.GetType("Marco.Network.Vision.RippleClientFeatures, Marco.Network");
        static bool HasHints => Hints != null && Features != null;

        static DirectionHintChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
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
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            SessionState.SetBool(Pending, true);
            Subscribe();
            EditorApplication.EnterPlaymode();
        }

        static void Collect(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (expectFault && message.Contains("DirectionHint fault")) { faultLogs++; return; }
            if (errors.Count < 40) errors.Add(type + ": " + message.Split('\n')[0]);
        }

        static void Expect(bool condition, string item, string detail)
        {
            results.Add((condition ? "ok   " : "FAIL ") + item + ": " + detail);
            if (!condition) failures.Add(item + ": " + detail);
        }

        // ---- hint system access (reflection: the check compiles before the system exists) ----
        static void SetSwitch(bool on) { Features?.GetField("DirectionHint", Flags)?.SetValue(null, on); switchOn = on; }
        static int Read(string name) => Hints?.GetProperty(name, Flags)?.GetValue(null) is int value ? value : 0;
        static int Shown => Read("Shown");
        static int Calls => Read("SoundCalls");
        static int LastOctant => HasHints ? Read("LastOctant") : -1;
        static void SetFault(bool on) => Hints?.GetField("FaultForChecks", Flags)?.SetValue(null, on);
        // Wired = the SoundTarget hook calls into the hint system (counts calls whatever the switch).
        static bool Wired => Calls > callsBefore;
        static bool ExpectHints => HasHints && switchOn && Wired;

        static void Mark() { shownBefore = Shown; callsBefore = Calls; }
        static int ShownSince => Shown - shownBefore;

        static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Direction hint check timed out at phase " + phase + " step " + step);
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
                    // State D — lobby: a hidden ring-display sound before the round must not light a hint.
                    SetSwitch(true);
                    Mark();
                    Send(round, player, "", player.transform.position + Vector3.right * 2f, -1, muffled: true, radius: 0f, seconds: 2.5f);
                    until = now + .6; phase = 2; return;
                }
                if (phase == 2 && now >= until)
                {
                    Expect(ShownSince == 0, "D lobby (no round)", $"hints {ShownSince} (wired={Wired}, system={(HasHints ? "present" : "absent")})");
                    round.StartRound(); until = now + 1.5; phase = 3; return;
                }
                if (phase == 3 && now >= until)
                {
                    player.GetComponent<PlayerMovement>().enabled = false;
                    player.GetComponent<PlayerInteractor>().enabled = false;
                    foreach (var device in Microphone.devices) if (Microphone.IsRecording(device)) Microphone.End(device);
                    step = 0; phase = 4; return;
                }
                if (phase == 4)
                {
                    if (RunStep(round, player, now)) return;
                    phase = 9; return;
                }
                if (phase == 9)
                {
                    SetSwitch(false);
                    SetFault(false);
                    foreach (var line in results) Debug.Log("[DirectionHintChecks] RESULT " + line);
                    foreach (var line in errors) Debug.Log("[DirectionHintChecks] LOG " + line);
                    if (errors.Count > 0) failures.Add($"{errors.Count} exception/error log(s) during the run");
                    if (failures.Count > 0) throw new Exception("FAIL: " + string.Join(" | ", failures));
                    Debug.Log($"[DirectionHintChecks] PASS in {Scene} (hint system {(HasHints ? "present" : "absent — baseline")}). No scene saved.");
                    Finish(0);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }

        // Steps run twice: switch ON (steps 0..) then switch OFF (steps 50..) for the hidden cases.
        static bool RunStep(NetworkRound round, NetworkPlayer player, double now)
        {
            var rings = player.GetComponentInChildren<WalkingFootRings>();
            if (now < until) return true;
            switch (step)
            {
                case 0: // hidden valve loop (wall between): ring hidden, hint on the valve's octant, again after 2.5 s
                case 50:
                    {
                        bool on = step == 0;
                        SetSwitch(on);
                        if (!FindHiddenValve(round, player, out valveId, out sourcePoint, out hostSpot))
                        {
                            // Active valves are random: when none has a walled spot in range, the same loop is sent directly.
                            results.Add($"hidden valve ({(on ? "on" : "off")}): no active valve with a walled spot in range this round - synthetic muffled loop ticks instead");
                            sourcePoint = player.transform.position + Vector3.forward * 2f;
                            expectedOctant = 0;
                            rings.ClearPulses();
                            Mark();
                            loopStart = now; firstChecked = false;
                            step = on ? 5 : 55; return true;
                        }
                        expectedOctant = ServerOctant(sourcePoint, hostSpot);
                        rings.ClearPulses();
                        Mark();
                        Invoke(round, "ValveAudio", valveId, ValveSoundEvent.RotatingStarted);
                        until = now + 1.0; step++; return true;
                    }
                case 1:
                case 51:
                    {
                        bool on = step == 1;
                        int first = ShownSince;
                        Expect(RingsNear(rings, sourcePoint, 1f) == 0, $"Hidden valve {valveId} ring ({(on ? "on" : "off")})", $"world rings at the valve: {RingsNear(rings, sourcePoint, 1f)} (expected 0 — wall)");
                        if (on && ExpectHints)
                            Expect(first == 1 && LastOctant == expectedOctant, $"Hidden valve {valveId} hint (on)",
                                $"hints {first}, octant {LastOctant} (server snap {expectedOctant}), host at {hostSpot}");
                        else Expect(first == 0, $"Hidden valve hint ({(on ? "on, not wired" : "off")})", $"hints {first}");
                        until = now + 2.8; step++; return true;
                    }
                case 2:
                case 52:
                    {
                        bool on = step == 2;
                        if (on && ExpectHints) Expect(ShownSince == 2, "Hidden valve loop repeat (2.5 s)", $"hints after 3.8 s: {ShownSince} (expected 2)");
                        else Expect(ShownSince == 0, $"Hidden valve loop repeat ({(on ? "on, not wired" : "off")})", $"hints {ShownSince}");
                        Invoke(round, "ValveAudio", valveId, ValveSoundEvent.RotatingStopped);
                        until = now + .5; step = on ? 10 : 60; return true;
                    }
                case 5: // synthetic hidden loop: muffled 0-radius "Valve" ticks every 0.25 s, like a turning valve behind a wall
                case 55:
                    {
                        bool on = step == 5;
                        Send(round, player, "Valve", sourcePoint, 100009, muffled: true, radius: 0f, seconds: 8f);
                        if (!firstChecked && now >= loopStart + 1.0)
                        {
                            firstChecked = true;
                            if (on && ExpectHints) Expect(ShownSince == 1 && LastOctant == expectedOctant, "Hidden loop hint (on, synthetic)", $"hints {ShownSince}, octant {LastOctant} (expected {expectedOctant})");
                            else Expect(ShownSince == 0, $"Hidden loop hint ({(on ? "on, not wired" : "off")}, synthetic)", $"hints {ShownSince}");
                        }
                        if (now >= loopStart + 3.8)
                        {
                            if (on && ExpectHints) Expect(ShownSince == 2, "Hidden loop repeat (2.5 s, synthetic)", $"hints after 3.8 s: {ShownSince} (expected 2)");
                            else Expect(ShownSince == 0, $"Hidden loop repeat ({(on ? "on, not wired" : "off")}, synthetic)", $"hints {ShownSince}");
                            step = on ? 10 : 60; until = now + .5; return true;
                        }
                        until = now + .25; return true;
                    }
                case 10: // hidden gate pulses S5 (wall between the host and an exit)
                case 60:
                    {
                        bool on = step == 10;
                        SetSwitch(on);
                        ExitView exit = null;
                        foreach (var candidate in ExitView.All) if (candidate != null) { exit = candidate; break; }
                        if (exit == null || !FindHiddenSpot(round, player, exit.transform.position, PulseSpec.Shout, out hostSpot))
                        { results.Add($"skip hidden gate ({(on ? "on" : "off")}): no walled spot near an exit"); step = on ? 20 : 70; return true; }
                        sourcePoint = exit.transform.position;
                        expectedOctant = ServerOctant(sourcePoint, hostSpot);
                        rings.ClearPulses();
                        Mark();
                        Invoke(round, "EmitGatePulses");
                        until = now + .6; step++; return true;
                    }
                case 11:
                case 61:
                    {
                        bool on = step == 11;
                        Expect(RingsNear(rings, sourcePoint, 1f) == 0, $"Hidden gate ring ({(on ? "on" : "off")})", $"world rings at the exit: {RingsNear(rings, sourcePoint, 1f)} (expected 0)");
                        if (on && ExpectHints)
                            Expect(ShownSince >= 1 && ShownSince <= 2, "Hidden gate hint (on)", $"hints {ShownSince} (one per heard exit), last octant {LastOctant}, near exit snap {expectedOctant}");
                        else Expect(ShownSince == 0, $"Hidden gate hint ({(on ? "on, not wired" : "off")})", $"hints {ShownSince}");
                        if (on && ExpectHints) OverlayCheck(player);
                        step = on ? 20 : 70; until = now + .3; return true;
                    }
                case 20: // visible ring (S4 tag at the host): ring yes, hint no
                    {
                        SetSwitch(true);
                        rings.ClearPulses();
                        Mark();
                        Invoke(round, "EmitTagPulse", player);
                        until = now + .6; step = 21; return true;
                    }
                case 21:
                    {
                        float radius = RingRadiusAt(rings, player.transform.position);
                        Expect(radius > 0 && ShownSince == 0, "Visible ring gets no hint", $"ring {radius}m, hints {ShownSince}");
                        step = 30; return true;
                    }
                case 30: // hidden underwater valve rule: a ring tick then muffled 0-radius ticks for 2 s → no hint; later → hint
                    {
                        rings.ClearPulses();
                        Mark();
                        var point = player.transform.position + new Vector3(6f, 0, 0);
                        Send(round, player, "ValveUnderwater", point, 100002, muffled: true, radius: 12f, seconds: 2.5f);
                        until = now + .25; step = 31; tick = 0; return true;
                    }
                case 31:
                    {
                        var point = player.transform.position + new Vector3(6f, 0, 0);
                        if (tick++ < 8) { Send(round, player, "ValveUnderwater", point, 100002, muffled: true, radius: 0f, seconds: 2.5f); until = now + .25; return true; }
                        Expect(ShownSince == 0, "Underwater valve between its rings", $"hints {ShownSince} during 2 s of ring-less ticks after a ring (expected 0)");
                        until = now + 1.0; step = 32; return true;
                    }
                case 32:
                    {
                        var point = player.transform.position + new Vector3(6f, 0, 0);
                        Send(round, player, "ValveUnderwater", point, 100002, muffled: true, radius: 0f, seconds: 2.5f);
                        until = now + .4; step = 33; return true;
                    }
                case 33:
                    {
                        if (ExpectHints) Expect(ShownSince == 1, "Underwater valve hidden (no ring for 2.6 s)", $"hints {ShownSince} (expected 1)");
                        else Expect(ShownSince == 0, "Underwater valve hidden (not wired / absent)", $"hints {ShownSince}");
                        step = 40; return true;
                    }
                case 40: // fault inside the hint path: the ring (played before the hint call) still appears, one log per kind
                    {
                        if (!HasHints) { results.Add("skip fault injection: hint system absent"); step = 45; return true; }
                        rings.ClearPulses();
                        faultLogs = 0; expectFault = true;
                        SetFault(true);
                        Mark();
                        var point = player.transform.position + new Vector3(0, 0, 3f);
                        for (int i = 0; i < 5; i++) Send(round, player, "", point + Vector3.right * i * .01f, -1, muffled: false, radius: 9f, seconds: 2.5f);
                        // The first logged exception is slow in the batch editor (stack trace), so the queued rings get time to build.
                        until = now + 1.5; step = 41; return true;
                    }
                case 41:
                    {
                        // The editor's Error Pause stops play mode when the deliberate exception is logged (a built player
                        // has no such pause): note it, resume, and give the queued rings frames to build.
                        SetFault(false);
                        expectFault = false;
                        faultPaused = EditorApplication.isPaused;
                        EditorApplication.isPaused = false;
                        until = now + 1.0; step = 42; return true;
                    }
                case 42:
                    {
                        var point = player.transform.position + new Vector3(0, 0, 3f);
                        int ringsShown = RingsNear(rings, point, .5f);
                        var queue = typeof(WalkingFootRings).GetField("queue", Flags)?.GetValue(rings) as IList;
                        var diag = new System.Text.StringBuilder();
                        foreach (var ring in Rings(rings)) { var t = ring.GetType(); var g = (GameObject)t.GetField("Object").GetValue(ring); diag.Append($"[{g.transform.position} r={t.GetField("Radius").GetValue(ring)} sound={t.GetField("Sound").GetValue(ring)}] "); }
                        var asm = typeof(WalkingFootRings).Assembly;
                        var budget = asm.GetType("Marco.Network.Vision.RippleBudget")?.GetProperty("Remaining", Flags)?.GetValue(null);
                        var world = typeof(WalkingFootRings).GetField("world", Flags)?.GetValue(rings);
                        var estimate = asm.GetType("Marco.Network.Vision.RippleReach")?.GetMethod("Estimate", Flags)?.Invoke(null, new object[] { world, point, 9f });
                        Debug.Log($"[DirectionHintChecks] DIAG fault rings: point {point}, player {player.transform.position}, queue {queue?.Count}, budget remaining {budget}, estimate {estimate}, frame {Time.frameCount}, rings {diag}");
                        if (Wired) Expect(ringsShown == 5 && faultLogs == 1, "Hint fault does not touch sound or ring", $"rings {ringsShown}/5 still shown, exception logs {faultLogs} (expected 1 — once per kind), editor error pause hit: {faultPaused}");
                        else Expect(ringsShown == 5 && faultLogs == 0, "Hint fault (not wired)", $"rings {ringsShown}/5, exception logs {faultLogs}");
                        step = 45; return true;
                    }
                case 45: // the overlay itself (direct call, wired or not): a hidden sound ahead lights the top edge, behind the bottom
                case 47:
                    {
                        var onSound = Hints?.GetMethod("OnSound", Flags);
                        if (onSound == null) { results.Add("skip overlay: hint system absent"); step = 50; return true; }
                        bool ahead = step == 45;
                        // Hints still held from earlier steps (a turning valve holds for its whole rotation) would hide the band under test.
                        var channel = Hints.GetField("Hint", Flags)?.GetValue(null);
                        channel?.GetType().GetMethod("Clear")?.Invoke(channel, null);
                        var view = player.GetComponentInChildren<Camera>(true);
                        var forward = Vector3.ProjectOnPlane(view != null ? view.transform.forward : player.transform.forward, Vector3.up).normalized;
                        var point = player.transform.position + forward * (ahead ? 3f : -3f);
                        Debug.Log($"[DirectionHintChecks] DIAG before direct call: shown {Shown}, extended {Read("Extended")}, last {LastOctant}, soundCalls {Calls}, voiceCalls {Read("VoiceCalls")}");
                        Mark();
                        onSound.Invoke(null, new object[] { "", point, true, -1, 9000 + step, true, 0f, 2.5f });
                        until = now + .3; step++; return true;
                    }
                case 46:
                case 48:
                    {
                        bool ahead = step == 46;
                        Debug.Log($"[DirectionHintChecks] DIAG after direct call: shown {Shown}, extended {Read("Extended")}, last {LastOctant}, soundCalls {Calls}, voiceCalls {Read("VoiceCalls")}");
                        Expect(ShownSince == 1, $"Overlay direct call ({(ahead ? "ahead" : "behind")})", $"hints {ShownSince}");
                        OverlayCheck(player);
                        var side = LitSide();
                        Expect(ahead ? side == "top" : side == "bottom", $"Overlay edge ({(ahead ? "ahead → top" : "behind → bottom")})", $"brightest band on the {side} edge");
                        if (ahead) { until = now + 3.2; step = 47; return true; }   // let the first band fade before the second
                        step = 80; until = now + .6; return true;
                    }
                case 80: // state D, live: a control hint while alive, then none as Echo, escaped, spectating, or for my own sound
                    {
                        if (!ExpectHints && !(HasHints && Calls > 0)) { results.Add("skip state D live: hint system absent or not wired"); step = 50; return true; }
                        SetSwitch(true);
                        Mark();
                        Send(round, player, "", player.transform.position + Vector3.right * 2f, -1, muffled: true, radius: 0f, seconds: 2.5f);
                        until = now + .5; step = 81; return true;
                    }
                case 81:
                    Expect(ShownSince == 1, "D control (alive, in a round)", $"hints {ShownSince} (expected 1)");
                    statusIndex = 0; step = 82; return true;
                case 82:
                    if (statusIndex >= DStatuses.Length) { step = 85; return true; }
                    player.SetStatus(DStatuses[statusIndex]);
                    until = now + .3; step = 83; return true;
                case 83:
                    {
                        Mark();
                        var offset = Quaternion.Euler(0, 90f * (statusIndex + 1), 0) * Vector3.forward * 2f;
                        Send(round, player, "", player.transform.position + offset, -1, muffled: true, radius: 0f, seconds: 2.5f);
                        until = now + .5; step = 84; return true;
                    }
                case 84:
                    {
                        var overlay = GameObject.Find("Direction Hint Overlay");
                        bool canvasOff = overlay == null || !overlay.GetComponent<Canvas>().enabled;
                        var status = DStatuses[statusIndex];
                        Expect(ShownSince == 0 && canvasOff, $"D {status}{(status == PlayerStatus.Tagged ? " (Echo)" : "")}", $"hints {ShownSince}, overlay canvas {(canvasOff ? "off" : "on")}");
                        player.SetStatus(PlayerStatus.Alive);
                        statusIndex++;
                        until = now + .3; step = 82; return true;
                    }
                case 85:
                    {
                        Mark();
                        Send(round, player, "", player.transform.position + new Vector3(-1.5f, 0, -1.5f), -1, muffled: true, radius: 0f, seconds: 2.5f, fromOther: false);
                        until = now + .5; step = 86; return true;
                    }
                case 86:
                    Expect(ShownSince == 0, "D own sound", $"hints {ShownSince}");
                    step = 50; return true;
                default:
                    return false;
            }
        }

        static int tick;
        static double loopStart;
        static int statusIndex;
        static readonly PlayerStatus[] DStatuses = { PlayerStatus.Tagged, PlayerStatus.Escaped, PlayerStatus.Spectating };
        static bool faultPaused;
        static bool firstChecked;

        static void OverlayCheck(NetworkPlayer player)
        {
            var overlay = GameObject.Find("Direction Hint Overlay");
            var canvas = overlay != null ? overlay.GetComponent<Canvas>() : null;
            int lit = 0;
            if (overlay != null)
                foreach (var renderer in overlay.GetComponentsInChildren<CanvasRenderer>(true))
                    if (renderer.gameObject.activeInHierarchy && renderer.GetAlpha() > .001f) lit++;
            Expect(canvas != null && canvas.enabled && canvas.renderMode == RenderMode.ScreenSpaceOverlay && lit >= 1,
                "Overlay drawn", $"canvas {(canvas != null ? (canvas.enabled ? "on" : "off") : "missing")}, mode {canvas?.renderMode}, sorting {canvas?.sortingOrder}, lit slots {lit}");
        }

        // Edge of the brightest lit band, from its position relative to the screen centre.
        static string LitSide()
        {
            var overlay = GameObject.Find("Direction Hint Overlay");
            if (overlay == null) return "none";
            float best = 0; Vector2 at = default;
            foreach (var renderer in overlay.GetComponentsInChildren<CanvasRenderer>(true))
                if (renderer.GetAlpha() > best) { best = renderer.GetAlpha(); at = ((RectTransform)renderer.transform).anchoredPosition; }
            var all = new System.Text.StringBuilder();
            foreach (var renderer in overlay.GetComponentsInChildren<CanvasRenderer>(true))
                all.Append($"[{renderer.name} a={renderer.GetAlpha():0.000} at {((RectTransform)renderer.transform).anchoredPosition}] ");
            var view = NetworkPlayer.Local?.GetComponentInChildren<Camera>(true);
            Debug.Log($"[DirectionHintChecks] DIAG overlay: lastOctant {LastOctant}, view yaw {(view != null ? view.transform.eulerAngles.y : -1)}, screen {Screen.width}x{Screen.height}, {all}");
            if (best <= 0) return "none";
            if (Mathf.Abs(at.y) * Screen.width >= Mathf.Abs(at.x) * Screen.height) return at.y > 0 ? "top" : "bottom";
            return at.x > 0 ? "right" : "left";
        }

        // ---- helpers ----
        static object Invoke(NetworkRound round, string method, params object[] args) =>
            typeof(NetworkRound).GetMethod(method, Flags).Invoke(round, args);

        // A real SoundTarget from the server to the host's own client connection.
        static void Send(NetworkRound round, NetworkPlayer player, string clip, Vector3 point, int loopKey, bool muffled, float radius, float seconds, bool fromOther = true)
        {
            var target = typeof(NetworkRound).GetMethod("SoundTarget", Flags);
            target.Invoke(round, new object[] { player.Owner, clip, point, .5f, 1f, loopKey, muffled, -1, false, fromOther, radius, seconds, -1, -1 });
        }

        // SoundWorld snaps a hidden source to the nearest 45° around the listener (atan2(x, z), 0 = +Z).
        static int ServerOctant(Vector3 source, Vector3 listener)
        {
            float yaw = Mathf.Atan2(source.x - listener.x, source.z - listener.z) * Mathf.Rad2Deg;
            return ((int)Mathf.Round(yaw / 45f) % 8 + 8) % 8;
        }

        // An active valve (only those may turn) with a spot where the host hears it through a wall or the water surface.
        static bool FindHiddenValve(NetworkRound round, NetworkPlayer player, out ValveId id, out Vector3 source, out Vector3 spot)
        {
            var board = typeof(NetworkRound).GetField("board", Flags)?.GetValue(round) as ValveBoard;
            foreach (var candidate in new[] { ValveId.C, ValveId.D, ValveId.A, ValveId.B, ValveId.E })
            {
                if (board == null || !board.Valves.ContainsKey(candidate) || !ValveView.TryGet(candidate, out var view)) continue;
                if (!FindHiddenSpot(round, player, view.transform.position, (float)Invoke(round, "ValveSoundRadius", candidate), out spot)) continue;
                id = candidate; source = view.transform.position;
                return true;
            }
            id = default; source = default; spot = default;
            return false;
        }

        // A floor spot inside the hearing range where the host hears `source` through a wall (muffled).
        static bool FindHiddenSpot(NetworkRound round, NetworkPlayer player, Vector3 source, float radius, out Vector3 spot)
        {
            var movement = player.GetComponent<PlayerMovement>();
            float reach = radius * .5f * (player.Role == PlayerRole.Seeker ? 1.2f : 1f) - .2f;
            for (float distance = 1.5f; distance < reach; distance += .75f)
                for (int i = 0; i < 16; i++)
                {
                    float angle = i * Mathf.PI / 8f;
                    var candidate = source + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                    if (!Physics.Raycast(candidate + Vector3.up * 2.5f, Vector3.down, out var floor, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (floor.normal.y < .7f || Physics.CheckSphere(floor.point + Vector3.up * 1f, .35f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    var at = floor.point + Vector3.up * 1.05f;
                    movement.Teleport(at);
                    Physics.SyncTransforms();
                    var args = new object[] { source, player, radius, null, null, null, null };
                    bool heard = (bool)typeof(NetworkRound).GetMethod("TryHearPoolVoice", Flags).Invoke(round, args);
                    if (heard && (bool)args[5])
                    {
                        spot = at;
                        return true;
                    }
                }
            spot = default;
            return false;
        }

        static IList Rings(WalkingFootRings rings) => (IList)typeof(WalkingFootRings).GetField("rings", Flags).GetValue(rings);

        static int RingsNear(WalkingFootRings rings, Vector3 point, float within)
        {
            int count = 0;
            foreach (var ring in Rings(rings))
            {
                var type = ring.GetType();
                if (!(bool)type.GetField("Sound").GetValue(ring)) continue;
                var go = (GameObject)type.GetField("Object").GetValue(ring);
                var p = go.transform.position;
                if (new Vector2(p.x - point.x, p.z - point.z).magnitude < within) count++;
            }
            return count;
        }

        static float RingRadiusAt(WalkingFootRings rings, Vector3 point)
        {
            float found = 0;
            foreach (var ring in Rings(rings))
            {
                var type = ring.GetType();
                if (!(bool)type.GetField("Sound").GetValue(ring)) continue;
                var go = (GameObject)type.GetField("Object").GetValue(ring);
                var p = go.transform.position;
                if (new Vector2(p.x - point.x, p.z - point.z).magnitude < .5f) found = (float)type.GetField("Radius").GetValue(ring);
            }
            return found;
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
