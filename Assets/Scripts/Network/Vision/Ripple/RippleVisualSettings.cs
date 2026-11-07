using UnityEngine;

namespace Marco.Network.Vision
{
    // Every ring-presentation number in one place (기획서 §16 · 부록 A). Two kinds of values:
    // "기획서 값" are copied from the design document and cite it; "연출 튜닝값" are not in the
    // design document and are presentation tuning only. Judgement (who hears what, how far) is server-side
    // and never read from here.
    internal static class RippleVisualSettings
    {
        // ── 기획서 값 ───────────────────────────────

        // 부록 A [바닥 링]: Walk 1.5m · 0.5s (owner only), Sprint · swimming 5.0m · 0.4s (everyone).
        public const float WalkRingRadius = 1.5f, WalkRingSeconds = .5f;
        public const float SprintRingRadius = 5f, SprintRingSeconds = .4f;
        // 부록 A [소리 등급]: Talk 1.2s (rubber-duck landing ring, D8), Shout 22m · 2.5s (drowning, exits).
        public const float TalkSeconds = 1.2f;
        public const float ShoutRadius = 22f, ShoutSeconds = 2.5f;
        // 부록 A [본인 링 표시]: own ring on the own screen keeps its radius; alpha 35% (floor 30%), width 50%.
        public static float SelfAlpha = .35f;
        public const float SelfAlphaFloor = .30f;
        public static float SelfWidth = .5f;
        public static float SelfAlphaClamped => Mathf.Max(SelfAlpha, SelfAlphaFloor);
        // §3.2-1: Echo listeners keep at most 8 rings.
        public const int EchoSoundRingCap = 8;
        // 부록 A [바닥 링]: remote Sprint rings — one per player, three in total.
        public const int RemoteFloorRingCap = 3;
        // §16.2 palette (base / colour-blind). Seeker rings use a double edge (§16.4).
        public static readonly Color RunnerColor = Hex(0x35, 0xF0, 0xD0), RunnerColorBlind = Hex(0xF2, 0xE2, 0x05);
        public static readonly Color SeekerColor = Hex(0xFF, 0x3B, 0x4D), SeekerColorBlind = Hex(0x33, 0x57, 0xFF);
        public static readonly Color EnvironmentColor = Hex(0xE8, 0xE8, 0xE8);
        // §16.2 colour-blind mode. No UI yet; off by default.
        public static bool ColorBlindMode;

        public static Color Runner => ColorBlindMode ? RunnerColorBlind : RunnerColor;
        public static Color Seeker => ColorBlindMode ? SeekerColorBlind : SeekerColor;

        // ── 연출 튜닝값 ───────────────────────────────

        // Expanding-ring sound rings alive at once outside Echo (existing renderer value, unchanged).
        public static int SoundRingCap = 16;
        // Self ring = source point within this horizontal distance of the local player (D1).
        public const float SelfDistance = .3f;
        // Server-path ring colour = nearest living, non-Echo player within this distance (D5).
        public const float CharacterColorDistance = 1f;
        // Rings within this distance of a valve are environment rings and never "own" rings (D1 · D5).
        public const float ValveExclusionDistance = .5f;

        // Host-frame safety (호스트 = 서버): visual raycasts per ring and per rendered frame.
        public const int RaysPerRing = 512, RaysPerFrame = 1024;
        // Ray directions and floor samples per direction by radius. Cost ≈ directions × (samples − 1) × 2.
        public static int RayDirections(float radius) => radius <= 2f ? 16 : radius <= 12.5f ? 24 : 20;
        public static int ProfileSamples(float radius) => radius <= 2f ? 5 : radius <= 12.5f ? 9 : 11;
        // Water-surface rings only need one horizontal ray per direction.
        public const int WaterRayDirections = 32;
        // Extra directions inserted where neighbouring reaches differ (doors, corners).
        public const int MaxRefinedRays = 16;
        public static float ReachJump(float radius) => Mathf.Max(.75f, radius * .2f);
        // Rendered smoothness is independent of ray count: one segment per this much circumference.
        public const float SegmentLength = .3f;
        public const int MinSegments = 48, MaxSegments = 512;
        // Floor probe below a source point (voice is emitted at eye height).
        public static readonly float[] FloorProbeDrops = { 0f, .8f, 1.6f, 2.4f };
        // Pool sources up to this height above the surface are drawn on the surface (D4).
        public const float WaterSurfaceReach = 2.2f;
        public const float SurfaceLift = .025f;

        // Expanding ring motion.
        public const float StartRadius = .05f;
        public const float ExpandPower = 2.2f;   // 1 − (1 − t)^p : fast start, slow arrival
        public const float FadeInSeconds = .05f;
        public const float FadePower = 1.25f;    // (1 − t)^p : soft tail, no hard cut at the end
        public const float MaxAlpha = .85f;
        public const float FloorAlpha = .8f;
        public const float ThinAtEnd = .6f;      // core/trail width multiplier at t = 1
        // Owner floor ring fades this long after movement input stops.
        public const float StopFadeSeconds = .12f;

        // Ring cross-section (metres unless noted).
        public const float CoreWidth = .05f, FloorCoreWidth = .045f;
        public const float TrailWidth = .35f, FloorTrailWidth = .12f;
        public const float FrontHalo = .06f;
        public const float Glow = .45f;
        public const float MinPixels = 1.5f;
        public const float DoubleGap = .14f, DoubleInnerBrightness = .6f;
        // A segment stopped by a wall dims to this over StopFadeFraction × radius of further expansion.
        public const float StoppedDim = .55f, StopFadeFraction = .25f;

        static Color Hex(int r, int g, int b) => new(r / 255f, g / 255f, b / 255f, 1f);
    }
}
