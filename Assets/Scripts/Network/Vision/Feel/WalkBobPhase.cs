using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network.Vision
{
    // V32 연출.md 2.1 — the walking camera bob follows the own footstep phase. The camera rides the animated head
    // (FirstPersonBody) and the walk clip is sampled at CharacterMotion.GaitPhase, the same phase the own footstep SFX
    // reads (FootstepIndex = floor(cycles × 2): SFX at phase 0 and 0.5). The clip's head lows sit elsewhere in that phase
    // (measured 0.26 and 0.84), so only the PERIODIC part of the camera height is moved in phase: the live height plus
    // T(w(φ)) − T(φ), where T is the height learned per phase over whole walking cycles and w is a piecewise-linear phase
    // warp that puts T's two lows on the SFX phases. Amplitude and shape stay the clip's; anything not periodic (look
    // pitch, stairs, crouch drop) passes through untouched and without delay. Sprint, swimming and the Echo are untouched.
    // [GAP 연출 고유 시작값 — 연출.md 16.2]: blend 0.15 s, settle 0.3 s, whole-cycle learning with weight 0.5.
    public static class WalkBobRules
    {
        public const int Bins = 64;
        // Walk ↔ run / start / stop: the correction fades in and out over this (the clip's own run cross-fade is 0.125 s).
        public const float BlendSeconds = .15f;
        // A cycle is learned only after the walk gait has held this long (walk and run clip weights settled: 0.1 / 0.125 s).
        public const float SettleSeconds = .3f;
        // A new whole cycle moves the table this far toward itself.
        public const float LearnWeight = .5f;
        // Learning stops while the view pitches faster than this (the head tilt follows the look).
        public const float MaxPitchDegreesPerSecond = 30f;
        // Coverage: no gap between neighbouring samples of a cycle larger than this (phase units).
        public const float MaxSampleGap = .12f;

        public static float Wrap01(float x) => x - Mathf.Floor(x);

        // Circular Catmull-Rom interpolation of a per-phase table (smooth: a broad, shallow low is not flattened into a
        // plateau, which would leave its frame undefined).
        public static float Sample(float[] table, float phase)
        {
            int n = table.Length;
            float x = Wrap01(phase) * n - .5f;
            int i = Mathf.FloorToInt(x); float f = x - i;
            float p0 = table[((i - 1) % n + n) % n], p1 = table[(i % n + n) % n], p2 = table[((i + 1) % n + n) % n], p3 = table[((i + 2) % n + n) % n];
            return .5f * (2f * p1 + (p2 - p0) * f + (2f * p0 - 5f * p1 + 4f * p2 - p3) * f * f + (3f * p1 - p0 - 3f * p2 + p3) * f * f * f);
        }

        // Phase warp: 0 → lowA, 0.5 → lowB, 1 → lowA + 1, linear in between (lowA < lowB < lowA + 1).
        public static float Warp(float phase, float lowA, float lowB)
        {
            float p = Wrap01(phase);
            return p < .5f ? lowA + (lowB - lowA) * (p / .5f) : lowB + (lowA + 1f - lowB) * ((p - .5f) / .5f);
        }

        // The table's two lows on the interpolated curve: the deepest, and the deepest at least a quarter cycle from it.
        public const int LowSearchSteps = 1024;
        public static void FindLows(float[] table, out float first, out float second)
        {
            int n = LowSearchSteps, a = 0; float best = float.MaxValue;
            for (int i = 0; i < n; i++) { float v = Sample(table, (i + .5f) / n); if (v < best) { best = v; a = i; } }
            int b = a; best = float.MaxValue;
            for (int k = n / 4; k <= 3 * n / 4; k++)
            {
                int i = (a + k) % n; float v = Sample(table, (i + .5f) / n);
                if (v < best) { best = v; b = i; }
            }
            first = (a + .5f) / n; second = (b + .5f) / n;
        }

        // Pair the two lows with the SFX phases 0 and 0.5 (whichever pairing moves them less) → lowA (with 0, in
        // [−0.5, 0.5)) and lowB (with 0.5, in (lowA, lowA + 1)). False when they cannot be ordered (degenerate table).
        public static bool PairLows(float first, float second, out float lowA, out float lowB)
        {
            static float Off(float x, float s) => Wrap01(x - s + .5f) - .5f;
            float keep = Mathf.Abs(Off(first, 0f)) + Mathf.Abs(Off(second, .5f));
            float swap = Mathf.Abs(Off(second, 0f)) + Mathf.Abs(Off(first, .5f));
            lowA = keep <= swap ? Off(first, 0f) : Off(second, 0f);
            lowB = .5f + (keep <= swap ? Off(second, .5f) : Off(first, .5f));
            return lowA < lowB && lowB < lowA + 1f;
        }

        // Linear fade toward the target, time based (the same at 30 / 60 / 144 fps), shown through a smoothstep.
        public static float Fade(float weight, bool on, float deltaTime) =>
            Mathf.MoveTowards(weight, on ? 1f : 0f, Mathf.Max(0f, deltaTime) / BlendSeconds);
        public static float Shown(float weight) { float w = Mathf.Clamp01(weight); return w * w * (3f - 2f * w); }

        // The height correction at this phase: T(w(φ)) − T(φ).
        public static float Correction(float[] table, float phase, float lowA, float lowB) =>
            Sample(table, Warp(phase, lowA, lowB)) - Sample(table, phase);
    }

    // The per-phase height table, learned from whole walking cycles only (a cycle with any invalid frame is dropped).
    public sealed class WalkBobProfile
    {
        private readonly float[] table = new float[WalkBobRules.Bins];
        private readonly float[] fresh = new float[WalkBobRules.Bins];
        private readonly List<Vector2> samples = new(512);
        private int cycle = int.MinValue; private bool cycleValid;
        public bool Ready { get; private set; }
        public float LowA { get; private set; }
        public float LowB { get; private set; }
        public int CyclesLearned { get; private set; }
        public float[] Table => table;

        public void Reset()
        {
            Ready = false; CyclesLearned = 0; samples.Clear(); cycle = int.MinValue; cycleValid = false;
            System.Array.Clear(table, 0, table.Length);
        }

        // One frame: the gait cycle number (FootstepIndex / 2), the phase in it, the live camera height above the body.
        public void Observe(int cycleNumber, float phase, float height, bool valid)
        {
            if (cycleNumber != cycle)
            {
                if (cycleValid && cycleNumber == cycle + 1) Finish();
                samples.Clear(); cycle = cycleNumber; cycleValid = valid;
            }
            if (!valid) { cycleValid = false; return; }
            if (cycleValid) samples.Add(new Vector2(phase, height));
        }

        private void Finish()
        {
            int n = samples.Count;
            if (n < 8) return;
            if (samples[0].x > WalkBobRules.MaxSampleGap || samples[n - 1].x < 1f - WalkBobRules.MaxSampleGap) return;
            for (int i = 1; i < n; i++) if (samples[i].x - samples[i - 1].x > WalkBobRules.MaxSampleGap || samples[i].x < samples[i - 1].x) return;
            // resample to the bins (linear between neighbouring samples, across the cycle seam as well)
            int bins = fresh.Length, j = 0; float mean = 0f;
            for (int b = 0; b < bins; b++)
            {
                float p = (b + .5f) / bins;
                while (j < n && samples[j].x < p) j++;
                Vector2 lo = j == 0 ? new Vector2(samples[n - 1].x - 1f, samples[n - 1].y) : samples[j - 1];
                Vector2 hi = j == n ? new Vector2(samples[0].x + 1f, samples[0].y) : samples[j];
                float f = hi.x - lo.x > 1e-6f ? (p - lo.x) / (hi.x - lo.x) : 0f;
                fresh[b] = lo.y + (hi.y - lo.y) * f; mean += fresh[b];
            }
            mean /= bins;
            for (int b = 0; b < bins; b++)
            {
                float v = fresh[b] - mean;
                table[b] = Ready ? table[b] + (v - table[b]) * WalkBobRules.LearnWeight : v;
            }
            WalkBobRules.FindLows(table, out float first, out float second);
            if (!WalkBobRules.PairLows(first, second, out float a, out float b2)) return;
            LowA = a; LowB = b2; Ready = true; CyclesLearned++;
        }

        public float Correction(float phase) => Ready ? WalkBobRules.Correction(table, phase, LowA, LowB) : 0f;
    }
}
