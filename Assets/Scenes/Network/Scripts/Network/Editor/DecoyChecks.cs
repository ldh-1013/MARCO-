using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Marco.Network.Editor
{
    // Exercises the real FishNet host, private inventory RPC and server physics on an isolated floor.
    [InitializeOnLoad]
    public static class DecoyChecks
    {
        private const string Pending = "Marco.DecoyChecks";
        private static int phase, maxStep;
        private static double deadline, until;
        private static bool heard, sawRing, checkedLanding;
        private static Vector3 expectedLanding;
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly Vector3 TestPosition = new(2000, .05f, 2000);
        private static GameObject wall;

        static DecoyChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
        private static void Subscribe()
        {
            deadline = EditorApplication.timeSinceStartup + 100;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new Exception("Run in a separate batch editor.");
            EditorSceneManager.OpenScene("Assets/Scenes/Network/NetworkPool_v1_2.unity", OpenSceneMode.Single);
            SessionState.SetBool(Pending, true); Subscribe(); EditorApplication.EnterPlaymode();
        }

        private static object Field(object instance, string name) => instance.GetType().GetField(name, Flags | BindingFlags.Public).GetValue(instance);
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static int Count(NetworkRound round, string name) => ((IList)Field(round, name)).Count;

        private static void CheckActualPickupViews(NetworkRound round, NetworkPlayer player)
        {
            var views = (System.Collections.Generic.List<GameObject>)Field(round, "decoyPickupViews");
            Check(views.Count == 2 && views.TrueForAll(v => v.activeInHierarchy), "Both shelf ducks must actually spawn and be active");
            var camera = player.GetComponentInChildren<Camera>();
            var movement = player.GetComponent<PlayerMovement>();
            Vector3 oldPosition = player.transform.position, cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float oldMode = Shader.GetGlobalFloat("_MarcoMinusEnabled"), oldIntensity = Shader.GetGlobalFloat("_MarcoLightIntensity");
            var oldClear = camera.clearFlags; Color oldBackground = camera.backgroundColor;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                Shader.SetGlobalFloat("_MarcoMinusEnabled", 1); Shader.SetGlobalFloat("_MarcoLightIntensity", 0);
                for (int i = 0; i < views.Count; i++)
                {
                    string support = i == 0 ? "h10_" : "h01_";
                    var supports = Physics.RaycastAll(DecoyConfig.PickupPositions[i] + Vector3.up * .04f,
                        Vector3.down, .1f, ~0, QueryTriggerInteraction.Ignore);
                    Check(supports.Any(hit => hit.collider.GetComponentsInParent<Transform>().Any(t => t.name.StartsWith(support))),
                        "Duck must rest on the requested pool hall equipment shelf " + support);
                    Vector3 front = Quaternion.Euler(0, DecoyConfig.PickupYaw[i], 0) * Vector3.back;
                    Vector3 approach = DecoyConfig.PickupPositions[i] + front * .9f;
                    approach.y = 1.05f;
                    movement.Teleport(approach); Physics.SyncTransforms();
                    Check(round.TryFindDecoyPickup(approach, out int pickup) && pickup == i, "Duck not in pickup range from shelf front");
                    Check(!DecoyBallistics.Cast(approach + Vector3.up * .5f,
                        DecoyConfig.PickupPositions[i] - approach, out _), "Shelf duck pickup path is blocked");
                    camera.transform.position = approach + Vector3.up * .6f;
                    var head = views[i].GetComponentsInChildren<Transform>().First(t => t.name == "머리");
                    camera.transform.LookAt(head.position);
                    if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) continue;
                    var visible = Render(camera);
                    views[i].SetActive(false);
                    var hidden = Render(camera);
                    views[i].SetActive(true);
                    var a = visible.GetPixels32(); var b = hidden.GetPixels32(); int pixels = 0;
                    for (int p = 0; p < a.Length; p++)
                        if (a[p].r > 170 && a[p].g > 100 && a[p].b < 100 &&
                            Mathf.Abs(a[p].r - b[p].r) + Mathf.Abs(a[p].g - b[p].g) + Mathf.Abs(a[p].b - b[p].b) > 70) pixels++;
                    Check(pixels > 30, "Spawned duck is not visibly rendered at standing eye height in darkness");
                    string directory = Environment.GetEnvironmentVariable("MARCO_DUCK_PREVIEW_DIR");
                    if (!string.IsNullOrEmpty(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory);
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, $"DuckShelf-{i + 1}.png"), visible.EncodeToPNG());
                        Shader.SetGlobalFloat("_MarcoMinusEnabled", 0);
                        camera.transform.position = approach + Vector3.up * .6f + front * 1.2f;
                        camera.transform.LookAt(DecoyConfig.PickupPositions[i] + Vector3.down * .65f);
                        var context = Render(camera);
                        System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, $"DuckShelf-{i + 1}-Context.png"), context.EncodeToPNG());
                        UnityEngine.Object.Destroy(context);
                        Shader.SetGlobalFloat("_MarcoMinusEnabled", 1);
                    }
                    UnityEngine.Object.Destroy(visible); UnityEngine.Object.Destroy(hidden);
                    Debug.Log($"[DecoyVisibility] shelf={support}, active={views[i].activeInHierarchy}, darkVisiblePixels={pixels}");
                }
            }
            finally
            {
                foreach (var view in views) if (view != null) view.SetActive(true);
                movement.Teleport(oldPosition); camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                camera.clearFlags = oldClear; camera.backgroundColor = oldBackground;
                Shader.SetGlobalFloat("_MarcoMinusEnabled", oldMode); Shader.SetGlobalFloat("_MarcoLightIntensity", oldIntensity);
            }
        }

        private static Texture2D Render(Camera camera)
        {
            var rt = RenderTexture.GetTemporary(960, 600, 24); var old = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                var image = new Texture2D(960, 600, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); image.Apply(); return image;
            }
            finally { RenderTexture.active = old; RenderTexture.ReleaseTemporary(rt); }
        }

        private static void Gesture(PlayerInteractor interactor, NetworkRound round, bool pressed, bool released, bool locked = true) =>
            typeof(PlayerInteractor).GetMethod("HandleDecoyThrow", Flags).Invoke(interactor, new object[] { round, locked, pressed, released });

        private static void Tick()
        {
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (now > deadline) throw new Exception("Decoy timeout phase " + phase);
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
                    round.StartRound();
                    until = now + 1; phase = 10; return;
                }
                if (phase == 10 && now >= until)
                {
                    player.SetRole(PlayerRole.Runner);
                    until = now + .5; phase = 11; return;
                }
                if (phase == 11 && now >= until)
                {
                    Check(player.Role == PlayerRole.Runner, "Runner role must settle before pickup");
                    Check(!round.TryPickupDecoy(player.PlayerId, 0), "Briefing pickup must reject");
                    ((RoundStartSchedule)Field(round, "startSchedule")).Tick(40f);
                    player.GetComponent<PlayerInteractor>().enabled = false;
                    player.GetComponent<PlayerMovement>().enabled = false;
                    CheckActualPickupViews(round, player);
                    var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    floor.name = "Decoy test floor";
                    floor.transform.position = TestPosition + new Vector3(0, -.55f, 10);
                    floor.transform.localScale = new Vector3(40, 1, 40);
                    player.GetComponent<PlayerMovement>().Teleport(DecoyConfig.PickupPositions[0] + Vector3.up * .05f);
                    Physics.SyncTransforms();
                    Check(!round.TryPickupDecoy(player.PlayerId, -1), "Negative index must reject");
                    Check(!round.TryPickupDecoy(player.PlayerId, 999), "Out of range index must reject");
                    Check(!round.TryPickupDecoy(player.PlayerId, 1), "Remote pickup must reject");
                    Check(round.TryPickupDecoy(player.PlayerId, 0), "Nearby pickup must succeed");
                    Check(!round.TryPickupDecoy(player.PlayerId, 0), "Duplicate pickup must reject");
                    until = EditorApplication.timeSinceStartup + .5; phase = 2; return;
                }
                if (phase == 2 && now >= until)
                {
                    Check(player.DecoyCount == 1, "Owner inventory RPC missing; role=" + player.Role +
                        ", status=" + player.Status + ", holders=" + ((System.Collections.Generic.HashSet<int>)Field(round, "decoyHolders")).Count);
                    Check(!round.TryThrowDecoy(player.PlayerId, new Vector3(float.NaN, 0, 1)), "NaN aim accepted");
                    Check(!round.TryThrowDecoy(player.PlayerId, Vector3.forward * 100), "Oversized aim accepted");
                    player.SetRole(PlayerRole.Seeker);
                    Check(!round.TryThrowDecoy(player.PlayerId, Vector3.forward), "Seeker throw accepted");
                    player.SetRole(PlayerRole.Runner);
                    player.GetComponent<PlayerMovement>().Teleport(TestPosition);
                    Physics.SyncTransforms();
                    var points = new Vector3[DecoyConfig.PreviewMaxDots];
                    int pointCount = DecoyBallistics.Predict(DecoyBallistics.Origin(TestPosition, false),
                        new Vector3(0, -.4f, 1).normalized, points, out bool predictedLanding);
                    Check(predictedLanding && pointCount > 3, "Trajectory did not find floor");
                    expectedLanding = points[pointCount - 1] - Vector3.up * (DecoyConfig.ProjectileRadius + .005f);
                    Check(round.TryThrowDecoy(player.PlayerId, new Vector3(0, -.4f, 1).normalized), "Throw rejected");
                    Check(Count(round, "decoyFlights") == 1 && Count(round, "decoyTrails") == 0, "Activated before landing");
                    Check(!round.TryThrowDecoy(player.PlayerId, Vector3.forward), "Consumed item thrown twice");
                    until = now + 4; phase = 3; return;
                }
                if (phase == 3)
                {
                    foreach (var trail in (IList)Field(round, "decoyTrails"))
                    {
                        if ((int)Field(trail, "Step") == 0)
                        {
                            Check(Vector3.Distance(expectedLanding, (Vector3)Field(trail, "Position")) < .01f,
                                "Preview and authoritative landing differ");
                            checkedLanding = true;
                        }
                        maxStep = Mathf.Max(maxStep, (int)Field(trail, "Step"));
                    }
                    foreach (var source in round.GetComponentsInChildren<AudioSource>())
                        if (source.name.StartsWith("SFX Concrete_")) heard = true;
                    var rings = player.GetComponentInChildren<WalkingFootRings>();
                    if (rings != null)
                        foreach (var ring in (IList)Field(rings, "rings"))
                            if (((GameObject)Field(ring, "Object")).activeSelf) sawRing = true;
                    if (now < until) return;
                    Check(heard && sawRing, "Landing must produce audible and visible pulse");
                    Check(checkedLanding, "Predicted landing was not checked");
                    Check(maxStep >= DecoyConfig.MovingSteps - 1, "Fake steps did not move");
                    Check(Count(round, "decoyFlights") == 0 && Count(round, "decoyTrails") == 0, "Device did not expire");
                    Check(player.DecoyCount == 0, "Consumed count not synced");
                    player.GetComponent<PlayerMovement>().Teleport(DecoyConfig.PickupPositions[1] + Vector3.up * .05f);
                    Physics.SyncTransforms();
                    Check(round.TryPickupDecoy(player.PlayerId, 1), "Second pickup rejected");
                    player.GetComponent<PlayerMovement>().Teleport(TestPosition);
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.transform.position = TestPosition + new Vector3(0, 1f, 5);
                    wall.transform.localScale = new Vector3(20, 4, .3f);
                    Physics.SyncTransforms();
                    maxStep = 0;
                    Check(round.TryThrowDecoy(player.PlayerId, new Vector3(0, -.4f, 1).normalized), "Wall trial throw rejected");
                    until = now + 4; phase = 4; return;
                }
                if (phase == 4)
                {
                    foreach (var trail in (IList)Field(round, "decoyTrails"))
                    {
                        Check(((Vector3)Field(trail, "Position")).z < wall.transform.position.z, "Trail crossed wall");
                        maxStep = Mathf.Max(maxStep, (int)Field(trail, "Step"));
                    }
                    if (now < until) return;
                    Check(maxStep < DecoyConfig.MovingSteps - 1, "Wall did not stop trail");
                    // Replenish the two map pickups for the independent gesture trial.
                    typeof(NetworkRound).GetMethod("ResetDecoysServer", Flags).Invoke(round, new object[] { true });
                    player.GetComponent<PlayerMovement>().Teleport(DecoyConfig.PickupPositions[0] + Vector3.up * .05f);
                    Physics.SyncTransforms();
                    Check(round.TryPickupDecoy(player.PlayerId, 0), "Gesture trial pickup rejected");
                    player.GetComponent<PlayerMovement>().Teleport(TestPosition);
                    Physics.SyncTransforms();
                    until = now + .5; phase = 5; return;
                }
                if (phase == 5 && now >= until)
                {
                    Check(player.DecoyCount == 1, "Gesture trial count missing");
                    var interactor = player.GetComponent<PlayerInteractor>();
                    typeof(PlayerInteractor).GetField("selectedItemSlot", Flags).SetValue(interactor, 3);
                    Gesture(interactor, round, true, false);
                    Gesture(interactor, round, false, false);
                    Check(interactor.IsAimingDecoy && Count(round, "decoyFlights") == 0 && player.DecoyCount == 1,
                        "Holding must preview without consuming/throwing");
                    Gesture(interactor, round, false, true, false);
                    Check(!interactor.IsAimingDecoy && Count(round, "decoyFlights") == 0, "Menu cancel threw item");
                    Gesture(interactor, round, false, true);
                    Check(Count(round, "decoyFlights") == 0, "Release without fresh press threw item");
                    Gesture(interactor, round, true, false);
                    typeof(PlayerInteractor).GetMethod("OnApplicationFocus", Flags).Invoke(interactor, new object[] { false });
                    Gesture(interactor, round, false, true);
                    Check(!interactor.IsAimingDecoy && Count(round, "decoyFlights") == 0, "Focus-loss cancellation failed");
                    Gesture(interactor, round, true, false);
                    typeof(PlayerInteractor).GetField("selectedItemSlot", Flags).SetValue(interactor, 2);
                    Gesture(interactor, round, false, false);
                    Check(!interactor.IsAimingDecoy, "Slot switch must cancel");
                    typeof(PlayerInteractor).GetField("selectedItemSlot", Flags).SetValue(interactor, 3);
                    var camera = player.GetComponentInChildren<Camera>();
                    camera.transform.rotation = Quaternion.LookRotation(new Vector3(0, 1, .1f));
                    Gesture(interactor, round, true, false);
                    Gesture(interactor, round, false, true);
                    Check(!interactor.IsAimingDecoy, "Release must end aim");
                    until = now + 2; phase = 6; return;
                }
                if (phase == 6)
                {
                    if (Count(round, "decoyFlights") == 0 || player.DecoyCount != 0)
                    { Check(now < until, "Release did not send real throw RPC"); return; }
                    typeof(NetworkRound).GetMethod("ResetRound", Flags).Invoke(round, null);
                    Check(Count(round, "decoyFlights") == 0 && Count(round, "decoyTrails") == 0, "Reset left devices");
                    Check(!round.TryThrowDecoy(player.PlayerId, Vector3.forward), "Lobby throw accepted");
                    Debug.Log("[DecoyChecks] PASS: two real shelf spawns visible in darkness, accessible pickup; hold/release/cancel, landing, fake steps, wall stop, expiry/reset.");
                    Finish(0);
                }
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(1); }
        }

        private static void Finish(int code)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(code);
        }
    }
}
