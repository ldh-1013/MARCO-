using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using Marco.Network.Escape;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Marco.Network.Editor
{
    // MARCO > Escapee > Verify escapee echo (기획서 3.3-3 · 연출.md 5).
    // Hosts a real local session in the pool map, escapes through the server path (gate opened by the
    // master-key cheat) and renders the local player's camera offscreen into Logs/EscapeeEcho:
    //   fade_mid / fade_end / fade_reveal   the escape fade pinned at 0.5 / 1.0 / 1.25 s
    //   escapee_view_dark / _lit             the escapee looking down at its cyan body, banner "탈출 성공"
    //   side_by_side_dark / _voice / _lit    eliminated Echo (purple), escapee (cyan) and a living Runner
    //   after_rematch                        the same player after a rematch: living body, no banner
    // dark = silent microphone (in-game darkness), voice = microphone light pinned at 대화 9 m,
    // lit = F8 development vision. *_3d.png is the camera alone. result.txt holds automatic checks
    // (body, collision, colour, fade, visibility in darkness, reset).
    // Made for batch mode (graphics on, no window): there is no Game view or IMGUI there, so the HUD
    // panels and the cyan overlay are composited onto the camera image from the game's own values —
    // NetworkTestHud's banner text/rects and EscapeeTransition.OverlayAlpha (HudComposite).
    //   Unity.exe -batchmode -projectPath <repo> -executeMethod Marco.Network.Editor.EscapeeEchoRenderCheck.RunAndExit -logFile <log>
    // Plays the scene without saving anything; from the menu, the scene that was open comes back afterwards.
    [InitializeOnLoad]
    public static class EscapeeEchoRenderCheck
    {
        private const string Pending = "Marco.EscapeeEchoRenderCheck";
        private const string ReturnScene = "Marco.EscapeeEchoRenderCheck.ReturnScene";
        private const string ExitAfter = "Marco.EscapeeEchoRenderCheck.ExitAfter";
        private const string ReportKey = "Marco.EscapeeEchoRenderCheck.Report";
        private const string ExitCodeKey = "Marco.EscapeeEchoRenderCheck.ExitCode";
        private const string MapScene = "Assets/Scenes/Network/NetworkPool_v1_2.unity";
        private const string OutputFolder = "Logs/EscapeeEcho";
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static int phase;
        private static double until, deadline;
        private static readonly StringBuilder report = new();
        private static bool ok = true;
        private static readonly List<GameObject> dummies = new();
        private static readonly List<PlayableGraph> graphs = new();
        private static string pendingShot;
        private static int shotWaitFrames;

        static EscapeeEchoRenderCheck()
        {
            // A domain reload while leaving Play mode would drop the Return handler: finish the exit here.
            int exitCode = SessionState.GetInt(ExitCodeKey, -1);
            if (exitCode >= 0 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.EraseInt(ExitCodeKey);
                EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
                return;
            }
            if (!SessionState.GetBool(Pending, false)) return;
            report.Clear().Append(SessionState.GetString(ReportKey, ""));
            Subscribe();
        }

        [MenuItem("MARCO/Escapee/Verify escapee echo")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode before checking the escapee echo."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SessionState.SetString(ReturnScene, SceneManager.GetActiveScene().path);
            Start(exitAfter: false);
        }

        // Batch mode entry (see the header). Exits with 0 on PASS, 1 on FAIL.
        public static void RunAndExit()
        {
            SessionState.SetString(ReturnScene, "");
            Start(exitAfter: true);
        }

        private static void Start(bool exitAfter)
        {
            Directory.CreateDirectory(OutputFolder);
            foreach (var old in Directory.GetFiles(OutputFolder, "*.png")) File.Delete(old);
            report.Clear().AppendLine($"Escapee echo check {DateTime.Now:yyyy-MM-dd HH:mm}");
            SessionState.SetString(ReportKey, report.ToString());
            SessionState.SetBool(ExitAfter, exitAfter);
            SessionState.SetBool(Pending, true);
            EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Single);
            Subscribe();
            EditorApplication.EnterPlaymode();
        }

        private static void Subscribe()
        {
            phase = 0;
            ok = true;
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Log(string line) => report.AppendLine(line);

        private static void Check(bool condition, string what)
        {
            Log((condition ? "  ok    " : "  FAIL  ") + what);
            ok &= condition;
        }

        private static T Get<T>(object instance, string name) =>
            (T)instance.GetType().GetField(name, Flags).GetValue(instance);

        private static void Call(object instance, string name, params object[] args) =>
            instance.GetType().GetMethod(name, Flags).Invoke(instance, args);

        // Waits a few frames so the look changes settle, then renders the player's camera.
        private static void Shot(string name, int nextPhase, int settleFrames = 4)
        {
            pendingShot = name;
            shotWaitFrames = settleFrames;
            phase = nextPhase;
        }

        private static bool PumpShot()
        {
            if (pendingShot == null) return false;
            if (shotWaitFrames-- > 0) return true;
            var player = NetworkPlayer.Local;
            var image = HudComposite.RenderCamera(player.GetComponentInChildren<Camera>());
            File.WriteAllBytes($"{OutputFolder}/{pendingShot}_3d.png", image.EncodeToPNG());
            float overlay = player.TryGetComponent(out EscapeeTransition fade) ? fade.OverlayAlpha : 0f;
            string banner = NetworkTestHud.EchoBodyBannerTitle(NetworkRound.Instance, player);
            HudComposite.Draw(image, NetworkRound.Instance, banner, overlay);
            File.WriteAllBytes($"{OutputFolder}/{pendingShot}.png", image.EncodeToPNG());
            Log($"[{pendingShot}] rendered {image.width}x{image.height}, overlay {overlay:F2}, banner {banner ?? "none"}");
            Object.DestroyImmediate(image);
            pendingShot = null;
            return true;
        }

        private static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Timed out at phase " + phase);
                if (!EditorApplication.isPlaying) return;
                if (PumpShot() || now < until) return;

                var round = NetworkRound.Instance;
                var player = NetworkPlayer.Local;
                switch (phase)
                {
                    case 0:
                    {
                        var lan = Object.FindAnyObjectByType<LanConnector>();
                        if (lan == null) return;
                        // The title sequence only plays on a fresh launch; in play it is long gone by now.
                        foreach (var intro in Object.FindObjectsByType<Lobby.LobbyIntro>(FindObjectsSortMode.None))
                            intro.enabled = false;
                        lan.Host();
                        phase = 1;
                        return;
                    }
                    case 1:
                        if (round == null || player == null || !round.IsClientInitialized) return;
                        round.StartRound();
                        until = now + 1;
                        phase = 2;
                        return;
                    case 2:
                        PrepareRunnerAtExit(round, player);
                        until = now + .6;
                        phase = 3;
                        return;
                    case 3:
                        Log("[before escape]");
                        Check(player.Status == PlayerStatus.Alive && player.Role == PlayerRole.Runner, "alive Runner before escape");
                        Check(round.GateOpen, "gate open (master key)");
                        Check(round.TryEscape(player.PlayerId, ExitPosition()), "server accepts the escape at the exit");
                        phase = 4;
                        return;
                    case 4:
                    {
                        // Wait for the status SyncVar to reach this client and start the fade.
                        var fade = player.GetComponent<EscapeeTransition>();
                        if (player.Status != PlayerStatus.Escaped || fade == null || !fade.IsRunning) return;
                        fade.FreezeAt(.5f);
                        Log("[fade 0.5 s]");
                        Check(player.IsEscapeFading, "movement frozen while fading");
                        Check(player.GetComponent<CharacterController>().enabled, "living body kept until full cover");
                        Shot("fade_mid", 5);
                        return;
                    }
                    case 5:
                        player.GetComponent<EscapeeTransition>().FreezeAt(1f);
                        Shot("fade_end", 6);
                        return;
                    case 6:
                    {
                        Log("[fade 1.0 s]");
                        Check(!player.IsEscapeFading, "body switched at full cover");
                        Check(!player.GetComponent<CharacterController>().enabled, "no collision as Echo body");
                        var body = player.GetComponent<CharacterBody>();
                        Check(body.Ghost == GhostLook.Escapee, "own body uses the cyan escapee ghost");
                        player.GetComponent<EscapeeTransition>().FreezeAt(1.25f);
                        Shot("fade_reveal", 7);
                        return;
                    }
                    case 7:
                    {
                        // Past the end: Update ends the fade during the settle frames (unfrozen in case 8).
                        player.GetComponent<EscapeeTransition>().FreezeAt(10f);
                        // Open floor space, looking down at its own body as a player would.
                        MoveToOpenSpace(player);
                        SilenceMicrophone(player, 0f, 0f);
                        LookDown(player, 78f);
                        Shot("escapee_view_dark", 8, 10);
                        return;
                    }
                    case 8:
                        Log("[escapee view]");
                        LogCamera(player);
                        Check(!player.GetComponent<EscapeeTransition>().IsRunning, "fade finished");
                        player.GetComponent<EscapeeTransition>().FreezeAt(null);
                        Check(player.IsEscapee && player.HasEchoBody && !player.IsEcho, "escapee: Echo body, not an Echo");
                        SetFullVision(player, true);
                        Shot("escapee_view_lit", 9, 10);
                        return;
                    case 9:
                        SetFullVision(player, false);
                        SpawnLineup(player);
                        LookDown(player, 8f);
                        Shot("side_by_side_dark", 10, 12);
                        return;
                    case 10:
                        // As if talking at 대화 level: the microphone light around the viewer (radius 9 m).
                        SilenceMicrophone(player, 9f, 1.2f);
                        Shot("side_by_side_voice", 11, 20);
                        return;
                    case 11:
                        SilenceMicrophone(player, 0f, 0f);
                        SetFullVision(player, true);
                        Shot("side_by_side_lit", 12, 10);
                        return;
                    case 12:
                        SetFullVision(player, false);
                        ClearLineup();
                        Log("[rematch]");
                        round.RestartRound();
                        until = now + 1.2;
                        phase = 13;
                        return;
                    case 13:
                        Check(player.Status == PlayerStatus.Alive, "status back to Alive");
                        Check(!player.HasEchoBody, "no Echo body after rematch");
                        Check(player.GetComponent<CharacterController>().enabled, "collision back after rematch");
                        Check(player.GetComponent<CharacterBody>().Ghost == GhostLook.None, "living colours back after rematch");
                        Check(!player.GetComponent<EscapeeTransition>().IsRunning, "no fade left over");
                        // One host is drawn Seeker again; show the Runner body the escapee had.
                        player.SetRole(PlayerRole.Runner);
                        SetFullVision(player, true);
                        LookDown(player, 78f);
                        Shot("after_rematch", 14, 14);
                        return;
                    case 14:
                        AnalyseShots();
                        Finish();
                        return;
                }
            }
            catch (Exception error)
            {
                ok = false;
                Log("EXCEPTION " + error);
                Debug.LogException(error);
                Finish();
            }
            finally
            {
                SessionState.SetString(ReportKey, report.ToString());
            }
        }

        private static Vector3 ExitPosition()
        {
            foreach (var exit in ExitView.All) return exit.transform.position;
            throw new Exception("No ExitView in the map scene.");
        }

        // One player hosts, so the draw makes them the Seeker in a ValvesOnly round: turn them into a
        // Runner, skip the role reveal, open the gate and stand them in front of an exit. The HUD is
        // shown as in a full round (timer, no restart button) so the banner sits where it will in play.
        private static void PrepareRunnerAtExit(NetworkRound round, NetworkPlayer player)
        {
            player.SetRole(PlayerRole.Runner);
            Get<RoundStartSchedule>(round, "startSchedule").Tick(40f);
            Call(round, "CheatOpenActiveValves");
            var mode = round.GetType().GetField("mode", Flags).GetValue(round);
            mode.GetType().GetProperty("Value").SetValue(mode, RoundMode.Full);
            var remaining = round.GetType().GetField("remainingSeconds", Flags).GetValue(round);
            remaining.GetType().GetProperty("Value").SetValue(remaining, 497f);

            Vector3 exit = ExitPosition();
            Vector3 toward = Vector3.ProjectOnPlane(exit - new Vector3(30, exit.y, 30), Vector3.up).normalized;
            if (toward.sqrMagnitude < .01f) toward = Vector3.forward;
            Vector3 stand = exit - toward * 1.6f;
            stand.y = exit.y + 1.05f;
            var movement = player.GetComponent<PlayerMovement>();
            movement.TeleportToSpawn(stand, Quaternion.LookRotation(toward));
            Physics.SyncTransforms();
            Log($"[setup] exit {exit}, standing at {stand}");
        }

        // First floor spot (y ≈ 0) with 7 m of clear sight ahead and ±1.5 m beside it, found by raycasts
        // over a grid of the pool map, so the lineup is never behind a wall.
        private static void MoveToOpenSpace(NetworkPlayer player)
        {
            var directions = new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            for (float x = 4; x <= 54; x += 3)
                for (float z = 4; z <= 62; z += 3)
                {
                    if (!Physics.Raycast(new Vector3(x, 3f, z), Vector3.down, out var floor, 3.5f, ~0, QueryTriggerInteraction.Ignore) ||
                        floor.normal.y < .9f || floor.point.y > .3f || floor.collider.GetComponentInParent<NetworkPlayer>() != null) continue;
                    Vector3 eye = floor.point + Vector3.up * 1.2f;
                    foreach (var direction in directions)
                    {
                        var side = Vector3.Cross(Vector3.up, direction) * 1.5f;
                        if (Physics.Raycast(eye, direction, 7f) || Physics.Raycast(eye + side, direction, 5f) ||
                            Physics.Raycast(eye - side, direction, 5f) || Physics.Raycast(eye, Vector3.up, 2.5f)) continue;
                        var movement = player.GetComponent<PlayerMovement>();
                        // Pivot 1.0 m above the floor, as a standing body (CharacterAsset modelOffset −1).
                        movement.TeleportToSpawn(floor.point + Vector3.up, Quaternion.LookRotation(direction));
                        Physics.SyncTransforms();
                        Log($"[open space] floor {floor.point} facing {direction}");
                        return;
                    }
                }
            Log("  WARN  no open space found; staying at the exit");
        }

        // Pins the microphone light: radius 0 = silence (in-game darkness), otherwise a steady voice light.
        private static void SilenceMicrophone(NetworkPlayer player, float radius, float intensity)
        {
            var vision = player.GetComponentInChildren<MicrophoneVisionController>(true);
            if (vision == null) return;
            var type = vision.GetType();
            type.GetField("microphoneClip", Flags).SetValue(vision, null);
            foreach (var name in new[] { "targetWorldRadius", "worldRadius" }) type.GetField(name, Flags).SetValue(vision, radius);
            foreach (var name in new[] { "targetWorldIntensity", "worldIntensity" }) type.GetField(name, Flags).SetValue(vision, intensity);
            type.GetField("status", Flags).SetValue(vision, radius > 0 ? "대화 · 주변이 드러납니다 (검증용 고정)" : "침묵 (검증용 고정)");
            // Dots dropped by room noise before the pin would otherwise linger for 5 s.
            var dots = vision.GetComponent<MicrophoneFootDots>();
            if (dots != null)
                foreach (var dot in (System.Collections.IList)dots.GetType().GetField("dots", Flags).GetValue(dots))
                    ((GameObject)dot.GetType().GetField("gameObject", Flags).GetValue(dot)).SetActive(false);
        }

        private static void LogCamera(NetworkPlayer player)
        {
            var camera = player.GetComponentInChildren<Camera>();
            if (camera == null) return;
            bool hasFloor = Physics.Raycast(camera.transform.position, Vector3.down, out var hit, 20f, ~0, QueryTriggerInteraction.Ignore);
            Log($"  camera {camera.transform.position}, pivot {player.transform.position}, floor {(hasFloor ? hit.distance.ToString("F2") + " m below" : "none")}");
        }

        private static void LookDown(NetworkPlayer player, float pitch)
        {
            var movement = player.GetComponent<PlayerMovement>();
            movement.GetType().GetField("pitch", Flags).SetValue(movement, pitch);
        }

        private static void SetFullVision(NetworkPlayer player, bool enabled)
        {
            var vision = player.GetComponentInChildren<MicrophoneVisionController>(true);
            if (vision != null) vision.SetDevelopmentFullVision(enabled);
        }

        // Three bodies 3 m ahead built with the same CharacterBody path the players use:
        // eliminated Echo (purple ghost), escapee (cyan ghost) and a living Runner for scale.
        private static void SpawnLineup(NetworkPlayer viewer)
        {
            var source = viewer.GetComponent<CharacterBody>();
            var asset = Get<CharacterAsset>(source, "runnerAsset");
            var forward = Vector3.ProjectOnPlane(viewer.transform.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward);
            Vector3 center = viewer.transform.position + forward * 3.2f;
            var looks = new[] { GhostLook.Echo, GhostLook.Escapee, GhostLook.None };
            var names = new[] { "탈락 메아리", "탈출자", "도망자" };
            for (int i = 0; i < looks.Length; i++)
            {
                var go = new GameObject("Lineup " + names[i]);
                go.SetActive(false);
                go.transform.SetPositionAndRotation(center + right * ((i - 1) * 1.15f), Quaternion.LookRotation(-forward));
                var body = go.AddComponent<CharacterBody>();
                body.GetType().GetField("runnerAsset", Flags).SetValue(body, asset);
                foreach (var field in new[] { "ghostTint", "escapeeGhostTint" })
                    body.GetType().GetField(field, Flags).SetValue(body, source.GetType().GetField(field, Flags).GetValue(source));
                go.SetActive(true);
                body.Show(PlayerRole.Runner, true, looks[i]);
                if (body.BodyAnimator != null && asset.idleClip != null)
                {
                    var graph = PlayableGraph.Create("Lineup idle");
                    var output = AnimationPlayableOutput.Create(graph, "Animation", body.BodyAnimator);
                    output.SetSourcePlayable(AnimationClipPlayable.Create(graph, asset.idleClip));
                    graph.Play();
                    graphs.Add(graph);
                }
                dummies.Add(go);
            }
        }

        private static void ClearLineup()
        {
            foreach (var graph in graphs) if (graph.IsValid()) graph.Destroy();
            graphs.Clear();
            foreach (var go in dummies) if (go != null) Object.Destroy(go);
            dummies.Clear();
        }

        // Colour checks on the saved PNGs: the escapee overlay, and both ghosts readable in the dark.
        private static void AnalyseShots()
        {
            Log("[images]");
            var mid = Load("fade_mid");
            var end = Load("fade_end");
            if (mid != null && end != null)
            {
                Color m = Average(mid), e = Average(end);
                Check(Mathf.Abs(e.r - EscapeFade.Cyan.r) < .04f && Mathf.Abs(e.g - EscapeFade.Cyan.g) < .04f && Mathf.Abs(e.b - EscapeFade.Cyan.b) < .04f,
                    $"fade_end is #7FE7E0 over the whole screen (avg {ColorUtility.ToHtmlStringRGB(e)})");
                Check(Luma(m) < Luma(e) - .1f, $"fade_mid is part way (luma {Luma(m):F2} < {Luma(e):F2})");
            }
            // Lineup checks use the camera image alone, middle band only.
            var dark = Load("side_by_side_dark_3d");
            if (dark != null)
            {
                int purple = CountPixels(dark, IsPurple), cyan = CountPixels(dark, IsCyan);
                Log($"  dark lineup pixels: purple {purple}, cyan {cyan}");
                Check(purple > 1500, "eliminated Echo readable in darkness (purple pixels)");
                Check(cyan > 1500, "escapee readable in darkness (cyan pixels)");
            }
            var after = Load("after_rematch_3d");
            if (after != null)
            {
                int cyan = CountPixels(after, IsCyan), purple = CountPixels(after, IsPurple);
                Log($"  after rematch ghost pixels: cyan {cyan}, purple {purple}");
            }
            foreach (var image in new[] { mid, end, dark, after }) if (image != null) Object.DestroyImmediate(image);
        }

        private static bool IsPurple(Color c) => c.b > .16f && c.b > c.g * 1.3f && c.r > c.g * 1.05f;
        private static bool IsCyan(Color c) => c.g > .16f && c.b > .14f && c.g > c.r * 1.3f && c.b > c.r * 1.2f && Mathf.Abs(c.g - c.b) < .25f;

        private static Texture2D Load(string name)
        {
            string path = $"{OutputFolder}/{name}.png";
            if (!File.Exists(path)) { Check(false, $"{name}.png written"); return null; }
            var image = new Texture2D(2, 2);
            image.LoadImage(File.ReadAllBytes(path));
            return image;
        }

        private static Color Average(Texture2D image)
        {
            var pixels = image.GetPixels32();
            double r = 0, g = 0, b = 0;
            for (int i = 0; i < pixels.Length; i += 7) { r += pixels[i].r; g += pixels[i].g; b += pixels[i].b; }
            int n = (pixels.Length + 6) / 7;
            return new Color((float)(r / n / 255), (float)(g / n / 255), (float)(b / n / 255));
        }

        private static float Luma(Color c) => .299f * c.r + .587f * c.g + .114f * c.b;

        private static int CountPixels(Texture2D image, Func<Color, bool> match)
        {
            // Rows 30 %–88 % from the top (texture rows run bottom-up): clear of the HUD panels.
            var pixels = image.GetPixels32();
            int count = 0, width = image.width;
            for (int y = (int)(image.height * .12f); y < (int)(image.height * .70f); y++)
                for (int x = 0; x < width; x++)
                    if (match(pixels[y * width + x])) count++;
            return count;
        }

        private static void Finish()
        {
            ClearLineup();
            EditorApplication.update -= Tick;
            report.Insert(0, ok ? "PASS\n" : "FAIL\n");
            File.WriteAllText($"{OutputFolder}/result.txt", report.ToString());
            Debug.Log("[MARCO][Escapee] " + report);
            SessionState.SetBool(Pending, false);
            bool exitAfter = SessionState.GetBool(ExitAfter, false);
            int code = ok ? 0 : 1;
            string back = SessionState.GetString(ReturnScene, "");
            if (exitAfter) SessionState.SetInt(ExitCodeKey, code);
            EditorApplication.playModeStateChanged += Return;
            EditorApplication.ExitPlaymode();

            void Return(PlayModeStateChange change)
            {
                if (change != PlayModeStateChange.EnteredEditMode) return;
                EditorApplication.playModeStateChanged -= Return;
                if (exitAfter) { SessionState.EraseInt(ExitCodeKey); EditorApplication.Exit(code); }
                else if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            }
        }
    }
}
