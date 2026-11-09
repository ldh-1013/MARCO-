using System;
using System.IO;
using System.Reflection;
using FishNet.Managing;
using FishNet.Managing.Object;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Real host/owner RPC checks on a disposable generated scene. No -quit: exits after Play checks.
    [InitializeOnLoad]
    public static class CombatHudChecks
    {
        private const string Pending = "Marco.CombatHudChecks";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int phase, checks;
        private static double until, deadline;
        private static NetworkPlayer dummy;
        private static GameObject wall;
        private static string output;
        static CombatHudChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
        private static void Subscribe()
        {
            deadline = EditorApplication.timeSinceStartup + 120;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
        public static void Run()
        {
            if (!Application.isBatchMode) throw new Exception("Use a separate batch project.");
            if (AssetDatabase.FindAssets("t:DefaultPrefabObjects").Length == 0)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<DefaultPrefabObjects>(), "Assets/DefaultPrefabObjects.asset");
            NetworkTestTools.CreateTestScene();
            var manager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>();
            manager.SpawnablePrefabs = AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>("Assets/DefaultPrefabObjects.asset");
            EditorUtility.SetDirty(manager);
            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            var gameView = EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView"));
            gameView.position = new Rect(0, 0, 1280, 720);
            gameView.Repaint();
            SessionState.SetBool(Pending, true);
            Subscribe();
            EditorApplication.EnterPlaymode();
        }
        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
            checks++;
            Debug.Log("[CombatHudChecks] PASS: " + message);
        }
        private static object Field(object instance, string name) => instance.GetType().GetField(name, Private).GetValue(instance);
        private static void Swing(PlayerInteractor interactor, NetworkRound round) =>
            typeof(PlayerInteractor).GetMethod("Swing", Private).Invoke(interactor, new object[] { round });
        private static void Capture(string name)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var type = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            RenderTexture target = null;
            var window = EditorWindow.GetWindow(type);
            window.Repaint();
            for (var current = type; current != null && target == null; current = current.BaseType)
            {
                foreach (var method in current.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                    if (method.ReturnType == typeof(RenderTexture) && method.GetParameters().Length == 0)
                        target = method.Invoke(null, null) as RenderTexture;
                foreach (var field in current.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (target == null && field.FieldType == typeof(RenderTexture)) target = field.GetValue(window) as RenderTexture;
            }
            if (target == null) throw new Exception("GameView has no rendered frame");
            var old = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            RenderTexture.active = old;
            File.WriteAllBytes(Path.Combine(output, name + ".png"), image.EncodeToPNG());
            if (name == "Seeker-ready" || name == "Seeker-cooldown")
            {
                Color center = image.GetPixel(image.width / 2, image.height / 2);
                Check(center.r > .25f && center.g > .25f && center.b > .25f, "Actual GameView renders seeker centre dot: " + name);
            }
            if (name == "Seeker-cooldown")
            {
                int orangePixels = 0;
                int radius = Mathf.CeilToInt(40 * Mathf.Min(image.width / 1920f, image.height / 1080f));
                for (int y = image.height / 2 - radius; y <= image.height / 2 + radius; y++)
                    for (int x = image.width / 2 - radius; x <= image.width / 2 + radius; x++)
                    {
                        Color pixel = image.GetPixel(x, y);
                        if (pixel.r > .6f && pixel.g > .25f && pixel.b < .4f) orangePixels++;
                    }
                Check(orangePixels > 10, "Actual GameView renders the orange cooldown arc");
            }
            Color edge = image.GetPixel(1, image.height / 2);
            if (name == "Runner-1.0") Check(edge.r > edge.g * 1.4f, "Actual GameView renders red wounded edges");
            if (name == "Runner-1.5") Check(edge.r > edge.g && edge.g > .08f, "Actual GameView renders orange recovery edges");
            if (name == "Runner-healed") Check(edge.r < edge.g, "Actual GameView clears injury edges after treatment");
            UnityEngine.Object.DestroyImmediate(image);
        }
        private static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Timeout in phase " + phase);
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
                    if (round == null || player == null || !round.IsClientInitialized) return;
                    round.StartRound(); until = now + .5; phase = 2; return;
                }
                if (now < until) return;
                var interactor = player.GetComponent<PlayerInteractor>();
                if (phase == 2)
                {
                    ((RoundStartSchedule)Field(round, "startSchedule")).Tick(40);
                    player.GetComponent<PlayerMovement>().enabled = false;
                    player.transform.position = new Vector3(0, 1, 0);
                    player.transform.rotation = Quaternion.identity;
                    player.GetComponentInChildren<MicrophoneVisionController>()?.SetDevelopmentFullVision(true);
                    var camera = player.GetComponentInChildren<Camera>();
                    camera.cullingMask = 0; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .1f, .12f);
                    output = Path.GetFullPath("CombatHudCaptures"); Directory.CreateDirectory(output);
                    Check(player.Role == PlayerRole.Seeker && round.Mode == RoundMode.ValvesOnly, "One-player screenshot route uses Seeker in ValvesOnly");
                    Check(UnityEngine.Object.FindAnyObjectByType<NetworkTestHud>() != null, "Actual network HUD exists");
                    until = now + .5; phase = 3; return;
                }
                if (phase == 3)
                {
                    Capture("Seeker-ready");
                    Swing(interactor, round); until = now + .2; phase = 4; return;
                }
                if (phase == 4)
                {
                    Check(!interactor.SwingPending && interactor.SwingCooldownRemaining > 0 && interactor.SwingCooldownFraction > 0,
                        "Real owner RPC resolves a miss into the cooldown ring");
                    Capture("Seeker-cooldown");
                    float end = (float)Field(interactor, "nextSwingTime");
                    Swing(interactor, round);
                    Check(Mathf.Approximately((float)Field(interactor, "nextSwingTime"), end), "Cooldown ignores repeated clicks");
                    until = now + 1.6; phase = 5; return;
                }
                if (phase == 5)
                {
                    Check(interactor.SwingCooldownRemaining == 0 && interactor.SwingCooldownFraction == 0, "Miss ring expires");
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkTestTools.PlayerPrefabPath);
                    var body = UnityEngine.Object.Instantiate(prefab, new Vector3(0, 1, 1.5f), Quaternion.identity);
                    dummy = body.GetComponent<NetworkPlayer>();
                    UnityEngine.Object.FindAnyObjectByType<NetworkManager>().ServerManager.Spawn(body);
                    dummy.SetRole(PlayerRole.Runner); dummy.SetStatus(PlayerStatus.Alive);
                    dummy.GetComponent<PlayerMovement>().enabled = false;
                    until = now + .4; phase = 6; return;
                }
                if (phase == 6)
                {
                    Check(round.TryFindTagTarget(player.transform, round.TagRadius, out var found) && found == dummy, "Network attack reaches a body whose pivot is beyond 1.2m");
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = new Vector3(0, 1, .6f); wall.transform.localScale = new Vector3(2, 3, .1f);
                    Check(!round.TryFindTagTarget(player.transform, round.TagRadius, out _), "Network attack cannot pass through a wall");
                    wall.GetComponent<Collider>().isTrigger = true;
                    Check(round.TryFindTagTarget(player.transform, round.TagRadius, out _), "Trigger does not block the network attack");
                    UnityEngine.Object.Destroy(wall);
                    Swing(interactor, round); until = now + .2; phase = 7; return;
                }
                if (phase == 7)
                {
                    Check(interactor.SwingCooldownRemaining > 2 && interactor.SwingCooldownFraction > .8f, "Real successful attack uses the longer hit cooldown");
                    player.SetRole(PlayerRole.Runner); player.SendHealth(2); until = now + .3; phase = 8; return;
                }
                if (phase == 8)
                {
                    Check(player.HealthHalves == 2 && player.LastInjuryTime >= 0, "Owner-private health RPC drives the red injury effect");
                    Capture("Runner-1.0");
                    player.SendHealth(3); until = now + .3; phase = 9; return;
                }
                if (phase == 9)
                {
                    Check(player.HealthHalves == 3, "Bandaged 1.5 health arrives through the real owner RPC");
                    Capture("Runner-1.5");
                    player.SendHealth(4); until = now + .3; phase = 10; return;
                }
                if (phase == 10)
                {
                    Capture("Runner-healed");
                    Debug.Log("[CombatHudChecks] PASS: " + checks + " checks; captures=" + output);
                    Finish(0);
                }
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }
        private static void Finish(int code)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(code);
        }
    }
}
