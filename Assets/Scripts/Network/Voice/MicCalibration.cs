using System;
using System.Collections.Generic;
using Marco.Network.Round;

namespace Marco.Network.Voice
{
    // 기획서 5.2 ③ 캘리브레이션 · 부록 A [마이크 캘리브레이션] (11-15) — pure rules, no scene, no MonoBehaviour.
    // Measure turns one 3 s prompt (150 frames of 20 ms dBFS) into a personal offset; Grade applies it to the
    // −30 / −15 grade boundaries only. With offset 0, Grade is exactly the fixed-threshold rule it replaced.
    public static class MicCalibration
    {
        // 부록 A [마이크 캘리브레이션] "측정 3초 = 20ms 프레임 150개" (5.2 ① · ③).
        public const int FrameMilliseconds = 20;
        public const float DurationSeconds = 3f;
        public const int FrameCount = 150;
        // 부록 A "기준값 −40 dBFS" — [GAP] 구현 시작값(속삭임 구간 −50~−30의 중앙에서 유도, 플레이테스트로 조정).
        public const float TargetDbfs = -40f;
        // 부록 A "오프셋 = 기준값 − baseline, ±6dB 클램프" (12.6 — 11-13 "보정 오프셋 상한 ±6dB").
        public const float OffsetLimitDb = 6f;
        // 부록 A "유성 프레임 = 소음 바닥 + 8dB 이상" — 게이트 마진(VoiceActivityGate.MarginDb)과 값은 같지만 독립 조정 — [GAP] 구현 시작값.
        public const float ActiveMarginDb = 8f;
        // 부록 A "소음 바닥 = 측정 프레임 dBFS의 하위 10% 값" — [GAP] 구현 시작값.
        public const int FloorPercentile = 10;
        // 부록 A "유성 프레임 25개(0.5초) 미만 → 무효" — [GAP] 구현 시작값.
        public const int MinActiveFrames = 25;
        // 부록 A "클리핑 프레임 5개 이상 → 무효" — [GAP] 구현 시작값. 클리핑 판정 기준은 연동 단계에서 정한다.
        public const int ClipFrameLimit = 5;
        // 부록 A "실패: 최대 3회 재시도, 계속 실패하면 오프셋 0" — [GAP] 구현 시작값.
        public const int MaxAttempts = 3;

        public enum FailReason { None, Clipped, TooFewActive }

        public readonly struct Result
        {
            public readonly bool Valid;
            public readonly float Baseline;   // dBFS; NaN when invalid
            public readonly float Offset;     // dB, already clamped; 0 when invalid
            public readonly float NoiseFloor; // dBFS; NaN when there were no frames
            public readonly int ActiveFrames;
            public readonly FailReason Reason;

            public Result(bool valid, float baseline, float offset, float noiseFloor, int activeFrames, FailReason reason)
            {
                Valid = valid; Baseline = baseline; Offset = offset; NoiseFloor = noiseFloor;
                ActiveFrames = activeFrames; Reason = reason;
            }
        }

        // One prompt: per-frame RMS dBFS and the number of clipped frames among them.
        public static Result Measure(IReadOnlyList<float> frameDbfs, int clippedFrames)
        {
            var frames = new List<float>(frameDbfs?.Count ?? 0);
            if (frameDbfs != null)
                foreach (float value in frameDbfs)
                    if (!float.IsNaN(value) && !float.IsInfinity(value)) frames.Add(value);
            frames.Sort();
            float floor = frames.Count > 0 ? frames[PercentileIndex(frames.Count, FloorPercentile)] : float.NaN;
            if (clippedFrames >= ClipFrameLimit) return Invalid(floor, 0, FailReason.Clipped);

            var active = new List<float>(frames.Count);
            if (frames.Count > 0)
                foreach (float value in frames)
                    if (value >= floor + ActiveMarginDb) active.Add(value);   // frames is sorted, so active is too
            if (active.Count < MinActiveFrames) return Invalid(floor, active.Count, FailReason.TooFewActive);

            float baseline = Median(active);
            return new Result(true, baseline, ClampOffset(TargetDbfs - baseline), floor, active.Count, FailReason.None);
        }

        public static float ClampOffset(float offset) => Math.Max(-OffsetLimitDb, Math.Min(OffsetLimitDb, offset));

        // 0 silence, 1 Whisper, 2 Talk, 3 Shout (MicrophoneVisionController's level). The offset moves only the
        // −30 / −15 boundaries; whether there is a voice at all is still silenceDb on the measured dBFS. The order of
        // the tests is the old fixed-threshold rule's, so offset 0 returns exactly what it returned.
        public static int Grade(float dbfs, float offset, float silenceDb)
        {
            float graded = dbfs + offset;
            return graded >= PulseSpec.ShoutDbfs ? 3 : graded >= PulseSpec.TalkDbfs ? 2 : dbfs >= silenceDb ? 1 : 0;
        }

        // Nearest-rank percentile on a sorted list: the smallest value with at least p% of the frames at or below it.
        internal static int PercentileIndex(int count, int percentile) =>
            Math.Max(0, Math.Min(count - 1, (int)Math.Ceiling(count * percentile / 100.0) - 1));

        private static float Median(List<float> sorted)
        {
            int middle = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) * .5f;
        }

        private static Result Invalid(float floor, int activeFrames, FailReason reason) =>
            new(false, float.NaN, 0f, floor, activeFrames, reason);
    }
}
