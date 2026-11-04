using UnityEngine;
using Marco.Core.WinCondition;

namespace Marco.Network.Vision
{
    // Distance cadence is independent of noisy speed grades and animation/audio timing.
    public sealed class MovementPulseClock
    {
        public static bool IsFastMovement(PlayerRole role, float speed, bool surfaceWater) =>
            surfaceWater || speed > 5f;

        private Vector3 previous;
        private bool initialized, pendingFast;
        private float stepDistance, ringDistance, gradeTime, stationaryTime;
        public bool Moving { get; private set; }
        public bool Fast { get; private set; }
        public bool Advance(Vector3 position, float deltaTime, bool eligible, bool fast, out bool ring)
        {
            ring = false;
            Vector3 offset = position - previous;
            previous = position;
            float distance = new Vector2(offset.x, offset.z).magnitude;
            bool valid = initialized && eligible && deltaTime > 0 &&
                offset.magnitude <= Mathf.Max(.5f, 15f * deltaTime);
            if (!initialized) Fast = pendingFast = fast;
            initialized = true;
            Moving = valid && distance > .00001f;
            if (!valid)
            {
                stepDistance = ringDistance = stationaryTime = gradeTime = 0;
                return false;
            }
            if (!Moving)
            {
                stationaryTime += deltaTime;
                // A repeated network sample is not a new stop. Real stops still reset cadence.
                if (stationaryTime >= .15f) stepDistance = ringDistance = 0;
                return false;
            }
            stationaryTime = 0;
            if (fast == Fast) gradeTime = 0;
            else
            {
                if (pendingFast != fast) { pendingFast = fast; gradeTime = 0; }
                gradeTime += deltaTime;
                if (gradeTime >= .15f) { Fast = fast; gradeTime = 0; stepDistance = ringDistance = 0; }
            }
            stepDistance += distance;
            ringDistance += distance;
            float interval = Fast ? 6f : 2f;
            bool step = stepDistance + .0001f >= interval;
            if (step) stepDistance = Mathf.Max(0, stepDistance - interval);
            if (ringDistance + .0001f >= 6f) { ringDistance = Mathf.Max(0, ringDistance - 6f); ring = true; }
            return step;
        }
    }
}
