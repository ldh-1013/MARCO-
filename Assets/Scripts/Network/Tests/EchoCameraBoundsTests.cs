using Marco.Network.Player;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // [11-24 V51 B] 기획서 3.2 · 맵설계 ⑫: the Echo body's drawn camera stays inside the same building box as its flight
    // (EchoBodyRules.Flight*), the near-plane corner distance inside every face.
    public sealed class EchoCameraBoundsTests
    {
        [Test]
        public void ClampInside_KeepsInsidePoints_AndHoldsOutsidePointsTheInsetInside()
        {
            var inside = new Vector3(32f, 3f, 31f);
            Assert.AreEqual(inside, EchoInertia.ClampInside(inside, .1f));
            var held = EchoInertia.ClampInside(new Vector3(-.1f, 9.39f, 62.1f), .1f);
            Assert.AreEqual(EchoBodyRules.FlightMinX + .1f, held.x, 1e-5f);
            Assert.AreEqual(EchoBodyRules.FlightMaxY - .1f, held.y, 1e-5f);
            Assert.AreEqual(EchoBodyRules.FlightMaxZ - .1f, held.z, 1e-5f);
            held = EchoInertia.ClampInside(new Vector3(64.1f, -9f, -1f), 0f);
            Assert.AreEqual(new Vector3(EchoBodyRules.FlightMaxX, EchoBodyRules.FlightMinY, EchoBodyRules.FlightMinZ), held);
        }

        [Test]
        public void ClampInside_NaNIsReturnedAsItWas_AndANegativeInsetIsZero()
        {
            var bad = EchoInertia.ClampInside(new Vector3(float.NaN, 30f, -9f), .1f);
            Assert.IsTrue(float.IsNaN(bad.x)); Assert.AreEqual(30f, bad.y);
            Assert.AreEqual(new Vector3(EchoBodyRules.FlightMinX, 3f, 31f), EchoInertia.ClampInside(new Vector3(-5f, 3f, 31f), -1f));
        }

        [Test]
        public void NearCornerDistance_IsTheNearPlaneCorner()
        {
            // 90° vertical, aspect 1: tan 45° = 1 both ways → near × √3.
            Assert.AreEqual(.1f * Mathf.Sqrt(3f), EchoInertia.NearCornerDistance(.1f, 90f, 1f), 1e-5f);
            Assert.GreaterOrEqual(EchoInertia.NearCornerDistance(.3f, 60f, 16f / 9f), .3f);
            Assert.AreEqual(0f, EchoInertia.NearCornerDistance(-1f, 60f, 1f));
        }
    }
}
