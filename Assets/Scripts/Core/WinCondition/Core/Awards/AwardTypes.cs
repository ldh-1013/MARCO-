using System;

namespace Marco.Core.WinCondition
{
    // The three awards in show order (기획서 8 · 12.5 — cards revealed left to right).
    public enum AwardKind : byte { MostScreams, SilentSurvivor, BestLure }

    // Sounds the server reports to the award tally (기획서 5.1 SoundType, grouped by how 8.1 · 8.2 treat them).
    // Jump / landing has no SoundType and is only detected on clients: not reported (TODO in AwardRules).
    public enum AwardNoise : byte
    {
        Footstep,   // Walk · Sprint · surface swim, radius after the floor multiplier (5.9)
        Voice,      // Whisper · Talk · Shout
        Scream,     // 3.5 — the only sound 8.1 counts
        Breath,     // 3.6 — 3 m → 9 m
        Bandage,
        Treat,      // the healer's
        Drowning,   // breath ran out: Shout-grade forced pulse (5.9-1)
        Moan,       // excluded from 8.2
        Valve,      // excluded from 8.2
        Item,       // duck · recorder: not reported (decided 2026-11-10); excluded if ever reported
    }

    // Where a Runner's round ended, for the award rules. Undecided at round end = 미탈출 (still a survivor).
    public enum AwardPlayerState : byte { Undecided, Escaped, Tagged }

    // A position without UnityEngine (Core has no engine reference).
    public readonly struct AwardPoint
    {
        public readonly float X, Y, Z;
        public AwardPoint(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static float Distance(AwardPoint a, AwardPoint b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    public static class AwardRules
    {
        // 8.2 무성 생존상: everything a Runner makes counts except valve sounds and Moan (and item lures,
        // which are not the Runner's own body sound). Breath · Bandage · Treat count (GAP-96, 10-03).
        // TODO: 점프·착지 소음 미집계 — 서버 이벤트 생기면 추가 (지금은 클라이언트만 감지, 5.1에 SoundType 없음).
        public static bool CountsForSilence(AwardNoise kind) =>
            kind != AwardNoise.Moan && kind != AwardNoise.Valve && kind != AwardNoise.Item;

        // 8.2: under 1 m of movement the Runner is not ranked (0-division guard).
        public const float MinimumDistanceMeters = 1f;

        // 3.6 / 5.1 Breath: 3 m on the first breath of a still spell, +2 m every 15 s, at most 9 m.
        // The server breathes at 20 s of stillness and then every 15 s (NetworkRound.Audio TickBreath).
        public const float BreathStartSeconds = 20f, BreathStepSeconds = 15f;
        public const float BreathBaseRadius = 3f, BreathRadiusStep = 2f, BreathMaxRadius = 9f;

        public static float BreathRadiusAt(float stillSeconds)
        {
            if (stillSeconds < BreathStartSeconds) return BreathBaseRadius;
            int index = (int)Math.Floor((stillSeconds - BreathStartSeconds) / BreathStepSeconds + 1e-4);
            return Math.Min(BreathMaxRadius, BreathBaseRadius + BreathRadiusStep * index);
        }

        public static string Title(AwardKind kind) => kind switch
        {
            AwardKind.MostScreams => "최다 비명상",
            AwardKind.SilentSurvivor => "무성 생존상",
            _ => "최고의 낚시상",
        };

        // The reason line on the card (12.5), in words. value: screams, silence score, or lures.
        // 낚시상 keeps the 8.3 wording as is ("술래를 N번 낚음").
        public static string Evidence(AwardKind kind, float value) => kind switch
        {
            AwardKind.MostScreams => $"비명 {(int)Math.Round(value)}번",
            AwardKind.SilentSurvivor => "가장 조용히 움직임",
            _ => $"술래를 {(int)Math.Round(value)}번 낚음",
        };

        // The small number under the words, only where the words leave it out (무성: the 8.2 score).
        public static string EvidenceDetail(AwardKind kind, float value) =>
            kind == AwardKind.SilentSurvivor ? $"무성 점수 {value:0.00}" : null;

        public const string NoWinner = "수상자 없음";
    }
}
