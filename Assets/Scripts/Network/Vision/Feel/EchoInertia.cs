using UnityEngine;

namespace Marco.Network.Vision
{
    // 연출 2.2 메아리 비행 "bob 없음. 관성 있는 가감속(0.3초)" [11-22 V43]: the camera follows the real flight position through a
    // critically damped follower, so a start ramps up and a stop glides out over about 0.3 s. Presentation only — the flight
    // itself (PlayerMovement, 6.0 m/s, position sync, listening and knock position) is untouched; the real position is never
    // changed, only where this machine's own camera is drawn. Rotation is never lagged (looking stays immediate).
    //
    // The follower is the exact solution of x'' = ω²(target − x) − 2ω x' (ω = 2 / SmoothSeconds, the model of Vector3.SmoothDamp) over each frame with the target
    // moving linearly between the last frame's position and this one's, so 30 / 60 / 144 fps give the same curve.
    //   SmoothSeconds 0.10: steady lag = 2 × speed / ω = speed × 0.1 s = 0.6 m at 6.0 m/s.
    //   Start from rest: the drawn speed is v (1 − (1 + ωt) e^(−ωt)) — 59 % of the flight speed at 0.1 s, 91 % at 0.2 s, 98.3 % at 0.3 s,
    //   rising monotonically (no overshoot at all, so well inside the ≤ 15 % limit).
    //   Stop: the lag is (L + v t) e^(−ωt) — never negative (the camera does not pass the real position), under 1 cm after 0.3 s.
    //   MaxLagMeters 0.75 is a hard cap (a hitch or a speed change cannot stretch the lag).
    // The lag is the whole effect, so it sits outside the 2 cm feel cap (that cap is for the shakes; the walking bob fix is outside it
    // too). [GAP 연출 고유 시작값 — 연출.md 16.2]
    public sealed class EchoInertia
    {
        public const float SmoothSeconds = .1f, MaxLagMeters = .75f, ResetDistanceMeters = 2.5f;

        private Vector3 position, velocity, previousTarget;
        private bool ready;

        // The camera-side lag (real − drawn) of the last Step, and the drawn velocity — for the checks and the capture CSV.
        public Vector3 Lag { get; private set; }
        public Vector3 Velocity => velocity;

        // [11-25 V65] True from a vertical push by the per-position range until the drawn camera is back inside that range
        // (the camera calls EndPush then): the follower may trail the push farther than the hard cap and the camera's vertical
        // clamp waits for it, so the camera glides down (or up) with the usual inertia instead of being cut.
        public bool PushActive { get; private set; }
        public void EndPush() { PushActive = false; }

        public void Reset() { ready = false; velocity = Vector3.zero; Lag = Vector3.zero; PushActive = false; }

        // [11-24 V51 B] 기획서 3.2 · 맵설계 ⑫ "건물 밖으로 나가지 않는다": the flight position is already held in the building box
        // (EchoBodyRules.ClampFlight, the same box); the drawn camera is held in it too, `inset` inside every face, so neither the
        // eye above the body, the follower's trail nor the near clip plane reaches into an outer wall or above the ceiling.
        // `inset` is the camera's own near-plane corner distance (no new value). A NaN position is returned as it was.
        // [11-25 V65] Vertically the same measured per-position range as the flight (EchoBoundsGrid at the drawn eye's x, z),
        // `inset` inside it; a range thinner than two insets drops the inset (the eye stays inside the range itself).
        // `holdVertical` false (a push is being followed, PushActive) leaves Y to the follower for those few frames.
        public static Vector3 ClampInside(Vector3 eye, float inset, bool holdVertical = true)
        {
            if (float.IsNaN(eye.x) || float.IsNaN(eye.y) || float.IsNaN(eye.z)) return eye;
            inset = Mathf.Max(0f, inset);
            float x = Mathf.Clamp(eye.x, Marco.Network.Player.EchoBodyRules.FlightMinX + inset, Marco.Network.Player.EchoBodyRules.FlightMaxX - inset);
            float z = Mathf.Clamp(eye.z, Marco.Network.Player.EchoBodyRules.FlightMinZ + inset, Marco.Network.Player.EchoBodyRules.FlightMaxZ - inset);
            float lo = Marco.Network.Player.EchoBodyRules.FlightMinY + inset, hi = Marco.Network.Player.EchoBodyRules.FlightMaxY - inset;
            if (Marco.Network.Player.EchoBoundsGrid.TryGetVerticalRange(x, z, out float floor, out float top))
            {
                lo = floor + inset; hi = top - inset;
                if (hi < lo) { lo = floor; hi = top; }
            }
            return new Vector3(x, holdVertical ? Mathf.Clamp(eye.y, lo, hi) : eye.y, z);
        }

