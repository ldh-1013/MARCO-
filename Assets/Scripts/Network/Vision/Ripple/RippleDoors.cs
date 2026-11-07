using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network.Vision
{
    // Open doorways of the current map (PoolV12Part category Door / ExternalDoor), so a ring can aim rays
    // exactly between the two jambs (기획서 §16.4 [10-26] 열린 문 누출). The parts store the opening centre and
    // width but not the wall direction, so each door is probed once: the axis whose two short rays hit the
    // jambs at half the width is the wall. Read-only; nothing in the map is changed.
    internal static class RippleDoors
    {
        public struct Door
        {
            public Vector3 Center;      // on the floor, middle of the opening
            public Vector3 JambA, JambB;
            public float Width;
        }

        static readonly List<Door> doors = new();
        static readonly List<MonoBehaviour> sources = new();
        static float nextScan = -1;
        public static int ProbeRays { get; private set; }

        public static IReadOnlyList<Door> All
        {
            get
            {
                // Rebuild after a scene change (a source was destroyed) or while the map is still loading.
                bool stale = sources.Count == 0 ? Time.realtimeSinceStartup >= nextScan : sources[0] == null;
                if (stale) Scan();
                return doors;
            }
        }

        static void Scan()
        {
            doors.Clear();
            sources.Clear();
            nextScan = Time.realtimeSinceStartup + 2f;
            foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
            {
                if (component == null || component.GetType().Name != "PoolV12Part") continue;
                var type = component.GetType();
                string category = type.GetField("category")?.GetValue(component) as string;
                if (category != "Door" && category != "ExternalDoor") continue;
                float width = type.GetField("openingWidth")?.GetValue(component) is float w ? w : 0;
                if (width <= .2f || !(type.GetField("designPosition")?.GetValue(component) is Vector3 position)) continue;
                int floor = type.GetField("floor")?.GetValue(component) is int f ? f : 1;
                if (!Probe(position, width, floor, out var door)) continue;
                doors.Add(door);
                sources.Add(component);
            }
        }

        static bool Probe(Vector3 position, float width, int floor, out Door door)
        {
            door = default;
            float storey = floor >= 2 ? RippleVisualSettings.UpperFloorHeight : 0f;
            var center = new Vector3(position.x, storey + RippleVisualSettings.DoorProbeHeight, position.z);
            Vector3 axis;
            if (Jambs(center, Vector3.right, width)) axis = Vector3.right;
            else if (Jambs(center, Vector3.forward, width)) axis = Vector3.forward;
            else return false;
            ProbeRays++;
            float floorY = Physics.Raycast(center, Vector3.down, out var hit, RippleVisualSettings.DoorProbeHeight + .6f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y : storey;
            var middle = new Vector3(position.x, floorY, position.z);
            door = new Door { Center = middle, JambA = middle + axis * (width * .5f), JambB = middle - axis * (width * .5f), Width = width };
            return true;
        }

        static bool Jambs(Vector3 center, Vector3 axis, float width)
        {
            float half = width * .5f;
            ProbeRays += 2;
            return Physics.Raycast(center, axis, out var a, half + .6f, ~0, QueryTriggerInteraction.Ignore) &&
                   Physics.Raycast(center, -axis, out var b, half + .6f, ~0, QueryTriggerInteraction.Ignore) &&
                   Mathf.Abs(a.distance - half) < .35f && Mathf.Abs(b.distance - half) < .35f;
        }
    }
}
