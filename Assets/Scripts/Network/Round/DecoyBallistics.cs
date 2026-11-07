using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Round
{
    // Prediction and server flight use exactly the same fixed step and collision sweep.
    public static class DecoyBallistics
    {
        // Both callers run on Unity's main thread. Reuse the sweep buffer during per-frame prediction.
        private static readonly RaycastHit[] sweepHits = new RaycastHit[32];
        public static Vector3 Origin(Vector3 playerPosition, bool crouched) => playerPosition +
            Vector3.up * (crouched ? DecoyConfig.CrouchedReleaseHeight : DecoyConfig.ReleaseHeight);

        public static Vector3 Velocity(Vector3 aim) =>
            aim.normalized * DecoyConfig.ThrowSpeed + Vector3.up * DecoyConfig.UpwardSpeed;

        public static bool Step(ref Vector3 position, ref Vector3 velocity, out RaycastHit hit)
        {
            velocity += Vector3.down * (DecoyConfig.Gravity * DecoyConfig.PhysicsStep);
            Vector3 delta = velocity * DecoyConfig.PhysicsStep;
            if (!Cast(position, delta, out hit)) { position += delta; return false; }
            position = hit.point + hit.normal * (DecoyConfig.ProjectileRadius + .005f);
            if (hit.normal.y >= .5f && velocity.y <= 0) return true;
            velocity = Vector3.Reflect(velocity, hit.normal) * DecoyConfig.Bounce;
            return false;
        }

        public static bool Cast(Vector3 origin, Vector3 delta, out RaycastHit closest)
        {
            closest = default;
            float length = delta.magnitude;
            if (length < .00001f) return false;
            float best = float.MaxValue;
            int hitCount = Physics.SphereCastNonAlloc(origin, DecoyConfig.ProjectileRadius, delta / length,
                sweepHits, length, ~0, QueryTriggerInteraction.Ignore);
            var hits = sweepHits;
            if (hitCount == sweepHits.Length)
            {
                hits = Physics.SphereCastAll(origin, DecoyConfig.ProjectileRadius, delta / length,
                    length, ~0, QueryTriggerInteraction.Ignore);
                hitCount = hits.Length;
            }
            for (int i = 0; i < hitCount; i++)
            {
                var hit = hits[i];
                if (hit.collider.GetComponentInParent<NetworkPlayer>() != null || hit.distance >= best) continue;
                closest = hit; best = hit.distance;
            }
            return best < float.MaxValue;
        }

        // Even spatial spacing makes the arc a dotted line, regardless of projectile speed.
        // The last point is the landing centre; surfaces remain occluded by the renderer's depth test.
        public static int Predict(Vector3 origin, Vector3 aim, Vector3[] points, out bool landed)
        {
            landed = false;
            if (points == null || points.Length < 2 || !DecoyConfig.IsValidAim(aim.x, aim.y, aim.z)) return 0;
            Vector3 position = origin, velocity = Velocity(aim);
            int count = 1;
            points[0] = origin;
            float sinceDot = 0;
            int steps = Mathf.RoundToInt(DecoyConfig.MaxFlightSeconds / DecoyConfig.PhysicsStep);
            for (int i = 0; i < steps; i++)
            {
                Vector3 previous = position;
                landed = Step(ref position, ref velocity, out _);
                Vector3 segment = position - previous;
                float distance = segment.magnitude;
                float next = DecoyConfig.PreviewDotSpacing - sinceDot;
                while (next <= distance && count < points.Length - 1)
                {
                    points[count++] = previous + segment * (next / distance);
                    next += DecoyConfig.PreviewDotSpacing;
                }
                sinceDot = distance - (next - DecoyConfig.PreviewDotSpacing);
                if (landed || count >= points.Length - 1)
                {
                    points[count++] = position;
                    return count;
                }
            }
            if (count < points.Length) points[count++] = position;
            return count;
        }
    }
}
