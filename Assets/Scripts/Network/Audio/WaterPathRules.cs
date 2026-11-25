using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network.Audio
{
    // One straight leg of an occlusion path.
    public readonly struct PathLeg
    {
        public readonly Vector3 From, To;
        public PathLeg(Vector3 from, Vector3 to) { From = from; To = to; }
        public float Length => Vector3.Distance(From, To);
    }

    // 부록 A [사운드 차폐] 수면 경유 판정 [11-16]: when the source and the listener are on opposite sides of the water surface
    // (one under water, one above — both directions, 5.6 1199행), the occlusion path is two legs: the underwater end → the
    // surface point straight above it, then the surface point → the end above water. Same side: the straight line.
    // The path's shape is an implementation reading (기획서에 없는 구현 해석, 11-16); distance stays the straight 3D distance.
    public static class WaterPathRules
    {
        public static List<PathLeg> Build(Vector3 a, bool aUnder, Vector3 b, bool bUnder, float surfaceY)
        {
            var legs = new List<PathLeg>(2);
            Build(a, aUnder, b, bUnder, surfaceY, legs);
            return legs;
        }

        // Allocation-free form for the server's per-listener judgement.
        public static void Build(Vector3 a, bool aUnder, Vector3 b, bool bUnder, float surfaceY, List<PathLeg> legs)
        {
            legs.Clear();
            if (aUnder == bUnder)
            {
                legs.Add(new PathLeg(a, b));
                return;
            }
            Vector3 under = aUnder ? a : b;
            var surface = new Vector3(under.x, surfaceY, under.z);
            legs.Add(new PathLeg(a, surface));
            legs.Add(new PathLeg(surface, b));
        }
    }
}
