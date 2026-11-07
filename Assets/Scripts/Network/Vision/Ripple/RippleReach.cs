using System;
using System.Collections.Generic;
using Marco.Network.Audio;
using UnityEngine;

namespace Marco.Network.Vision
{
    // Where a ring may go, computed once when it is created (기획서 §16.4 [10-26]): every direction
    // stops at a wall or locker row, follows the floor up and down stairs, stops at a ledge, and never
    // crosses a storey. Rays are cast in a few directions; the mesh interpolates between them.
    internal sealed class RippleShape
    {
        public const int MaxRays = 64, MaxSamples = 16;
        public int RayCount, SampleCount, RaysUsed;
        public float Radius, Step;
        public bool Water, Capped;
        public readonly float[] Angle = new float[MaxRays];
        public readonly float[] Reach = new float[MaxRays];
        public readonly float[] Heights = new float[MaxRays * MaxSamples];  // relative to the source point

        public void Reset(float radius, int samples)
        {
            Radius = radius;
            SampleCount = Mathf.Clamp(samples, 2, MaxSamples);
            Step = radius / (SampleCount - 1);
            RayCount = RaysUsed = 0;
            Water = Capped = false;
        }
    }

    internal static class RippleReach
    {
        // Rays the build will cast at most, used to decide whether it fits this frame's budget.
        public static int Estimate(SoundWorld world, Vector3 origin, float radius)
        {
            if (IsWater(world, origin, out _)) return RippleVisualSettings.WaterRayDirections + 4;
            int directions = RippleVisualSettings.RayDirections(radius);
            int samples = RippleVisualSettings.ProfileSamples(radius);
            return RippleVisualSettings.FloorProbeDrops.Length + directions * (samples - 1) * 2 + directions * 2;
        }

        public static void Build(SoundWorld world, Vector3 origin, float radius, RippleShape shape)
        {
            int start = world.VisualQueryCount;
            int Used() => world.VisualQueryCount - start;
            radius = Mathf.Max(.05f, radius);

            if (IsWater(world, origin, out float surface))
            {
                // Water-surface ring (D4): flat on the surface, stops at the pool walls.
                shape.Reset(radius, 2);
                shape.Water = true;
                float lift = surface + RippleVisualSettings.SurfaceLift - origin.y;
                int count = RippleVisualSettings.WaterRayDirections;
                var from = new Vector3(origin.x, surface + .1f, origin.z);
                for (int i = 0; i < count; i++)
                {
                    float angle = i * Mathf.PI * 2f / count;
                    float reach = world.VisualReach(from, Direction(angle), radius);
                    Add(shape, angle, reach, lift, lift);
                }
                shape.RaysUsed = Used();
                return;
            }

            shape.Reset(radius, RippleVisualSettings.ProfileSamples(radius));
            bool onFloor = FindFloor(world, origin, out var floorOrigin);
            int directions = RippleVisualSettings.RayDirections(radius);
            for (int i = 0; i < directions; i++)
            {
                float angle = i * Mathf.PI * 2f / directions;
                March(world, origin, floorOrigin, onFloor, angle, shape, start);
            }
            Refine(world, origin, floorOrigin, onFloor, shape, start);
            shape.RaysUsed = Used();
        }

        static bool IsWater(SoundWorld world, Vector3 origin, out float surface)
        {
            surface = 0;
            if (!world.TryPool(origin, out var pool)) return false;
            surface = pool.center.y;
            return origin.y < surface + RippleVisualSettings.WaterSurfaceReach;
        }

        // Voice is emitted at eye height and valves sit on walls: look for the walkable floor below.
        static bool FindFloor(SoundWorld world, Vector3 origin, out Vector3 floor)
        {
            foreach (float drop in RippleVisualSettings.FloorProbeDrops)
                if (world.VisualFloor(origin + Vector3.down * drop, out floor)) return true;
            floor = origin;
            return false;
        }

        // One direction: step along the floor; stop at a wall (VisualReach) or where no floor continues.
        static void March(SoundWorld world, Vector3 origin, Vector3 floorOrigin, bool onFloor, float angle,
            RippleShape shape, int start)
        {
            int index = shape.RayCount;
            if (index >= RippleShape.MaxRays) return;
            var direction = Direction(angle);
            float baseHeight = floorOrigin.y - origin.y;
            int row = index * RippleShape.MaxSamples;
            shape.Heights[row] = baseHeight;
            float reach = shape.Radius;
            Vector3 previous = onFloor ? floorOrigin : origin;
            int last = 0;
            for (int j = 1; j < shape.SampleCount; j++)
            {
                if (world.VisualQueryCount - start >= RippleVisualSettings.RaysPerRing - 3)
                {
                    shape.Capped = true;
                    reach = (j - 1) * shape.Step;
                    break;
                }
                Vector3 next = origin + direction * (j * shape.Step);
                next.y = previous.y;
                if (onFloor && !FollowFloor(world, next, previous.y, out next))
                {
                    reach = (j - 1) * shape.Step;
                    break;
                }
                Vector3 delta = next - previous;
                float length = delta.magnitude;
                if (length > .0001f)
                {
                    float hit = world.VisualReach(previous + Vector3.up * .1f, delta / length, length);
                    if (hit < length - .001f)
                    {
                        float fraction = Mathf.Max(0, hit - .025f) / length;
                        reach = (j - 1 + fraction) * shape.Step;
                        shape.Heights[row + j] = Mathf.Lerp(previous.y, next.y, fraction) - origin.y;
                        last = j;
                        break;
                    }
                }
                previous = next;
                shape.Heights[row + j] = previous.y - origin.y;
                last = j;
            }
            for (int j = last + 1; j < RippleShape.MaxSamples; j++) shape.Heights[row + j] = shape.Heights[row + last];
            shape.Angle[index] = angle;
            shape.Reach[index] = Mathf.Clamp(reach, 0, shape.Radius);
            shape.RayCount++;
        }

