using UnityEngine;
using Marco.Core.WinCondition;

namespace Marco.Network.Vision
{
    // Distance cadence is independent of noisy speed grades and animation/audio timing.
    public sealed class MovementPulseClock
    {
        // [11-22 V42b] 부록 A [발소리 등급]: the grade boundary is per role and the speed is the 0.3 s average
        // (GradeSpeedWindow). One speed rule (> 5.0) sat on the Runner's own walking speed (5.00), so tick and frame
        // noise flipped the grade on about half the samples. [GAP — 연출.md 16.2 · 개정이력 2026-11-22]
        //   Runner 5.5: walk 5.0 (0.3 s average max 5.14) < 5.5 < sprint 6.375 (recovering) · 7.5 (0.3 s average min 6.0).
        //   Seeker 4.3: sneak 3.5 (0.3 s average max 4.15) < 4.3 < chase 5.05 .. 5.67 (4 players: 0.3 s average p1 5.17).
        // Surface swimming stays the sprint grade whatever the speed.
        public const float RunnerFastSpeed = 5.5f, SeekerFastSpeed = 4.3f;

        public static bool IsFastMovement(PlayerRole role, float speed, bool surfaceWater) =>
            surfaceWater || speed > (role == PlayerRole.Seeker ? SeekerFastSpeed : RunnerFastSpeed);

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

    // [11-22 V42b] The grade input: horizontal distance over the newest samples that cover 0.3 s (3 movement ticks on the
    // authority, the frames of the last 0.3 s on the owner). A jump larger than the clock's own validity limit (a teleport,
    // a respawn) empties the window.
    public sealed class GradeSpeedWindow
    {
        public const float Seconds = .3f;
        private const int Capacity = 256;
        private readonly float[] distances = new float[Capacity], times = new float[Capacity];
        private int head, count;

        public void Reset() => head = count = 0;

        public float Push(float distance, float deltaTime)
        {
            if (!(deltaTime > 0f)) return Average();
            if (distance > Mathf.Max(.5f, 15f * deltaTime)) { Reset(); return 0f; }
            if (count == Capacity) { head = (head + 1) % Capacity; count--; }
            int slot = (head + count) % Capacity;
            distances[slot] = distance; times[slot] = deltaTime; count++;
            // drop the oldest while the rest still covers the window
            while (count > 1 && Total(times) - times[head] >= Seconds - .0001f) { head = (head + 1) % Capacity; count--; }
            return Average();
        }

        private float Average()
        {
            float time = Total(times);
            return time > 0f ? Total(distances) / time : 0f;
        }

        private float Total(float[] values)
        {
            float sum = 0f;
            for (int i = 0; i < count; i++) sum += values[(head + i) % Capacity];
            return sum;
        }
    }
}
