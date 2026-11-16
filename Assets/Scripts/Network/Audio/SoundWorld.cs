using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Map;
using Marco.Network.Player;
using Marco.Network.Valves;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Audio
{
    // Read existing map metadata without a reference from Marco.Network to Assembly-CSharp.
    internal sealed class SoundWorld
    {
        private readonly Dictionary<Collider, MapPart> parts = new();
        private readonly HashSet<MonoBehaviour> walls = new();
        private readonly Dictionary<MonoBehaviour, (float enter, float exit, bool slab)> spanParts = new();
        private readonly List<OccluderSpan> spans = new();
        private readonly RaycastHit[] backHits = new RaycastHit[64];
        // The map part on a collider (Marco.Network.Map.MapParts — shared with clicker sight), cached per collider.
        private MapPart Metadata(Collider collider)
        {
            if (parts.TryGetValue(collider, out var cached)) return cached;
            var part = MapParts.Read(collider);
            parts[collider] = part;
            return part;
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
            var movement = player.GetComponent<PlayerMovement>();
            if (movement != null) return movement.WaterStateAtSurface(surface);
            if (controller.bounds.min.y >= surface - .05f) return 0;
            return controller.bounds.max.y < surface - .03f ? 2 : 1;
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
            if (floor != null) return true;
            // A capsule rests on the uphill side of its rounded foot. A centre ray can miss
            // that support on ramps even while the controller is standing on the staircase.
            float radius = Mathf.Max(.01f, controller.radius * Mathf.Abs(player.transform.lossyScale.x) - .01f);
            float castDistance = Mathf.Max(0, controller.bounds.extents.y - radius) + tolerance;
            foreach (var hit in Physics.SphereCastAll(player.transform.TransformPoint(controller.center), radius,
                Vector3.down, castDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<NetworkPlayer>() != null || hit.normal.y < .5f) continue;
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
                if (part.Category == "Stair") return "MetalStairs";
                if (part.Category == "Obstacle" && part.Zone == "ST") return "Carpet";
                if (part.Category == "Obstacle" && part.DesignId is "h03" or "h05" or "h07" or "h11" or "l03" or "l04" or "l05" or "l06") return "WoodBench";
                if (part.Category != "Floor") return "";
                switch (part.Zone)
                {
                    case "H": case "MS": case "WS": return "Tile";
                    case "AID": return position.z >= 24f ? "Carpet" : "Tile";
                    case "SZ": return position.z >= 18f ? "Concrete" : "Carpet";
                    case "A": case "DR": case "LD": case "C": return "Concrete";
                    case "SC": case "ST2": case "L2": return "Metal";
                    case "ML": case "WL": return "Mat";
                    case "LOB": case "OF1": case "CL": case "ST": return "Carpet";
                    default: return "";
                }
            }
            // The imported wooden bench instances have colliders but no PoolV12Part.
            for (var root = floor.transform; root != null; root = root.parent)
                if (root.name == "긴나무의자" || root.name.StartsWith("긴나무의자 (", System.StringComparison.Ordinal))
                    return "WoodBench";

            var renderer = floor.GetComponent<Renderer>();
            string name = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "";
            if (name == "Tile") return "Tile";
            if (name == "Concrete") return "Concrete";
            return "";
        }

        public float SurfaceMultiplier(Collider floor, Vector3 position) => Surface(floor, position) switch
        {
            "Metal" or "MetalStairs" => 1.5f,
            "Tile" => 1.3f,
            "Carpet" or "Mat" => .7f,
            _ => 1f
        };

        public static int FootstepVariants(string surface) => surface switch
        {
            "Tile" => 3,
            "Concrete" => 4,
            "Carpet" or "Mat" or "Metal" or "MetalStairs" or "WoodBench" => 1,
            _ => 0
        };

        private readonly RaycastHit[] visualHits = new RaycastHit[32];
        public int VisualQueryCount { get; private set; }
        private RaycastHit[] VisualHits(Vector3 origin, Vector3 direction, float distance, out int count)
        {
            VisualQueryCount++;
            count = Physics.RaycastNonAlloc(origin, direction, visualHits, distance, ~0, QueryTriggerInteraction.Ignore);
            if (count < visualHits.Length) return visualHits;
            // Never silently drop a wall when a crowded ray fills the reusable buffer.
            VisualQueryCount++;
            var overflow = Physics.RaycastAll(origin, direction, distance, ~0, QueryTriggerInteraction.Ignore);
            count = overflow.Length;
            return overflow;
        }

        // Follow the same walkable floor across ramps, without jumping to a different storey.
        public bool VisualFloor(Vector3 near, out Vector3 point)
        {
            point = near;
            float closest = float.MaxValue;
            bool found = false;
            var hits = VisualHits(near + Vector3.up * .6f, Vector3.down, 1.2f, out int count);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider.GetComponentInParent<NetworkPlayer>() != null || hit.normal.y < .5f) continue;
                var part = Metadata(hit.collider);
                if (part == null || !(part.Category == "Floor" || part.Category == "Stair" ||
                    part.Category == "Obstacle" && part.Zone == "ST")) continue;
                float difference = Mathf.Abs(hit.point.y - near.y);
                if (difference >= closest) continue;
                closest = difference; point = hit.point + Vector3.up * .025f; found = true;
            }
            return found;
        }

        public float VisualReach(Vector3 origin, Vector3 direction, float radius)
        {
            float reach = radius;
            var hits = VisualHits(origin, direction, radius, out int count);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider.GetComponentInParent<NetworkPlayer>() != null) continue;
                var part = Metadata(hit.collider);
                if (part != null && part.IsSoundStructure)
                    reach = Mathf.Min(reach, hit.distance);
            }
            return reach;
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
            out Vector3 playbackPoint, out float gain, out bool muffled) =>
            HearResolved(origin, listener, radius, out playbackPoint, out gain, out muffled, out _);

        public bool HearResolved(Vector3 origin, NetworkPlayer listener, float radius,
            out Vector3 playbackPoint, out float gain, out bool muffled, out float perceivedRadius) =>
            HearResolved(origin, listener, radius, out playbackPoint, out gain, out muffled, out perceivedRadius, out _);

        // durationScale: §5.6 perceivedDuration factor for this listener (PulseSpec.OccludedDurationScale) — 1 when unoccluded.
        public bool HearResolved(Vector3 origin, NetworkPlayer listener, float radius,
            out Vector3 playbackPoint, out float gain, out bool muffled, out float perceivedRadius, out float durationScale)
        {
            perceivedRadius = radius;
            playbackPoint = origin;
            durationScale = 1f;
            if (listener.HasEchoBody)
            {
                gain = 1;
                muffled = false;
                return true;
            }
            gain = 0;
            muffled = false;
            Vector3 end = listener.transform.position;
            if (listener.TryGetComponent<PlayerMovement>(out var listenerMovement)) end.y = listenerMovement.WaterEyeHeight;
            float distance = Vector3.Distance(origin, end);
            if (listener.Role == PlayerRole.Seeker) radius *= Marco.Network.Round.PulseSpec.SeekerHearing;
            if (distance >= radius) return false;
            int wallCount = CountOccluders(origin, end, distance);
            int waterCrossings = WaterSurfaceCrossings(origin, end);
            radius *= Mathf.Pow(PulseSpec.WallOcclusion, wallCount) * Mathf.Pow(PulseSpec.WaterOcclusion, waterCrossings);
            if (distance >= radius) return false;
            perceivedRadius = radius;
            durationScale = PulseSpec.OccludedDurationScale(wallCount, waterCrossings, false);
            muffled = wallCount + waterCrossings > 0;
            if (muffled)
            {
                // Do not send exact position/distance through an occluding wall.
                Vector3 direction = origin - end;
                float angle = Mathf.Round(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg / 45f) * 45f;
                playbackPoint = end + Quaternion.Euler(0, angle, 0) * Vector3.forward * 2f;
                gain = PulseSpec.OccludedVolume;
            }
            else gain = Mathf.Clamp01(1f - distance / Mathf.Max(radius, 0.01f));
            return true;
        }

        // 7절 · 부록 A [아이템] 섬광 대상 [11-16]: no sound-occlusion structure between the two points — the same count as
        // sound (touching parts merged, an end on a surface not counted); furniture and props never block.
        public bool HasClearSight(Vector3 from, Vector3 to) => CountOccluders(from, to, Vector3.Distance(from, to)) == 0;

        // 부록 A [사운드 차폐] 차폐 셈 단위 [11-16]: each occluder part (차폐 파트 or Wall / Floor / Ceiling) gets one span on the
        // line — enter = nearest forward hit, exit = length - nearest backward hit (an end inside the part: 0 or the length) —
        // and OccluderSpans merges touching / overlapping ones (gap <= PulseSpec.OccluderMergeGap).
        private int CountOccluders(Vector3 origin, Vector3 end, float distance)
        {
            walls.Clear();
            spanParts.Clear();
            spans.Clear();
            if (distance <= 0.05f) return 0;
            Vector3 direction = (end - origin) / distance;
            CollectSpanHits(origin, direction, distance, visualHits, true);
            CollectSpanHits(end, -direction, distance, backHits, false);
            foreach (var pair in spanParts)
            {
                bool fromInside = float.IsPositiveInfinity(pair.Value.enter), toInside = float.IsNegativeInfinity(pair.Value.exit);
                float enter = fromInside ? 0f : pair.Value.enter;
                float exit = toInside ? distance : pair.Value.exit;
                // An end lying on the part's surface (e.g. a drain hatch on the pool floor) is hit from one side only, over a
                // few centimetres: that is not "inside the part" and the part is not between the two ends.
                if (fromInside && exit <= EndOnSurface || toInside && enter >= distance - EndOnSurface) continue;
                walls.Add(pair.Key);
                spans.Add(new OccluderSpan(enter, Mathf.Max(enter, exit), pair.Value.slab));
            }
            return OccluderSpans.Count(spans, PulseSpec.OccluderMergeGap);
        }

        // An end within this distance of a part's surface counts as on the surface (the same 5 cm as the minimum line length).
        private const float EndOnSurface = .05f;

        private void CollectSpanHits(Vector3 from, Vector3 direction, float distance, RaycastHit[] buffer, bool forward)
        {
            int hits = Physics.RaycastNonAlloc(from, direction, buffer, distance, ~0, QueryTriggerInteraction.Ignore);
            if (hits == buffer.Length)
            {
                buffer = Physics.RaycastAll(from, direction, distance, ~0, QueryTriggerInteraction.Ignore);
                hits = buffer.Length;
            }
            for (int i = 0; i < hits; i++)
            {
                var hit = buffer[i];
                if (hit.collider.GetComponentInParent<NetworkPlayer>() != null) continue;
                var part = Metadata(hit.collider);
                if (part == null || !part.IsSoundStructure) continue;
                if (!spanParts.TryGetValue(part.Component, out var span))
                    span = (float.PositiveInfinity, float.NegativeInfinity, part.IsSlab);
                if (forward) span.enter = Mathf.Min(span.enter, hit.distance);
                else span.exit = Mathf.Max(span.exit, distance - hit.distance);
                spanParts[part.Component] = span;
            }
        }
    }
}
