using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Marco.Network.Player;
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
        private MethodInfo metadata, hearResolved, hearResolvedWithDuration;
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
