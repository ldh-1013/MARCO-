using System;
using System.Globalization;

namespace Marco.Prototype
{
    public struct PlayerPoseState
    {
        public int PlayerId;
        public uint Sequence, Attack, Interaction;
        public float X, Y, Z, Yaw, Pitch, Speed;
        public bool Running, InteractionSucceeded;
    }

    // Pose-only protocol: never contains a scene path or applies health/damage commands.
    public static class PlayerPoseProtocol
    {
        private const string Prefix = "MARCO/3 POSE ";
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        public static string Encode(PlayerPoseState p) => Prefix + string.Join(" ", new[] {
            p.PlayerId.ToString(Culture), p.Sequence.ToString(Culture),
            p.X.ToString("R", Culture), p.Y.ToString("R", Culture), p.Z.ToString("R", Culture),
            p.Yaw.ToString("R", Culture), p.Pitch.ToString("R", Culture), p.Speed.ToString("R", Culture),
            p.Running ? "1" : "0", p.Attack.ToString(Culture), p.Interaction.ToString(Culture), p.InteractionSucceeded ? "1" : "0" });

        public static bool TryParse(string line, out PlayerPoseState p)
        {
            p = default;
            if (line == null || line.Length > 512 || !line.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            var v = line.Substring(Prefix.Length).Split(' ');
            if (v.Length != 12 || !int.TryParse(v[0], NumberStyles.None, Culture, out p.PlayerId) || p.PlayerId < 0
                || !uint.TryParse(v[1], NumberStyles.None, Culture, out p.Sequence) || p.Sequence == 0
                || !Finite(v[2], -10000, 10000, out p.X) || !Finite(v[3], -10000, 10000, out p.Y)
                || !Finite(v[4], -10000, 10000, out p.Z) || !Finite(v[5], -360, 360, out p.Yaw)
                || !Finite(v[6], -70, 85, out p.Pitch) || !Finite(v[7], 0, 30, out p.Speed)
                || (v[8] != "0" && v[8] != "1") || !uint.TryParse(v[9], NumberStyles.None, Culture, out p.Attack)
                || !uint.TryParse(v[10], NumberStyles.None, Culture, out p.Interaction)
                || (v[11] != "0" && v[11] != "1")) return false;
            p.Running = v[8] == "1"; p.InteractionSucceeded = v[11] == "1";
            return true;
        }
        private static bool Finite(string text, float min, float max, out float value) =>
            float.TryParse(text, NumberStyles.Float, Culture, out value) && !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max;
    }
}
