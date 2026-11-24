using Marco.Network.Player;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // [11-25 V65] 기획서 3.2 · 맵설계 ⑫: the Echo flight's per-position range is the grid measured in the map scene
    // (EchoHeightGridBaker → Resources/EchoHeightGrid). Invariants of the asset, then the lookup and the clamps built on it.
    // Expected heights below are the measured values (slab underside = slab top − 0.2 bounds), not design numbers.
    public sealed class EchoHeightGridTests
    {
        private static EchoHeightGrid Asset => Resources.Load<EchoHeightGrid>(EchoHeightGrid.ResourcePath);

        [Test]
        public void Asset_CoversTheBuildingOutline_AtHalfMetreCells()
        {
            var g = Asset;
            Assert.IsNotNull(g, "Resources/EchoHeightGrid is missing");
            Assert.IsTrue(g.IsValid);
            Assert.AreEqual(EchoBodyRules.FlightMinX, g.originX); Assert.AreEqual(EchoBodyRules.FlightMinZ, g.originZ);
            Assert.AreEqual(.5f, g.cellSize);
            Assert.AreEqual(Mathf.RoundToInt((EchoBodyRules.FlightMaxX - EchoBodyRules.FlightMinX) / g.cellSize), g.countX);
            Assert.AreEqual(Mathf.RoundToInt((EchoBodyRules.FlightMaxZ - EchoBodyRules.FlightMinZ) / g.cellSize), g.countZ);
            Assert.AreEqual(g.countX * g.countZ, g.floor.Length); Assert.AreEqual(g.countX * g.countZ, g.top.Length);
        }

        [Test]
        public void Asset_EveryCell_HasFloorAtOrBelowTop_InsideTheOldBox()
        {
            var g = Asset;
            for (int i = 0; i < g.floor.Length; i++)
            {
                Assert.LessOrEqual(g.floor[i], g.top[i], $"cell {i}");
                Assert.GreaterOrEqual(g.floor[i], EchoBodyRules.FlightMinY, $"cell {i}");
                Assert.LessOrEqual(g.top[i], EchoBodyRules.FlightMaxY + 1e-4f, $"cell {i}");
            }
            Assert.AreEqual(0, g.noHitCells);
        }

        [Test]
        public void Asset_RecordsWhereItWasMeasured()
        {
            var g = Asset;
            Assert.AreEqual("Assets/Scenes/Maps/IndoorPool_v1_2.unity", g.sourceScene);
            StringAssert.IsMatch(@"^\d{4}-\d{2}-\d{2}$", g.measuredAt);
            StringAssert.IsMatch(@"^[0-9a-f]{40}$", g.gitSha);
        }

        [TestCase(20f, 4.5f, 0f, 4.5f)]        // 로비 4.5
        [TestCase(27f, 12f, 0f, 6.8f)]         // S1 계단실 (씬 값 — JSON의 로비 4.5와 다름)
        [TestCase(30f, 32f, 0f, 9f)]           // 홀 데크 9.0
        [TestCase(30f, 47f, -1.8f, 9f)]        // 경영풀 위: 수조 바닥
        [TestCase(36.5f, 54f, -3.476f, 9f)]    // 깊은 단
        [TestCase(50f, 40f, 0f, 3f)]           // 직원 통로 3.0
        [TestCase(45f, 20f, 0f, 6.5f)]         // 직원 통로 위 2층 관람 로비: 층 사이 통과, 6.5
        [TestCase(30f, 20f, 0f, 6.5f)]         // 2층 로비(CL)
        [TestCase(27f, 28f, 3f, 9f)]           // 관람석 하부 덕트 공간: 바닥 3.0
        public void Lookup_IsTheMeasuredRange(float x, float z, float floor, float top)
        {
            Assert.IsTrue(EchoBoundsGrid.TryGetVerticalRange(x, z, out float f, out float t));
            Assert.AreEqual(floor, f, 2e-3f); Assert.AreEqual(top, t, 1e-3f);
        }

        [Test]
        public void Lookup_OutsideOrNaN_IsFalseAndTheBox_TheFarEdgeIsTheLastCell()
        {
            Assert.IsFalse(EchoBoundsGrid.TryGetVerticalRange(-.01f, 10f, out float f, out float t));
            Assert.AreEqual(EchoBodyRules.FlightMinY, f); Assert.AreEqual(EchoBodyRules.FlightMaxY, t);
            Assert.IsFalse(EchoBoundsGrid.TryGetVerticalRange(10f, 62.01f, out _, out _));
            Assert.IsFalse(EchoBoundsGrid.TryGetVerticalRange(float.NaN, 10f, out _, out _));
            Assert.IsTrue(EchoBoundsGrid.TryGetVerticalRange(64f, 62f, out _, out _));
        }

        [Test]
        public void ClampFlight_HoldsYInTheRange_AndKeepsXZ()
        {
            // 홀 높이 8에서 직원 통로로: 천장 아래로 눌린다 (수평은 그대로)
            var pushed = EchoBodyRules.ClampFlight(new Vector3(48.3f, 8f, 40f));
            Assert.AreEqual(48.3f, pushed.x); Assert.AreEqual(40f, pushed.z); Assert.AreEqual(3f, pushed.y, 1e-3f);
            Assert.AreEqual(new Vector3(47.7f, 8f, 40f), EchoBodyRules.ClampFlight(new Vector3(47.7f, 8f, 40f)));   // 홀 안: 그대로
            // 층 사이(1층 천장 3.0 ↔ 2층 바닥 3.5) 통과: 2층 로비 아래 높이 3.2는 안쪽
            Assert.AreEqual(new Vector3(30f, 3.2f, 20f), EchoBodyRules.ClampFlight(new Vector3(30f, 3.2f, 20f)));
            // 수조 위는 수조 바닥까지, 데크는 바닥 0까지
            Assert.AreEqual(-1.8f, EchoBodyRules.ClampFlight(new Vector3(30f, -5f, 47f)).y, 2e-3f);
            Assert.AreEqual(0f, EchoBodyRules.ClampFlight(new Vector3(20f, -5f, 60f)).y, 1e-4f);
            // 지붕 위로 못 나감
            Assert.AreEqual(4.5f, EchoBodyRules.ClampFlight(new Vector3(20f, 7f, 4.5f)).y, 1e-3f);
        }

        [Test]
        public void ClampInside_UsesTheRangeWithTheInset()
        {
            var held = EchoInertia.ClampInside(new Vector3(50f, 4f, 40f), .1f);
            Assert.AreEqual(3f - .1f, held.y, 1e-3f);
            held = EchoInertia.ClampInside(new Vector3(30f, -3f, 47f), .1f);
            Assert.AreEqual(-1.8f + .1f, held.y, 2e-3f);
            // a range thinner than two insets: the inset is dropped, the eye stays inside the range itself
            held = EchoInertia.ClampInside(new Vector3(50f, 9f, 40f), 2f);
            Assert.AreEqual(3f, held.y, 1e-3f);
        }

        [Test]
        public void BoundsPush_IsFollowed_ATeleportIsStillCut()
        {
            // 홀 → 직원 통로: 눈 높이 8.6에서 3.6으로 (한 프레임 0.1 m 수평)
            Assert.IsTrue(EchoInertia.IsBoundsPush(new Vector3(47.95f, 8.6f, 40f), new Vector3(48.05f, 3.6f, 40f)));
            // 눈이 몸보다 먼저 칸 경계를 넘는 프레임(점프 없음)과 작은 하강은 해당 없음
            Assert.IsFalse(EchoInertia.IsBoundsPush(new Vector3(47.95f, 8.6f, 40f), new Vector3(48.05f, 8.6f, 40f)));
            Assert.IsFalse(EchoInertia.IsBoundsPush(new Vector3(47.95f, 8.6f, 40f), new Vector3(48.05f, 8.5f, 40f)));
            // 수조에서 데크로 나오며 바닥에 올려짐(위쪽 밀림)도 따라간다
            Assert.IsTrue(EchoInertia.IsBoundsPush(new Vector3(20f, -1.6f, 37.05f), new Vector3(20f, 0.2f, 36.95f)));
            // 같은 칸에서 천장 근처 하강은 해당 없음
            Assert.IsFalse(EchoInertia.IsBoundsPush(new Vector3(50f, 3.6f, 40f), new Vector3(50.1f, 3.5f, 40f)));
            // 수평 2.5 m 넘는 순간이동은 해당 없음
            Assert.IsFalse(EchoInertia.IsBoundsPush(new Vector3(44f, 8.6f, 40f), new Vector3(50f, 3.6f, 40f)));

            var inertia = new EchoInertia();
            float dt = 1f / 60f;
            inertia.Step(new Vector3(47.95f, 8.6f, 40f), dt, true);
            var drawn = inertia.Step(new Vector3(48.05f, 3.6f, 40f), dt, true);
            Assert.Greater(drawn.y, 8.6f - 1.5f, "followed with inertia, not cut to the hard cap");
            Assert.IsTrue(inertia.PushActive);
            float last = drawn.y;
            for (int i = 0; i < 40; i++)
            {
                drawn = inertia.Step(new Vector3(48.05f, 3.6f, 40f), dt, true);
                Assert.LessOrEqual(drawn.y, last + 1e-5f, "monotone down"); Assert.LessOrEqual(last - drawn.y, 1f, "no cut: a step per frame, not the whole 5 m"); Assert.GreaterOrEqual(drawn.y, 3.6f - 1e-4f, "never past the target");
                last = drawn.y;
            }
            Assert.AreEqual(3.6f, drawn.y, .05f, "caught up within 40 frames (0.67 s)");
            inertia.EndPush();
            Assert.IsFalse(inertia.PushActive);
            inertia.Step(new Vector3(48.05f, 3.6f, 40f), dt, true);
            drawn = inertia.Step(new Vector3(20f, 3.6f, 10f), dt, true);   // a real teleport is still cut
            Assert.AreEqual(new Vector3(20f, 3.6f, 10f), drawn);
        }
    }
}
