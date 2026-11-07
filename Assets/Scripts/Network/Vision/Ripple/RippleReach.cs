using System;
using System.Collections.Generic;
using Marco.Network.Audio;
using UnityEngine;

namespace Marco.Network.Vision
{
    // Where a ring may go, computed once when it is created (기획서 §16.4 [10-26]): every direction
    // stops at a wall or locker row, follows the floor up and down stairs, stops at a ledge, and never
    // crosses a storey. Rays are cast in a few directions plus densely through open doorways; the mesh
    // interpolates between them.
    internal sealed class RippleShape
    {
        public const int MaxRays = 128, MaxSamples = 16;
        public int RayCount, SampleCount, RaysUsed, EdgeCount, DoorRays, DoorsSkipped;
        public float Radius, Step;
        public bool Water, Capped;
        public readonly float[] Angle = new float[MaxRays];
        public readonly float[] Reach = new float[MaxRays];
        public readonly bool[] Wall = new bool[MaxRays];                    // stopped by a wall (not a ledge or the radius)
        public readonly float[] Heights = new float[MaxRays * MaxSamples];  // relative to the source point
        // Render ends placed exactly on door-cone edges so a leaked arc ends on the jamb angle.
        public readonly float[] EdgeAngle = new float[RippleVisualSettings.MaxDoorEdges];

        public void Reset(float radius, int samples)
        {
            Radius = radius;
            SampleCount = Mathf.Clamp(samples, 2, MaxSamples);
            Step = radius / (SampleCount - 1);
            RayCount = RaysUsed = EdgeCount = DoorRays = DoorsSkipped = 0;
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
            int estimate = RippleVisualSettings.FloorProbeDrops.Length + directions * (samples + 2);
            return NearDoor(origin, radius) ? RippleVisualSettings.RaysPerRing : estimate;
        }

        public static void Build(SoundWorld world, Vector3 origin, float radius, RippleShape shape)
        {
            int start = world.VisualQueryCount;
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
                    Add(shape, angle, reach, reach < radius - .001f, lift, lift);
                }
                Sort(shape);
                shape.RaysUsed = world.VisualQueryCount - start;
                return;
            }

