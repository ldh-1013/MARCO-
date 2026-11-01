using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Valves;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Audio
{
    // Read existing map metadata without a reference from Marco.Network to Assembly-CSharp.
    internal sealed class SoundWorld
    {
        private readonly Dictionary<Collider, Part> parts = new();
        private readonly HashSet<MonoBehaviour> walls = new();
        private sealed class Part
        {
            public string Category, Zone;
            public MonoBehaviour Component;
            public bool Occluder;
        }

        private Part Metadata(Collider collider)
        {
            if (parts.TryGetValue(collider, out var cached)) return cached;
            foreach (var component in collider.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().Name != "PoolV12Part") continue;
                var type = component.GetType();
                var part = new Part
                {
                    Category = type.GetField("category")?.GetValue(component) as string,
                    Zone = type.GetField("zone")?.GetValue(component) as string,
                    Occluder = type.GetField("soundOccluder")?.GetValue(component) is bool flag && flag,
                    Component = component
                };
                parts[collider] = part;
                return part;
            }
            parts[collider] = null;
            return null;
        }

        private readonly List<Bounds> pools = new();
        private bool poolsLoaded;
        public bool TryPool(Vector3 position, out Bounds pool)
        {
            if (!poolsLoaded)
            {
                poolsLoaded = true;
                foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                {
                    if (component == null || component.GetType().Name != "PoolV12Part") continue;
                    var type = component.GetType();
                    if (type.GetField("category")?.GetValue(component) as string != "Pool") continue;
                    pools.Add(new Bounds((Vector3)type.GetField("designPosition").GetValue(component),
                        (Vector3)type.GetField("designSize").GetValue(component)));
                }
            }
            foreach (var area in pools)
                if (position.x > area.min.x && position.x < area.max.x &&
                    position.z > area.min.z && position.z < area.max.z)
                { pool = area; return true; }
            pool = default;
            return false;
        }

        // The surface comes from map geometry; diving is determined by the eye height,
        // never by proximity to a valve or a deep-water volume.
        public int WaterState(NetworkPlayer player, out float surface)
        {
            surface = 0f;
            if (player == null || player.IsEcho || !TryPool(player.transform.position, out var pool)) return 0;
            var controller = player.GetComponent<CharacterController>();
            if (controller == null || !controller.enabled) return 0;
            surface = pool.center.y;
            if (controller.bounds.min.y >= surface - .05f) return 0;
            var movement = player.GetComponent<PlayerMovement>();
            float eyeY = movement != null ? movement.WaterEyeHeight : controller.bounds.max.y;
            return eyeY < surface - .03f ? 2 : 1;
        }

        public bool TryFloor(NetworkPlayer player, out Collider floor) => TryFloorNear(player, out floor, .25f);

        public bool TryFloorNear(NetworkPlayer player, out Collider floor, float tolerance)
        {
            floor = null;
            var controller = player.GetComponent<CharacterController>();
            if (controller == null || !controller.enabled) return false;
            float distance = controller.height * 0.5f + tolerance;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(player.transform.TransformPoint(controller.center),
                Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<NetworkPlayer>() != null || hit.normal.y < 0.5f) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                floor = hit.collider;
            }
            return floor != null;
        }

        public string Surface(Collider floor, Vector3 position)
        {
            // Zone assignments from the existing PoolV12 layout; mixed rooms split at their design Z.
            var part = Metadata(floor);
            if (part != null)
            {
                if (part.Category == "Stair") return "Concrete";
                if (part.Category == "Obstacle" && part.Zone == "ST") return "Tile";
                if (part.Category != "Floor") return "";
                switch (part.Zone)
                {
                    case "H": case "MS": case "WS": return "Tile";
                    case "AID": return "Tile";
                    case "SZ": return position.z >= 18f ? "Concrete" : "Tile";
                    case "A": case "DR": case "LD": case "C": return "Concrete";
                    case "SC": case "ST2": case "L2": return "Concrete";
                    case "LOB": case "OF1": case "ML": case "WL": case "CL": case "ST": return "Tile";
                    default: return "";
                }
            }
            var renderer = floor.GetComponent<Renderer>();
            string name = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "";
            if (name == "Tile") return "Tile";
            if (name == "Concrete") return "Concrete";
            return "";
        }

        public int WaterSurfaceCrossings(Vector3 from, Vector3 to)
        {
            TryPool(from, out _); // Load map metadata once.
            int count = 0;
            foreach (var pool in pools)
            {
                float surface = pool.center.y;
                if ((from.y < surface) == (to.y < surface) || Mathf.Abs(to.y - from.y) < .001f) continue;
                Vector3 crossing = Vector3.Lerp(from, to, (surface - from.y) / (to.y - from.y));
                if (crossing.x > pool.min.x && crossing.x < pool.max.x && crossing.z > pool.min.z && crossing.z < pool.max.z) count++;
            }
            return count;
        }

        public bool Hear(Vector3 origin, NetworkPlayer listener, float radius,
            out Vector3 playbackPoint, out float gain, out bool muffled)
        {
            playbackPoint = origin;
            gain = 0;
            muffled = false;
            Vector3 end = listener.transform.position;
            if (listener.TryGetComponent<PlayerMovement>(out var movement)) end.y = movement.WaterEyeHeight;
            float distance = Vector3.Distance(origin, end);
            if (listener.Role == PlayerRole.Seeker) radius *= 1.2f;
            if (distance > radius) return false;
            walls.Clear();
            if (distance > 0.05f)
            {
                foreach (var hit in Physics.RaycastAll(origin, (end - origin) / distance, distance,
                    ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider.GetComponentInParent<NetworkPlayer>() != null) continue;
                    var part = Metadata(hit.collider);
                    if (part != null && (part.Occluder || part.Category == "Wall" ||
                        part.Category == "Floor" || part.Category == "Ceiling")) walls.Add(part.Component);
                }
            }
            int waterCrossings = WaterSurfaceCrossings(origin, end);
            radius *= Mathf.Pow(0.5f, walls.Count + waterCrossings);
            if (distance > radius) return false;
            muffled = walls.Count + waterCrossings > 0;
            if (muffled)
            {
                // Do not send exact position/distance through an occluding wall.
                Vector3 direction = origin - end;
                float angle = Mathf.Round(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg / 45f) * 45f;
                playbackPoint = end + Quaternion.Euler(0, angle, 0) * Vector3.forward * 2f;
                gain = 0.5f;
            }
            else gain = Mathf.Clamp01(1f - distance / Mathf.Max(radius, 0.01f));
            return true;
        }
    }
}
