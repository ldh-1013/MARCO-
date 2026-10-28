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
        private readonly List<(Bounds Area, float Radius)> deepWater = new();
        // 0 dry, 1 body partly above water, 2 fully submerged. Pool water level is shared by
        // shallow/deep volumes; the circular sump's top is NOT the water surface.
        public int WaterState(NetworkPlayer player, out float surface)
        {
            surface = 0;
            if (pools.Count == 0)
                foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                {
                    if (component == null || component.GetType().Name != "PoolV12Part") continue;
                    var type = component.GetType();
                    string category = type.GetField("category")?.GetValue(component) as string;
                    string id = type.GetField("designId")?.GetValue(component) as string ?? "";
                    if (category == "WaterVolume" && (id.Contains("Deep") || id.Contains("CircularSump")))
                    {
                        var area = new Bounds((Vector3)type.GetField("designPosition").GetValue(component),
                            (Vector3)type.GetField("designSize").GetValue(component));
                        deepWater.Add((area, (float)type.GetField("radius").GetValue(component)));
                    }
                    if (category != "Pool") continue;
                    var center = (Vector3)type.GetField("designPosition").GetValue(component);
                    var size = (Vector3)type.GetField("designSize").GetValue(component);
                    pools.Add(new Bounds(center, size));
                }
            var controller = player.GetComponent<CharacterController>();
            if (controller == null || !controller.enabled) return 0;
            var body = controller.bounds;
            foreach (var pool in pools)
            {
                if (body.center.x <= pool.min.x || body.center.x >= pool.max.x ||
                    body.center.z <= pool.min.z || body.center.z >= pool.max.z) continue;
                surface = pool.center.y;
                if (body.min.y >= surface - .05f) return 0;
                if (player.Role == PlayerRole.Runner || player.Role == PlayerRole.Seeker)
                {
                    foreach (var deep in deepWater)
                    {
                        var center = deep.Area.center;
                        bool inside = body.center.x > deep.Area.min.x && body.center.x < deep.Area.max.x &&
                            body.center.z > deep.Area.min.z && body.center.z < deep.Area.max.z;
                        if (inside && (deep.Radius <= 0 || new Vector2(body.center.x-center.x, body.center.z-center.z).sqrMagnitude < deep.Radius*deep.Radius))
                            return 2;
                    }
                    float valveRange = NetworkRound.Instance != null ? NetworkRound.Instance.InteractRange : 2f;
                    foreach (var valve in ValveView.All)
                    {
                        if (valve.Id != ValveId.B && valve.Id != ValveId.E) continue;
                        var delta = body.center - valve.transform.position;
                        if (new Vector2(delta.x, delta.z).sqrMagnitude <= valveRange * valveRange) return 2;
                    }
                    return 1; // Shallow-water Runner cue is independent of model height/crouch.
                }
                return body.max.y > surface + .05f ? 1 : 2;
            }
            return 0;
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

        public bool Hear(Vector3 origin, NetworkPlayer listener, float radius,
            out Vector3 playbackPoint, out float gain, out bool muffled)
        {
            playbackPoint = origin;
            gain = 0;
            muffled = false;
            Vector3 end = listener.transform.position;
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
            radius *= Mathf.Pow(0.5f, walls.Count);
            if (distance > radius) return false;
            muffled = walls.Count > 0;
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
