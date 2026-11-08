using UnityEngine;

namespace Marco.Network.Round
{
    // Prototype values for the throwable requested 2026-11-04; see docs/메아리_투척장치_구현메모.md.
    // Kept together so these are not mistaken for the ghost role's 5-use / 25-second Knock rules.
    public static class DecoyConfig
    {
        public const float PickupRange = 1.5f;
        public const float ThrowSpeed = 10f, UpwardSpeed = 2f, Gravity = 18f;
        public const float ReleaseHeight = 1.2f, CrouchedReleaseHeight = .6f;
        public const float ProjectileRadius = .07f, Bounce = .35f, PhysicsStep = .02f, MaxFlightSeconds = 5f;
        public const float FlightSyncSeconds = .05f;
        public const float ImpactRadius = 9f, FootstepRadius = 6f;
        public const float ImpactRingRadius = 3f, FootstepRingRadius = 1f;
        public const float StepDistance = 2f, StepInterval = .4f;
        public const int MovingSteps = 5;
        public const float PreviewDotSpacing = .3f, PreviewDotRadius = .035f, PreviewLandingRadius = .12f;
        public const int PreviewMaxDots = 192;
        public const float PickupVisualHeight = 0f; // Duck body bottom rests .01m above its support.
        public static readonly Vector3[] PickupPositions =
        {
            new(43.25f, 1.8f, 61.6f), // h10: equipment shelf inside the pool hall, northeast wall.
            new(.36f, 1.2f, 36.5f), // h01: teaching-pool west shelf with the red crate, viewed from the east.
        };
        public static readonly float[] PickupYaw = { 0f, 270f }; // Face the accessible pool-side approach.

        public static bool IsValidAim(float x, float y, float z)
        {
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) ||
                float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z)) return false;
            float squared = x * x + y * y + z * z;
            return squared >= .9f && squared <= 1.1f;
        }
    }
}
