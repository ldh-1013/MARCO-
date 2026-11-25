using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Door leak of ring pulses (기획서 §16.4 [10-26]): a ring started in one room must continue through an
    // open doorway into the next room, inside the cone from the source to the two door jambs. For three real
    // first-floor doors this measures how many ray directions pass the door and how much leaked arc can be
    // drawn, and optionally saves a screenshot per door (MARCO_RIPPLE_SHOT = folder outside the project).
    // Measurement only: the microphone is stopped for stable darkness; no scene or prefab is saved.
    [InitializeOnLoad]
    public static class RippleDoorChecks
    {
        const string Pending = "Marco.RippleDoorChecks";
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static int phase, index;
        static double deadline, until;
        static readonly List<Door> doors = new();
        static readonly List<string> results = new();

        sealed class Door { public string Id; public Vector3 Center, Axis, Normal, Origin; public float Width; }

        static RippleDoorChecks() { if (SessionState.GetBool(Pending, false)) Subscribe(); }
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
                if (now > deadline) throw new Exception("Door check timed out at phase " + phase);
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
                    round.StartRound(); until = now + 1.5; phase = 2; return;
                }
                if (phase == 2 && now >= until)
                {
                    player.GetComponent<PlayerMovement>().enabled = false;
                    player.GetComponent<PlayerInteractor>().enabled = false;
                    foreach (var device in Microphone.devices) if (Microphone.IsRecording(device)) Microphone.End(device);
                    FindDoors();
                    if (doors.Count == 0) throw new Exception("No usable first-floor door found");
                    index = 0; until = now + 1.0; phase = 3; return;
                }
                var rings = player != null ? player.GetComponentInChildren<WalkingFootRings>() : null;
                if (rings == null) return;
                if (phase == 3 && now >= until)
                {
                    rings.ClearPulses();
                    var door = doors[index];
                    rings.ShowSound(door.Origin + Vector3.up * 1.5f, 22f, 2.5f, false);
                    until = now + 0.25; phase = 5; return;
                }
                // Wavefront at the walls of the source room: the wall reveal is at its brightest.
                if (phase == 5 && now >= until) { Capture(player, doors[index], "_wall"); until = now + 0.55; phase = 4; return; }
                if (phase == 4 && now >= until)
                {
                    var door = doors[index];
                    results.Add(Measure(rings, door));
                    Capture(player, door, "");
                    index++;
                    if (index < doors.Count) { until = now + 2.0; phase = 3; return; }
                    foreach (var line in results) Debug.Log("[RippleDoorChecks] RESULT " + line);
                    Debug.Log("[RippleDoorChecks] PASS: measurement finished. No scene saved.");
                    Finish(0);
                }
            }
            catch (Exception ex) { Debug.LogException(ex); Finish(1); }
        }

        // First-floor doors at least 2 m wide; the wall axis is found by probing the two jambs.
        static void FindDoors()
        {
            doors.Clear();
            var candidates = new List<(string id, Vector3 position, float width)>();
            foreach (var component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (component == null || component.GetType().Name != "PoolV12Part") continue;
                var type = component.GetType();
                string category = type.GetField("category")?.GetValue(component) as string;
                if (category != "Door" && category != "ExternalDoor") continue;
                if (type.GetField("floor")?.GetValue(component) is int floor && floor != 1) continue;
                float width = type.GetField("openingWidth")?.GetValue(component) is float w ? w : 0;
                if (width < 2f) continue;
                var position = (Vector3)type.GetField("designPosition").GetValue(component);
                candidates.Add(((string)type.GetField("designId").GetValue(component), position, width));
            }
            candidates.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            foreach (var (id, position, width) in candidates)
            {
                var center = new Vector3(position.x, 1.2f, position.z);
                Vector3 axis;
                if (Jambs(center, Vector3.right, width)) axis = Vector3.right;
                else if (Jambs(center, Vector3.forward, width)) axis = Vector3.forward;
                else continue;
                var normal = Vector3.Cross(Vector3.up, axis);
                foreach (float side in new[] { 1f, -1f })
                {
                    var origin = new Vector3(position.x, 0, position.z) + normal * (4f * side);
                    if (!Physics.Raycast(origin + Vector3.up * 2f, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    origin.y = floor.point.y + .025f;
                    // The straight line from the source to the door centre must be open.
                    var eye = origin + Vector3.up * 1.2f;
                    if (Physics.Linecast(eye, center, ~0, QueryTriggerInteraction.Ignore)) continue;
                    doors.Add(new Door { Id = id, Center = new Vector3(position.x, origin.y, position.z), Axis = axis, Normal = normal * side, Origin = origin, Width = width });
                    break;
                }
                if (doors.Count == 3) break;
            }
        }

        static bool Jambs(Vector3 center, Vector3 axis, float width)
        {
            float half = width * .5f;
            bool a = Physics.Raycast(center, axis, out var hitA, half + .6f, ~0, QueryTriggerInteraction.Ignore);
            bool b = Physics.Raycast(center, -axis, out var hitB, half + .6f, ~0, QueryTriggerInteraction.Ignore);
            return a && b && Mathf.Abs(hitA.distance - half) < .35f && Mathf.Abs(hitB.distance - half) < .35f;
        }

        // Rays inside the source→jamb cone that pass the door, and the drawable leaked arc at full radius
        // (quads beyond the door whose two ends are not split by a corner fade).
        static string Measure(WalkingFootRings rings, Door door)
        {
            var list = (IList)typeof(WalkingFootRings).GetField("rings", Flags).GetValue(rings);
            object ring = null;
            foreach (var entry in list)
                if ((bool)entry.GetType().GetField("Sound").GetValue(entry)) ring = entry;
            if (ring == null) return $"{door.Id}: no ring";
            var shape = ring.GetType().GetField("Shape").GetValue(ring);
            var shapeType = shape.GetType();
            var angles = (float[])shapeType.GetField("Angle").GetValue(shape);
            var reaches = (float[])shapeType.GetField("Reach").GetValue(shape);
            int count = (int)shapeType.GetField("RayCount").GetValue(shape);
            int used = (int)shapeType.GetField("RaysUsed").GetValue(shape);
            float radius = (float)shapeType.GetField("Radius").GetValue(shape);

            Vector3 jambA = door.Center + door.Axis * (door.Width * .5f), jambB = door.Center - door.Axis * (door.Width * .5f);
            float a = Angle(door.Origin, jambA), b = Angle(door.Origin, jambB);
            float doorDistance = Horizontal(door.Center - door.Origin);
            bool Inside(float angle) => InArc(angle, a, b);
            int through = 0;
            for (int i = 0; i < count; i++)
                if (Inside(angles[i]) && reaches[i] > doorDistance + .3f) through++;

            var mesh = (Mesh)ring.GetType().GetField("Mesh").GetValue(ring);
            var ray = new List<Vector4>(); var other = new List<Vector4>();
            mesh.GetUVs(0, ray); mesh.GetUVs(5, other);
            float arc = 0;
            int stride = other.Count == ray.Count && ray.Count % 4 == 0 ? 4 : 2;
            for (int v = 0; v + stride < ray.Count; v += stride)
            {
                int w = stride == 4 ? v + 2 : v + 2;
                float angle0 = Mathf.Atan2(ray[v].y, ray[v].x), angle1 = Mathf.Atan2(ray[w].y, ray[w].x);
                if (!Inside(angle0) || !Inside(angle1)) continue;
                float front0 = Mathf.Min(radius, ray[v].z), front1 = Mathf.Min(radius, ray[w].z);
                if (front0 <= doorDistance + .3f || front1 <= doorDistance + .3f) continue;
                if (Mathf.Abs(front0 - front1) >= .3f) continue;
                arc += Mathf.Min(front0, front1) * Mathf.Abs(Mathf.DeltaAngle(angle0 * Mathf.Rad2Deg, angle1 * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
            }
            float cone = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg));
            string extra = Stat(shape, "DoorRays") + Stat(shape, "DoorsSkipped");
            return $"{door.Id} width={door.Width} distance={doorDistance:0.0}m cone={cone:0.0}° rays={count} raysUsed={used} " +
                   $"raysThroughDoor={through} leakedArc={arc:0.00}m (ideal ≈ {cone * Mathf.Deg2Rad * radius:0.0}m at {radius}m){extra}";
        }

        static string Stat(object shape, string name)
        {
            var field = shape.GetType().GetField(name);
            return field == null ? "" : $" {name}={field.GetValue(shape)}";
        }

        static float Angle(Vector3 from, Vector3 to) => Mathf.Atan2(to.z - from.z, to.x - from.x);
        static float Horizontal(Vector3 v) => new Vector2(v.x, v.z).magnitude;
        static bool InArc(float angle, float a, float b)
        {
            float span = Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg);
            float offset = Mathf.DeltaAngle(a * Mathf.Rad2Deg, angle * Mathf.Rad2Deg);
            return span >= 0 ? offset >= 0 && offset <= span : offset <= 0 && offset >= span;
        }

        // From behind and above the source, looking through the door into the next room.
        static void Capture(NetworkPlayer player, Door door, string suffix)
        {
            string directory = Environment.GetEnvironmentVariable("MARCO_RIPPLE_SHOT");
            if (string.IsNullOrEmpty(directory)) return;
            var camera = player.GetComponentInChildren<Camera>();
            if (camera == null) return;
            var previousPosition = camera.transform.position;
            var previousRotation = camera.transform.rotation;
            var texture = new RenderTexture(1280, 720, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.transform.position = door.Origin + door.Normal * 2.5f + Vector3.up * 2.6f;
                camera.transform.rotation = Quaternion.LookRotation((door.Center - door.Normal * 3f) - camera.transform.position);
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "door_" + door.Id + suffix + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                camera.transform.position = previousPosition;
                camera.transform.rotation = previousRotation;
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        static void Finish(int code)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(code);
        }
    }
}