        // [11-25 V65] A frame's vertical jump that the per-position range made (flying from a high column into a lower one pushes
        // the body down under that column's slab, or up onto a floor): the horizontal step is an ordinary one (not a teleport), the
        // jump is longer than the follower's own hard cap (MaxLagMeters — no flight frame moves that far vertically) and the last
        // real eye height lies outside the new column's range on the side the jump came from. Such a jump is followed through the
        // inertia, not cut. (The eye reaches a column border before the body does, so the border itself is not the signal.)
        public static bool IsBoundsPush(Vector3 from, Vector3 to)
        {
            float dx = to.x - from.x, dz = to.z - from.z, dy = to.y - from.y;
            if (dx * dx + dz * dz > ResetDistanceMeters * ResetDistanceMeters) return false;
            if (!Marco.Network.Player.EchoBoundsGrid.TryGetVerticalRange(to.x, to.z, out float floor, out float top)) return false;
            return (dy < -MaxLagMeters && from.y > top) || (dy > MaxLagMeters && from.y < floor);
        }

        // The distance from a camera to the corners of its near plane: near × √(1 + tan²(vfov / 2) + tan²(hfov / 2)).
        public static float NearCornerDistance(float near, float verticalFov, float aspect)
        {
            float tv = Mathf.Tan(Mathf.Clamp(verticalFov, 1f, 179f) * .5f * Mathf.Deg2Rad), th = tv * Mathf.Max(.01f, aspect);
            return Mathf.Max(0f, near) * Mathf.Sqrt(1f + tv * tv + th * th);
        }

        // `target` is the real eye position this frame. Returns the position to draw (= target when not active).
        public Vector3 Step(Vector3 target, float deltaTime, bool active)
        {
            if (!active || !(deltaTime > 0f) || float.IsNaN(target.x + target.y + target.z)) { Reset(); return target; }
            // [11-25 V65] a vertical push by the per-position range is followed through the same inertia (not cut like a teleport):
            // the push is a step of the target, so only the horizontal part counts as this frame's target movement.
            bool push = ready && IsBoundsPush(previousTarget, target);
            if (push) { previousTarget.y = target.y; PushActive = true; }
            Vector3 away = ready ? target - position : Vector3.zero;
            if (PushActive) away.y = 0f;   // while a push is followed its vertical trail is not a teleport
            if (!ready || away.sqrMagnitude > ResetDistanceMeters * ResetDistanceMeters)
            {
                position = target; previousTarget = target; velocity = Vector3.zero; ready = true; Lag = Vector3.zero; PushActive = false;
                return target;
            }
            float omega = 2f / Mathf.Max(1e-4f, SmoothSeconds * Mathf.Max(0f, FeelRules.FeelIntensity));
            // e = drawn − real: e'' = −ω² e − 2ω (e' + u) for a target moving at u, so e = −2u/ω + (A + B t) e^(−ωt)
            Vector3 targetVelocity = (target - previousTarget) / deltaTime;
            Vector3 settled = -2f * targetVelocity / omega;
            Vector3 e0 = position - previousTarget, de0 = velocity - targetVelocity;
            Vector3 a = e0 - settled, b = de0 + omega * a;
            float decay = Mathf.Exp(-omega * deltaTime);
            position = target + settled + (a + b * deltaTime) * decay;
            velocity = targetVelocity + (b - omega * (a + b * deltaTime)) * decay;
            previousTarget = target;
            Vector3 lag = target - position;
            if (!PushActive && lag.sqrMagnitude > MaxLagMeters * MaxLagMeters)
            {
                position = target - lag.normalized * MaxLagMeters;
                lag = target - position;
            }
            Lag = lag;
            return position;
        }
    }
}
