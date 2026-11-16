using Marco.Network.Map;
using UnityEngine;

namespace Marco.Network.Round
{
    // 부록 A [태그 판정] 벽 차단 [11-16]: only map parts marked tagBlocker (walls, storey slabs, locker rows, pool side walls)
    // stop a tag. Colliders without a map part (furniture, props, pillars) and parts with tagBlocker = false do not.
    public static class TagBlockRules
    {
        public static bool Blocks(bool hasPart, bool tagBlocker) => hasPart && tagBlocker;

        // The server/client tag segment test. Hits on the seeker's or the target's own body are ignored.
        public static bool PathBlocked(Vector3 origin, Vector3 end, Transform seeker, Transform target)
        {
            Vector3 delta = end - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f) return false;
            foreach (var hit in Physics.RaycastAll(origin, delta / distance, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (seeker != null && hit.transform.IsChildOf(seeker)) continue;
                if (target != null && hit.transform.IsChildOf(target)) continue;
                var part = MapParts.Read(hit.collider);
                if (Blocks(part != null, part != null && part.TagBlocker)) return true;
            }
            return false;
        }
    }
}