            shape.Reset(radius, RippleVisualSettings.ProfileSamples(radius));
            bool onFloor = FindFloor(world, origin, out var floorOrigin);
            int directions = RippleVisualSettings.RayDirections(radius);
            for (int i = 0; i < directions; i++)
                March(world, origin, floorOrigin, onFloor, i * Mathf.PI * 2f / directions, shape, start);
            if (onFloor) Doors(world, origin, floorOrigin, shape, start);
            Sort(shape);
            Refine(world, origin, floorOrigin, onFloor, shape, start);
            shape.RaysUsed = world.VisualQueryCount - start;
        }

        static bool IsWater(SoundWorld world, Vector3 origin, out float surface)
        {
            surface = 0;
            if (!world.TryPool(origin, out var pool)) return false;
            surface = pool.center.y;
            return origin.y < surface + RippleVisualSettings.WaterSurfaceReach;
        }

        static bool NearDoor(Vector3 origin, float radius)
        {
            foreach (var door in RippleDoors.All)
            {
                float dx = door.Center.x - origin.x, dz = door.Center.z - origin.z;
                if (dx * dx + dz * dz < radius * radius) return true;
            }
            return false;
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
        // On a level floor the wall check covers the whole level run with one ray; slopes and stairs are
        // checked step by step.
        static void March(SoundWorld world, Vector3 origin, Vector3 floorOrigin, bool onFloor, float angle,
            RippleShape shape, int start)
        {
            int index = shape.RayCount;
            if (index >= RippleShape.MaxRays) return;
            var direction = Direction(angle);
            int row = index * RippleShape.MaxSamples;
            shape.Heights[row] = floorOrigin.y - origin.y;
            float reach = shape.Radius;
            bool wall = false, stopped = false;
            Vector3 previous = onFloor ? floorOrigin : origin;
            Vector3 runStart = previous;
            float runStartDistance = 0;
            int last = 0;
            for (int j = 1; j < shape.SampleCount; j++)
            {
                // Keep room for closing the open run (1 ray) plus one step (up to 4 rays).
                if (world.VisualQueryCount - start >= RippleVisualSettings.RaysPerRing - 5)
                {
                    shape.Capped = true;
                    if (CheckRun(world, direction, runStart, runStartDistance, previous, out reach)) wall = true;
                    else reach = (j - 1) * shape.Step;
                    stopped = true;
                    break;
                }
                Vector3 next = origin + direction * (j * shape.Step);
                next.y = previous.y;
                if (onFloor && !FollowFloor(world, next, previous.y, out next))
                {
                    // Ledge between the last sample and this one — unless a wall in the level run comes first.
                    if (CheckRun(world, direction, runStart, runStartDistance, previous, out reach)) wall = true;
                    else reach = (j - 1) * shape.Step;
                    stopped = true;
                    break;
                }
                if (Mathf.Abs(next.y - previous.y) > .05f)
                {
                    if (CheckRun(world, direction, runStart, runStartDistance, previous, out reach)) { wall = stopped = true; break; }
                    Vector3 delta = next - previous;
                    float length = delta.magnitude;
                    float hit = world.VisualReach(previous + Vector3.up * .1f, delta / length, length);
                    if (hit < length - .001f)
                    {
                        float fraction = Mathf.Max(0, hit - .025f) / length;
                        reach = (j - 1 + fraction) * shape.Step;
                        shape.Heights[row + j] = Mathf.Lerp(previous.y, next.y, fraction) - origin.y;
                        last = j;
                        wall = stopped = true;
                        break;
                    }
                    runStart = next;
                    runStartDistance = j * shape.Step;
                }
                previous = next;
                shape.Heights[row + j] = previous.y - origin.y;
                last = j;
            }
            if (!stopped && CheckRun(world, direction, runStart, runStartDistance, previous, out float runReach))
            {
                reach = runReach;
                wall = true;
            }
            for (int j = last + 1; j < RippleShape.MaxSamples; j++) shape.Heights[row + j] = shape.Heights[row + last];
            shape.Angle[index] = Mathf.Repeat(angle, Mathf.PI * 2f);
            shape.Reach[index] = Mathf.Clamp(reach, 0, shape.Radius);
            shape.Wall[index] = wall;
            shape.RayCount++;
        }

        // A level run from `from` (at distance fromDistance along the ray) to `to`: first wall, if any.
        static bool CheckRun(SoundWorld world, Vector3 direction, Vector3 from, float fromDistance, Vector3 to, out float reach)
        {
            reach = 0;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            if (length < .0001f) return false;
            float hit = world.VisualReach(from + Vector3.up * .1f, delta / length, length);
            if (hit >= length - .001f) return false;
            reach = fromDistance + Mathf.Max(0, hit - .025f);
            return true;
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

        // Open doorways in reach: one ray just inside each jamb, then fill the cone between them at
        // DoorFillSpacing of the ring's circumference, nearest door first, within the per-ring budget.
        // A door the source cannot see (wall in between) costs one ray and is skipped.
        static readonly List<(float distance, int index)> nearby = new();
        static void Doors(SoundWorld world, Vector3 origin, Vector3 floorOrigin, RippleShape shape, int start)
        {
            var doors = RippleDoors.All;
            nearby.Clear();
            for (int i = 0; i < doors.Count; i++)
            {
                var door = doors[i];
                if (Mathf.Abs(door.Center.y - floorOrigin.y) > 1.5f) continue;
                float distance = Vector2.Distance(new Vector2(door.Center.x, door.Center.z), new Vector2(origin.x, origin.z));
                if (distance < .5f || distance > shape.Radius - .3f) continue;
                nearby.Add((distance, i));
            }
            if (nearby.Count == 0) return;
            nearby.Sort((a, b) => a.distance.CompareTo(b.distance));
            int perRay = shape.SampleCount + 2;
            foreach (var (distance, i) in nearby)
            {
                var door = doors[i];
                int used = world.VisualQueryCount - start;
                if (used + 1 + perRay * 2 > RippleVisualSettings.RaysPerRing || shape.RayCount + 2 > RippleShape.MaxRays ||
                    shape.EdgeCount + 2 > RippleVisualSettings.MaxDoorEdges)
                {
                    shape.DoorsSkipped++;
                    continue;
                }
                // The doorway must be visible from the source on the floor.
                var eye = floorOrigin + Vector3.up * .1f;
                var target = new Vector3(door.Center.x, eye.y, door.Center.z);
                float seen = world.VisualReach(eye, (target - eye).normalized, distance);
                if (seen < distance - .3f) continue;

                float a = Mathf.Atan2(door.JambA.z - origin.z, door.JambA.x - origin.x);
                float b = Mathf.Atan2(door.JambB.z - origin.z, door.JambB.x - origin.x);
                float span = Mathf.DeltaAngle(a * Mathf.Rad2Deg, b * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float inset = Mathf.Min(RippleVisualSettings.DoorEdgeInsetDegrees * Mathf.Deg2Rad, Mathf.Abs(span) * .25f) * Mathf.Sign(span);
                float edgeA = a + inset, edgeB = a + span - inset;
                int budgetRays = (RippleVisualSettings.RaysPerRing - (world.VisualQueryCount - start)) / perRay;
                int wanted = Mathf.CeilToInt(Mathf.Abs(edgeB - edgeA) * shape.Radius / RippleVisualSettings.DoorFillSpacing) + 1;
                int count = Mathf.Clamp(Mathf.Min(wanted, budgetRays, RippleShape.MaxRays - shape.RayCount), 2, 64);
                for (int k = 0; k < count; k++)
                {
                    float angle = Mathf.Lerp(edgeA, edgeB, k / (float)(count - 1));
                    int before = shape.RayCount;
                    March(world, origin, floorOrigin, true, angle, shape, start);
                    if (shape.RayCount > before) shape.DoorRays++;
                }
                shape.EdgeAngle[shape.EdgeCount++] = Mathf.Repeat(edgeA, Mathf.PI * 2f);
                shape.EdgeAngle[shape.EdgeCount++] = Mathf.Repeat(edgeB, Mathf.PI * 2f);
            }
        }

        // Insert directions between neighbours whose reach differs a lot (corners), budget permitting.
        static void Refine(SoundWorld world, Vector3 origin, Vector3 floorOrigin, bool onFloor, RippleShape shape, int start)
        {
            float jump = RippleVisualSettings.ReachJump(shape.Radius);
            int cost = shape.SampleCount + 2;
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
                    // Door cones are already sampled densely.
                    if (angleB - angleA < .5f * Mathf.Deg2Rad) continue;
                    March(world, origin, floorOrigin, onFloor, (angleA + angleB) * .5f, shape, start);
                    inserted++;
                }
                Sort(shape);
            }
        }

        static void Add(RippleShape shape, float angle, float reach, bool wall, float baseHeight, float height)
        {
            int index = shape.RayCount;
            if (index >= RippleShape.MaxRays) return;
            int row = index * RippleShape.MaxSamples;
            shape.Heights[row] = baseHeight;
            for (int j = 1; j < RippleShape.MaxSamples; j++) shape.Heights[row + j] = height;
            shape.Angle[index] = angle;
            shape.Reach[index] = Mathf.Clamp(reach, 0, shape.Radius);
            shape.Wall[index] = wall;
            shape.RayCount++;
        }

        // Insertion sort by angle, moving the height rows with their rays.
        static readonly float[] rowBuffer = new float[RippleShape.MaxSamples];
        static void Sort(RippleShape shape)
        {
            for (int i = 1; i < shape.RayCount; i++)
            {
                float angle = shape.Angle[i], reach = shape.Reach[i];
                bool wall = shape.Wall[i];
                Array.Copy(shape.Heights, i * RippleShape.MaxSamples, rowBuffer, 0, RippleShape.MaxSamples);
                int j = i - 1;
                while (j >= 0 && shape.Angle[j] > angle)
                {
                    shape.Angle[j + 1] = shape.Angle[j];
                    shape.Reach[j + 1] = shape.Reach[j];
                    shape.Wall[j + 1] = shape.Wall[j];
                    Array.Copy(shape.Heights, j * RippleShape.MaxSamples, shape.Heights, (j + 1) * RippleShape.MaxSamples, RippleShape.MaxSamples);
                    j--;
                }
                shape.Angle[j + 1] = angle;
                shape.Reach[j + 1] = reach;
                shape.Wall[j + 1] = wall;
                Array.Copy(rowBuffer, 0, shape.Heights, (j + 1) * RippleShape.MaxSamples, RippleShape.MaxSamples);
            }
            Array.Sort(shape.EdgeAngle, 0, shape.EdgeCount);
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
