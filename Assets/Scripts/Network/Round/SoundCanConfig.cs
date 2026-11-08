using Marco.Network.Voice;
using UnityEngine;

namespace Marco.Network.Round
{
    public enum SoundCanStage : byte { None, Empty, Recording, Recorded, Placed, Playing, Placing }

    // §7: 3-second recording, placement, remote replay, Talk pulse, memory only.
    // 2026-11-05 user placement: one pickup on the photographed spectator seating steps.
    // Other values are prototype decisions documented in docs/소리통_구현메모.md.
    public static class SoundCanConfig
    {
        public const float RecordingSeconds = 3f, RecordingTimeout = 6f;
        public const int Frames = 150, Bytes = Frames * VoiceCodec.SamplesPerPacket;
        public const float FrameSeconds = .02f, PickupRange = 1.5f, PlacementForward = .8f;
        public const float PulseRadius = 9f, PulseSeconds = 1.2f;
        public const float PlaybackGain = 4f;
        public const float PlacementSeconds = 2f, PlacementMoveTolerance = .25f, PlacementLookDown = 55f;
        public static readonly Vector3[] PickupPositions = { new(27.5f, 3.9f, 32.2f) }; // o70, lowest tier.
    }
}