        // Same floor as before, or one stair flight up / down within this sample step.
        static bool FollowFloor(SoundWorld world, Vector3 next, float previousY, out Vector3 floor)
        {
            if (world.VisualFloor(next, out floor)) return true;
            next.y = previousY + .9f;
            if (world.VisualFloor(next, out floor)) return true;
            next.y = previousY - .9f;
            return world.VisualFloor(next, out floor);
        }

        // Insert directions between neighbours whose reach differs a lot (doorways, corners), budget permitting.
        static void Refine(SoundWorld world, Vector3 origin, Vector3 floorOrigin, bool onFloor, RippleShape shape, int start)
        {
            float jump = RippleVisualSettings.ReachJump(shape.Radius);
            int cost = (shape.SampleCount - 1) * 4;
            int inserted = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                int count = shape.RayCount;
                for (int i = 0; i < count && inserted < RippleVisualSettings.MaxRefinedRays; i++)
                {
                    int a = i, b = (i + 1) % count;
                    if (Mathf.Abs(shape.Reach[a] - shape.Reach[b]) <= jump) continue;
                    if (world.VisualQueryCount - start + cost > RippleVisualSettings.RaysPerRing) return;
                    float angleA = shape.Angle[a], angleB = shape.Angle[b];
                    if (angleB <= angleA) angleB += Mathf.PI * 2f;
                    March(world, origin, floorOrigin, onFloor, Mathf.Repeat((angleA + angleB) * .5f, Mathf.PI * 2f), shape, start);
                    inserted++;
                }
                Sort(shape);
            }
        }

        static void Add(RippleShape shape, float angle, float reach, float baseHeight, float height)
        {
            int index = shape.RayCount;
            if (index >= RippleShape.MaxRays) return;
            int row = index * RippleShape.MaxSamples;
            shape.Heights[row] = baseHeight;
            for (int j = 1; j < RippleShape.MaxSamples; j++) shape.Heights[row + j] = height;
            shape.Angle[index] = angle;
            shape.Reach[index] = Mathf.Clamp(reach, 0, shape.Radius);
            shape.RayCount++;
        }

        // Insertion sort by angle, moving the height rows with their rays.
        static readonly float[] rowBuffer = new float[RippleShape.MaxSamples];
        static void Sort(RippleShape shape)
        {
            for (int i = 1; i < shape.RayCount; i++)
            {
                float angle = shape.Angle[i], reach = shape.Reach[i];
                Array.Copy(shape.Heights, i * RippleShape.MaxSamples, rowBuffer, 0, RippleShape.MaxSamples);
                int j = i - 1;
                while (j >= 0 && shape.Angle[j] > angle)
                {
                    shape.Angle[j + 1] = shape.Angle[j];
                    shape.Reach[j + 1] = shape.Reach[j];
                    Array.Copy(shape.Heights, j * RippleShape.MaxSamples, shape.Heights, (j + 1) * RippleShape.MaxSamples, RippleShape.MaxSamples);
                    j--;
                }
                shape.Angle[j + 1] = angle;
                shape.Reach[j + 1] = reach;
                Array.Copy(rowBuffer, 0, shape.Heights, (j + 1) * RippleShape.MaxSamples, RippleShape.MaxSamples);
            }
        }

        public static Vector3 Direction(float angle) => new(Mathf.Cos(angle), 0, Mathf.Sin(angle));
    }

    // Visual raycasts spent in the current rendered frame, shared by every ring renderer on this client.
    internal static class RippleBudget
    {
        static int frame = -1, used;
        public static int Remaining { get { Sync(); return RippleVisualSettings.RaysPerFrame - used; } }
        public static void Spend(int rays) { Sync(); used += Mathf.Max(0, rays); }
        static void Sync() { if (frame != Time.frameCount) { frame = Time.frameCount; used = 0; } }
    }

    // Ring code runs inside sound/voice callbacks: an exception there would stop the audio or voice
    // send on the next line. Every entry point catches and reports each distinct failure once.
    internal static class RippleSafety
    {
        static readonly HashSet<string> reported = new();
        public static void Report(string where, Exception exception, UnityEngine.Object context = null)
        {
            try
            {
                if (reported.Count < 64 && reported.Add(where + "|" + exception.GetType().Name + "|" + exception.Message))
                    Debug.LogException(exception, context);
            }
            catch { /* reporting must never throw either */ }
        }
    }
}
