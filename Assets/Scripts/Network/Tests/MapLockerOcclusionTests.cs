using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Marco.Network.Player;
using Marco.Network.Round;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Marco.Network.Tests
{
    // 기획서 §5.6 락커 열 = Wall · 맵설계 9.3 [11-16] 씬 배치 정본: the 154 locker instances (케비넷.fbx) are grouped into
    // row segments whose parent carries PoolV12Part (Wall), so SoundWorld counts one wall per segment, not per locker.
    // Uses the real SoundWorld (internal, by reflection) on the real map scene, opened additively.
    public class MapLockerOcclusionTests
    {
        private const string MapScene = "Assets/Scenes/Maps/IndoorPool_v1_2.unity";
        private const string LockerFbx = "Assets/Object/케비넷.fbx";
        private const float ProbeRadius = 100f;
        private static readonly BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private Scene scene;
        private object soundWorld;
        private MethodInfo metadata, hearResolved, hearResolvedWithDuration, hasClearSight, waterCrossings, hearingOccluders, structureAbove;
        private GameObject listener;

        [OneTimeSetUp]
        public void Open()
        {
            scene = EditorSceneManager.OpenScene(MapScene, OpenSceneMode.Additive);
            Physics.SyncTransforms();
            var type = typeof(NetworkPlayer).Assembly.GetType("Marco.Network.Audio.SoundWorld", true);
            soundWorld = Activator.CreateInstance(type, true);
            metadata = type.GetMethod("Metadata", Any);
            hearResolved = type.GetMethods(Any).Single(m => m.Name == "HearResolved" && m.GetParameters().Length == 7);
            hearResolvedWithDuration = type.GetMethods(Any).Single(m => m.Name == "HearResolved" && m.GetParameters().Length == 8);
            hasClearSight = type.GetMethod("HasClearSight", Any);
            waterCrossings = type.GetMethod("WaterSurfaceCrossings", Any);
            hearingOccluders = type.GetMethod("CountHearingOccluders", Any);
            structureAbove = type.GetMethod("StructureAbove", Any);
            listener = new GameObject("occlusion listener");
            listener.AddComponent(typeof(NetworkPlayer));
        }

        [OneTimeTearDown]
        public void Close()
        {
            if (listener != null) Object.DestroyImmediate(listener);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
        }

        private List<GameObject> Lockers() => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject)
            .Where(go => PrefabUtility.IsAnyPrefabInstanceRoot(go) &&
                AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(go)) == LockerFbx)
            .ToList();

        // The perceived radius SoundWorld gives a sound at `from` heard at `to` (0 when not heard).
        private float Perceived(Vector3 from, Vector3 to)
        {
            listener.transform.position = to;
            Physics.SyncTransforms();
            var args = new object[] { from, listener.GetComponent(typeof(NetworkPlayer)), ProbeRadius, null, null, null, null };
            bool heard = (bool)hearResolved.Invoke(soundWorld, args);
            return heard ? (float)args[6] : 0f;
        }

        // §5.6 perceivedDuration factor SoundWorld gives this line (1 when unoccluded).
        private float DurationScale(Vector3 from, Vector3 to)
        {
            listener.transform.position = to;
            Physics.SyncTransforms();
            var args = new object[] { from, listener.GetComponent(typeof(NetworkPlayer)), ProbeRadius, null, null, null, null, null };
            Assert.IsTrue((bool)hearResolvedWithDuration.Invoke(soundWorld, args), "heard");
            return (float)args[7];
        }

        // Walls between two points: log2 of (unblocked radius / perceived radius) — the same ray length, so the
        // listener's role multiplier cancels out.
        private int Walls(Vector3 from, Vector3 to)
        {
            float open = Perceived(new Vector3(-100f, 50f, -100f), new Vector3(-100f, 50f, -100f) + (to - from));
            float heard = Perceived(from, to);
            Assert.Greater(open, 0f, "open probe");
            Assert.Greater(heard, 0f, "heard");
            int walls = Mathf.RoundToInt(Mathf.Log(open / heard, 2f));
            TestContext.WriteLine($"{from} -> {to}: {walls} wall(s); hits: {Hits(from, to)}");
            return walls;
        }

        // What the probe line crosses (for the message): each collider with its PoolV12Part designId and category.
        private string Hits(Vector3 from, Vector3 to)
        {
            var hits = Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), ~0, QueryTriggerInteraction.Ignore);
            return string.Join(" | ", hits.Select(h =>
            {
                object part = metadata.Invoke(soundWorld, new object[] { h.collider });
                string id = part == null ? "-" : part.GetType().GetField("DesignId").GetValue(part) + "/" + part.GetType().GetField("Category").GetValue(part);
                return h.collider.name + "@" + h.distance.ToString("0.00") + "[" + id + "]";
            }));
        }

        [Test]
        public void AllLockers_AreClassifiedAsWall()
        {
            var lockers = Lockers();
            Assert.AreEqual(154, lockers.Count, "케비넷.fbx instances");
            int colliders = 0;
            foreach (var locker in lockers)
                foreach (var collider in locker.GetComponentsInChildren<Collider>(true))
                {
                    colliders++;
                    object part = metadata.Invoke(soundWorld, new object[] { collider });
                    Assert.IsNotNull(part, locker.name + " has no PoolV12Part above it");
                    Assert.AreEqual("Wall", part.GetType().GetField("Category").GetValue(part), locker.name);
                }
            Assert.Greater(colliders, 0, "the locker model has colliders (addColliders)");
            var parents = lockers.Select(l => l.transform.parent).Distinct().ToList();
            Assert.AreEqual(22, parents.Count, "22 row segments");
            Assert.IsTrue(parents.All(p => p != null && p.name.StartsWith("LockerRow_z")), "every locker sits under a LockerRow parent");
        }

        // Row segments are named by their collider bounds (the locker mesh sits about 4 m from its pivot along x).
        [Test]
        public void IntoOneRowSegment_IsOneWall()
        {
            // LockerRow_z9.56_x9.2-14.9 (ML, against the south wall z 9): from the room into the row, stopping
            // before the wall — only the locker row is crossed.
            Assert.AreEqual(1, Walls(new Vector3(12f, 1f, 11.5f), new Vector3(12f, 1f, 9.3f)));
        }

        [Test]
        public void AlongOneRowSegment_IsStillOneWall()
        {
            // LockerRow_z13.19_x9.2-14.9: lengthwise through its 7 lockers — one part, so one wall.
            Assert.AreEqual(1, Walls(new Vector3(16f, 1f, 13.19f), new Vector3(9.5f, 1f, 13.19f)));
        }

        [Test]
        public void BackToBackRows_AreOneWall()
        {
            // Back-to-back rows z13.19 (pivot z 12.89) and z13.97 (pivot z 14.26), x 9.2–14.9, touching (gap −0.016 m):
            // straight across both. [11-16 V19 E] touching parts count once — was 2 before the merge rule.
            Assert.AreEqual(1, Walls(new Vector3(12f, 1f, 12.2f), new Vector3(12f, 1f, 14.9f)));
        }

        [Test]
        public void TwoRowsAcrossAnAisle_AreTwoWalls()
        {
            // z9.56 (9.17–9.94) and z13.19 (12.79–13.59) with the 2.85 m aisle between them: two separate blocks.
            Assert.AreEqual(2, Walls(new Vector3(12f, 1f, 9.5f), new Vector3(12f, 1f, 13.4f)));
        }

        [Test]
        public void RoomWallWithItsLockerRow_IsOneWall()
        {
            // The ML south wall (z 9) and the row against it (z 9.17–9.94, gap 0.066 m): from the room out into the lobby.
            Assert.AreEqual(1, Walls(new Vector3(12f, 1f, 11f), new Vector3(12f, 1f, 8f)));
        }

        [Test]
        public void DoubleWallAtX25_IsOneWall()
        {
            // Wall_ML_Z25_9 (x 24.90–25.10) and SideWall_25.35 (x 25.25–25.45), 0.15 m apart: was 2, now 1.
            Assert.AreEqual(1, Walls(new Vector3(24f, 1f, 11f), new Vector3(26f, 1f, 11f)));
        }

        [Test]
        public void StoreyFloor_IsStillTwoWalls()
        {
            // ML (1st floor) straight up into the CL lobby above (2nd floor y 3.5): a storey slab block counts 2.
            Assert.AreEqual(2, Walls(new Vector3(20f, 1.5f, 20.5f), new Vector3(20f, 5f, 20.5f)));
        }

        [Test]
        public void StoreyFloorAndTheWallUnderIt_AreThreeWalls()
        {
            // From the CL lobby (2nd floor) down through Floor_CL_18_32 and into Wall_ML_Z25_16 under it, ending in the sealed
            // PS1 space behind the wall: the slab (2) and the wall block (1) are different families — 3 [11-16 V20].
            Assert.AreEqual(3, Walls(new Vector3(24f, 5f, 20f), new Vector3(26f, 1.5f, 20f)));
        }

        private bool ClearSight(Vector3 from, Vector3 to)
        {
            Physics.SyncTransforms();
            bool clear = (bool)hasClearSight.Invoke(soundWorld, new object[] { from, to });
            TestContext.WriteLine($"sight {from} -> {to}: clear={clear}; hits: {Hits(from, to)}");
            return clear;
        }

        // 7절 · 부록 A [아이템] 섬광 대상 [11-16] — clicker sight uses the sound-occlusion structures only.
        [Test]
        public void ClickerSight_WallBlocks()
        {
            // AID → ML through Wall_AID_Z9_9 (x 9), 4 m.
            Assert.IsFalse(ClearSight(new Vector3(7f, 1f, 11f), new Vector3(11f, 1f, 11f)));
        }

        [Test]
        public void ClickerSight_LockerRowBlocks()
        {
            // ML, across the back-to-back rows z13.19 / z13.97 (x 9.2–14.9), 4 m.
            Assert.IsFalse(ClearSight(new Vector3(12f, 1f, 11.5f), new Vector3(12f, 1f, 15.5f)));
        }

        [Test]
        public void ClickerSight_FurnitureDoesNotBlock()
        {
            // A 4 m line straight across a 긴나무의자 (wooden bench FBX, no PoolV12Part) that crosses no structure.
            var benches = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true))
                .Where(c => !c.isTrigger && c.GetComponentInParent<Transform>().root.name.Contains("긴나무의자")).ToList();
            Assert.Greater(benches.Count, 0, "bench colliders");
            foreach (var bench in benches)
            {
                var b = bench.bounds;
                Vector3 axis = b.size.x < b.size.z ? Vector3.right : Vector3.forward;   // across the bench's short side
                Vector3 from = new Vector3(b.center.x, Mathf.Min(b.max.y - .05f, b.min.y + .3f), b.center.z) - axis * 2f, to = from + axis * 4f;
                bool hitsBench = Physics.RaycastAll(from, axis, 4f, ~0, QueryTriggerInteraction.Ignore).Any(h => h.collider == bench);
                bool crossesStructure = Physics.RaycastAll(from, axis, 4f, ~0, QueryTriggerInteraction.Ignore)
                    .Any(h => metadata.Invoke(soundWorld, new object[] { h.collider }) is { } p && (bool)p.GetType().GetProperty("IsSoundStructure").GetValue(p));
                if (!hitsBench || crossesStructure) continue;
                TestContext.WriteLine("bench " + bench.transform.root.name + " / " + bench.name + " bounds " + b.min.ToString("F2") + " .. " + b.max.ToString("F2"));
                Assert.IsTrue(ClearSight(from, to), "a bench does not block the flash");
                return;
            }
            Assert.Fail("no bench with a clear 4 m line across it");
        }

        // 2026-11-16 결정: the water surface is a sound attenuation rule, not a sight structure.
        [Test]
        public void ClickerSight_WaterSurfaceDoesNotBlock()
        {
            Vector3 from = new Vector3(4f, .5f, 44f), to = new Vector3(4.5f, -.6f, 44.5f);   // into the teaching pool, no structure
            Assert.AreEqual(1, (int)waterCrossings.Invoke(soundWorld, new object[] { from, to }), "the segment crosses the surface");
            Assert.IsTrue(ClearSight(from, to));
        }

        // ---- 부록 A [태그 판정] 벽 차단 [11-16]: TagBlockRules.PathBlocked (the segment NetworkRound.TryFindTagTarget tests) ----

        private static bool TagBlocked(Vector3 from, Vector3 to)
        {
            Physics.SyncTransforms();
            bool blocked = TagBlockRules.PathBlocked(from, to, null, null);
            var hits = Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .OrderBy(h => h.distance).Select(h => $"{h.collider.transform.root.name}/{h.collider.name}@{h.distance:F2}[{Marco.Network.Map.MapParts.Read(h.collider)?.DesignId ?? "-"}]");
            TestContext.WriteLine($"tag {from:F2} -> {to:F2}: blocked={blocked}; hits: {string.Join(" | ", hits)}");
            return blocked;
        }

        // 1-d segments: the Seeker stands on the deck with its capsule centre 0.35 m outside the water edge (origin = deck + 0.9),
        // the Runner swims (sphere r 0.35 at the surface − 0.1); the end is the Runner's closest point. d = horizontal centre distance.
        [TestCase(3f, 47f, -1f, .5f)] [TestCase(3f, 47f, -1f, 1f)] [TestCase(3f, 47f, -1f, 1.6f)] [TestCase(3f, 47f, -1f, 2.4f)]      // teaching W
        [TestCase(15f, 47.5f, -1f, 1f)] [TestCase(15f, 47.5f, -1f, 1.6f)] [TestCase(15f, 47.5f, -1f, 2.4f)]                          // competition W (1.8)
        [TestCase(40f, 47.5f, 1f, 1f)] [TestCase(40f, 47.5f, 1f, 1.6f)] [TestCase(40f, 47.5f, 1f, 2.4f)]                             // competition E (3.5)
        public void Tag_DeckToSwimmer_NotBlockedBySideWall(float edgeX, float z, float outward, float d)
        {
            var origin = new Vector3(edgeX + outward * .35f, .9f, z);
            var centre = new Vector3(origin.x - outward * d, -.1f, z);
            var end = centre + (origin - centre).normalized * .35f;
            Assert.IsFalse(TagBlocked(origin, end));
        }

        [Test]
        public void Tag_LockerRowBlocks() => Assert.IsTrue(TagBlocked(new Vector3(12f, 1f, 11.5f), new Vector3(12f, 1f, 15.5f)));

        [Test]
        public void Tag_RoomWallBlocks() => Assert.IsTrue(TagBlocked(new Vector3(7f, 1f, 11f), new Vector3(11f, 1f, 11f)));

        [TestCase("긴나무의자")]
        [TestCase("신발장")]
        [TestCase("oCol")]
        public void Tag_FurnitureAndPillarsDoNotBlock(string name)
        {
            var colliders = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(true))
                .Where(c => !c.isTrigger && c.enabled && c.GetComponentsInParent<Transform>(true).Any(t => t.name.Contains(name))).ToList();
            Assert.Greater(colliders.Count, 0, name);
            foreach (var target in colliders)
            {
                var b = target.bounds;
                if (b.size.y < .3f) continue;
                Vector3 axis = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
                Vector3 from = new Vector3(b.center.x, b.min.y + Mathf.Min(.5f, b.size.y * .5f), b.center.z) - axis * 1.5f, to = from + axis * 3f;
                var hits = Physics.RaycastAll(from, axis, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (!hits.Any(h => h.collider == target)) continue;
                if (hits.Any(h => Marco.Network.Map.MapParts.Read(h.collider) is { TagBlocker: true })) continue;
                TestContext.WriteLine($"{name}: {string.Join("/", target.GetComponentsInParent<Transform>(true).Reverse().Select(t => t.name))} bounds {b.min:F2} .. {b.max:F2}");
                Assert.IsFalse(TagBlocked(from, to), "the old rule blocked on any collider; furniture and pillars no longer do");
                return;
            }
            Assert.Fail("no 3 m line across a " + name + " that crosses no tag-blocking part");
        }

        // ---- 부록 A [사운드 차폐] 수면 경유 판정 [11-16] ----

        private (int walls, int water) Hearing(Vector3 source, Vector3 ear)
        {
            Physics.SyncTransforms();
            var args = new object[] { source, ear, Vector3.Distance(source, ear), 0 };
            int walls = (int)hearingOccluders.Invoke(soundWorld, args);
            return (walls, (int)args[3]);
        }

        // HearResolved's radius rule (SoundWorld.cs) for a 12 m forced pulse: Seeker ×1.2, ×0.5 per wall / surface.
        private static string Judge(float distance, (int walls, int water) c, float hearing)
        {
            float perceived = 12f * hearing * Mathf.Pow(.5f, c.walls + c.water);
            bool heard = distance < 12f * hearing && distance < perceived;
            bool muffled = c.walls + c.water > 0;
            return $"{perceived:F2} {(heard ? "heard" : "not heard")} ring={(heard && !muffled ? "Y" : "N")} gain={(heard ? (muffled ? .5f : 1f - distance / perceived) : 0f):F3}";
        }

        private static readonly (string source, Vector3 at, (string, Vector3)[] ears)[] V24Rows =
        {
            ("Drain 1", new Vector3(36f, -3.5f, 47.5f), new[] { ("edge", new Vector3(40.4f, 1.6f, 47.5f)), ("6m", new Vector3(42f, 1.6f, 47.5f)), ("10m", new Vector3(46f, 1.6f, 47.5f)) }),
            ("Drain 2", new Vector3(7f, -3.5f, 47f), new[] { ("edge", new Vector3(11.4f, 1.6f, 47f)), ("6m", new Vector3(13f, 1.6f, 47f)), ("10m", new Vector3(14.07f, 1.6f, 54.07f)) }),
            ("Valve B", new Vector3(38.5f, -2.31f, 52f), new[] { ("edge", new Vector3(40.35f, 1.6f, 52f)), ("6m", new Vector3(44.5f, 1.6f, 52f)), ("10m", new Vector3(48.5f, 1.6f, 52f)) }),
            ("Valve E", new Vector3(4.5f, -.11f, 51f), new[] { ("edge", new Vector3(2.6f, 1.6f, 51f)), ("6m", new Vector3(10.04f, 1.6f, 53.3f)), ("10m", new Vector3(14.5f, 1.6f, 51f)) }),
            ("V23 diver", new Vector3(3.6f, -.8f, 47f), new[] { ("west deck", new Vector3(1.5f, 1.6f, 47f)) }),
        };

        [Test]
        public void WaterPath_V24Table()
        {
            foreach (var row in V24Rows)
                foreach (var (label, ear) in row.ears)
                {
                    var c = Hearing(row.at, ear);
                    float d = Vector3.Distance(row.at, ear);
                    TestContext.WriteLine($"{row.source} {row.at:F2} -> {label} {ear:F2} d={d:F2}: walls={c.walls} water={c.water} | Survivor {Judge(d, c, 1f)} | Seeker {Judge(d, c, 1.2f)}");
                }
            var drain2Edge = Hearing(new Vector3(7f, -3.5f, 47f), new Vector3(11.4f, 1.6f, 47f));
            Assert.AreEqual((0, 1), drain2Edge, "drain 2 → teaching east edge: the path goes up the sump, through the surface");
            StringAssert.StartsWith("7.20 heard ring=N", Judge(6.74f, drain2Edge, 1.2f), "Seeker hears it muffled");
            StringAssert.StartsWith("6.00 not heard", Judge(6.74f, drain2Edge, 1f), "Survivor: 6.74 > 6");
        }

        [Test]
        public void WaterPath_SameSide_Unchanged()
        {
            // sump-bottom diver → diver in the shallow part (both under water): still the straight line through the sump wall
            var underwater = Hearing(new Vector3(7f, -3.2f, 47f), new Vector3(4.5f, -.8f, 44f));
            Assert.AreEqual(1, underwater.walls, "sump wall");
            Assert.AreEqual(0, underwater.water);
            // deck → deck across the teaching pool: straight, nothing between
            Assert.AreEqual((0, 0), Hearing(new Vector3(2f, 1.6f, 47f), new Vector3(12f, 1.6f, 47f)));
        }

        // 결정 11-16 예외: an underwater end with a structure above it before the surface keeps the straight line.
        [Test]
        public void WaterPath_CoveredWaterScan()
        {
            var covered = new List<string>();
            int probes = 0;
            foreach (var (pool, min, max) in new[] { ("teaching", new Vector2(3f, 41f), new Vector2(11f, 53f)), ("competition", new Vector2(15f, 37f), new Vector2(40f, 58f)) })
                for (float x = min.x + .25f; x < max.x; x += .5f)
                    for (float z = min.y + .25f; z < max.y; z += .5f)
                    {
                        if (!Physics.Raycast(new Vector3(x, -.01f, z), Vector3.down, out var floor, 5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        foreach (float lift in new[] { .05f, .5f })
                        {
                            var p = floor.point + Vector3.up * lift;
                            if (p.y >= 0f) continue;
                            probes++;
                            if ((bool)structureAbove.Invoke(soundWorld, new object[] { p, 0f })) covered.Add($"{pool} {p:F2}");
                        }
                    }
            TestContext.WriteLine($"{probes} underwater probes, covered: {covered.Count}" + (covered.Count > 0 ? " | " + string.Join(" | ", covered.Take(40)) : ""));
            Assert.AreEqual(0, covered.Count, "no covered water in IndoorPool_v1_2");
        }

        // 부록 A [사운드 차폐] 재판정 [11-16] + 수면 경유: a deck Seeker out of reach of the drain 2 forced pulse walks to the
        // teaching-pool edge while it lasts → upgraded to muffled (path up the sump, through the surface), no ring.
        [Test]
        public void Recheck_DeckSeekerWalksToDrainTwoEdge_MuffledUpgrade()
        {
            Vector3 drain = new Vector3(7f, -3.5f, 47f), far = new Vector3(14.07f, 1.6f, 54.07f), edge = new Vector3(11.4f, 1.6f, 47f);
            Vector3 ear = far;
            OcclusionRecheck.Hear hear = (int id, Vector3 origin, float radius, out HearSample sample) =>
            {
                var c = Hearing(origin, ear);
                float d = Vector3.Distance(origin, ear), r = radius * PulseSpec.SeekerHearing;
                float perceived = r * Mathf.Pow(PulseSpec.WallOcclusion, c.walls) * Mathf.Pow(PulseSpec.WaterOcclusion, c.water);
                bool heard = d < r && d < perceived;
                sample = new HearSample { Heard = heard, Blocked = c.walls + c.water > 0, Point = origin, Gain = .5f, PerceivedRadius = perceived,
                    DurationScale = PulseSpec.OccludedDurationScale(c.walls, c.water, false) };
                return heard;
            };
            hear(1, drain, PulseSpec.DrainWork, out var first);
            Assert.IsFalse(first.Heard, "out of reach at the start");
            var tracker = new OcclusionRecheck();
            var pulse = tracker.Begin(drain, PulseSpec.DrainWork, PulseSpec.DrainWorkPeriod, 0f);
            var sent = new List<(HearSample hearing, float elapsed)>();
            pulse.Send = (id, hearing, elapsed) => sent.Add((hearing, elapsed));
            pulse.Note(1, HearState.None);
            tracker.Commit(pulse);
            for (float t = 0f; t <= 3f; t += .02f)
            {
                if (t >= .6f) ear = edge;
                tracker.Tick(t, id => true, hear);
            }
            Assert.AreEqual(1, sent.Count, "one upgrade");
            Assert.IsTrue(sent[0].hearing.Blocked, "muffled — through the surface, so no ring (Audio.cs showPulse = !blocked)");
            Assert.That(sent[0].elapsed, Is.InRange(.6f, .85f));
            TestContext.WriteLine($"upgrade at {sent[0].elapsed:F2}s: perceived {sent[0].hearing.PerceivedRadius:F2} m, duration scale {sent[0].hearing.DurationScale:F2}");
        }

        // V31 벽 통과 probe: the camera sphere inside a wall / a locker row is "inside a structure"; open air and furniture
        // are not. Also the probe cost (OverlapSphereNonAlloc + cached MapParts lookups), reported per call.
        [Test]
        public void WallPassProbe_StructuresOnly_AndItsCost()
        {
            Physics.SyncTransforms();
            var buffer = new Collider[16];
            var cache = new Dictionary<Collider, bool>();
            bool Inside(Vector3 at)
            {
                int n = Physics.OverlapSphereNonAlloc(at, Marco.Network.Vision.WallPassRules.ProbeRadius, buffer, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (!cache.TryGetValue(buffer[i], out bool s)) { var p = Marco.Network.Map.MapParts.Read(buffer[i]); cache[buffer[i]] = s = p != null && p.IsSoundStructure; }
                    if (s) return true;
                }
                return false;
            }
            Assert.IsTrue(Inside(new Vector3(9f, 1.6f, 11f)), "inside Wall_AID_Z9_9 (x 8.9 – 9.1)");
            Assert.IsFalse(Inside(new Vector3(7f, 1.6f, 11f)), "open air in AID");
            Assert.IsTrue(Inside(new Vector3(12f, 1f, 13.58f)), "between the back-to-back locker rows z13.19 / z13.97");
            Assert.IsFalse(Inside(new Vector3(36.06f, 3.85f, 20.9f)), "inside a 긴나무의자 (furniture is not a structure)");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 2000; i++) Inside(new Vector3(7f + (i % 40) * .1f, 1.6f, 11f));
            double micro = watch.Elapsed.TotalMilliseconds * 1000.0 / 2000;
            TestContext.WriteLine($"probe cost: {micro:F2} µs per frame (2000 probes across the AID / ML wall, cached part lookups)");
            Assert.Less(micro, 200.0);
        }

        [Test]
        public void DurationScale_OpenOneWallTwoWalls()
        {
            Assert.AreEqual(1f, DurationScale(new Vector3(12f, 1f, 11.5f), new Vector3(12f, 1f, 10.5f)), 1e-4f, "nothing in the way");
            Assert.AreEqual(.5f, DurationScale(new Vector3(24f, 1f, 11f), new Vector3(26f, 1f, 11f)), 1e-4f, "one wall (the merged double wall)");
            // ML west wall (x 9) and the x 25 double wall: two separate walls, 0.25 -> floor 0.5.
            Assert.AreEqual(2, Walls(new Vector3(8f, 1f, 11f), new Vector3(26f, 1f, 11f)), "two separate walls");
            Assert.AreEqual(.5f, DurationScale(new Vector3(8f, 1f, 11f), new Vector3(26f, 1f, 11f)), 1e-4f, "floor ×0.5");
        }

        [Test]
        public void ExistingWall_StillCountsOnce()
        {
            // Wall_ML_X24_9 (z 24, x 9–11.5) between ML and MS, away from the locker rows.
            Assert.AreEqual(1, Walls(new Vector3(10.25f, 1f, 22.5f), new Vector3(10.25f, 1f, 25.5f)));
        }
    }
}
