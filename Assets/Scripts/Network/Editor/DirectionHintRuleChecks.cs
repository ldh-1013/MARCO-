using System;
using System.Collections.Generic;
using Marco.Network.Vision;
using UnityEditor;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Unit checks of the pure Phase C rules (DirectionHintRules). Batch, no scene, no play mode. The Marco.Network
    // assembly has no EditMode test assembly and adding one would add an asmdef, so these run as a batch check.
    public static class DirectionHintRuleChecks
    {
        static readonly List<string> failures = new();
        static int passed;

        public static void Run()
        {
            if (!Application.isBatchMode) throw new Exception("Batch only");
            int code = 0;
            try
            {
                Octants();
                Relative();
                Kinds();
                States();
                Fades();
                Sources();
                Bands();
                Debug.Log($"[DirectionHintRuleChecks] RESULT {passed} passed, {failures.Count} failed");
                foreach (var failure in failures) Debug.Log("[DirectionHintRuleChecks] RESULT FAIL " + failure);
                if (failures.Count > 0) throw new Exception("FAIL: " + string.Join(" | ", failures));
                Debug.Log("[DirectionHintRuleChecks] PASS");
            }
            catch (Exception ex) { Debug.LogException(ex); code = 1; }
            EditorApplication.Exit(code);
        }

        static void Check(bool condition, string name)
        {
            if (condition) passed++;
            else failures.Add(name);
        }

        static void Octants()
        {
            var o = Vector3.zero;
            Check(DirectionHintRules.Octant(o, new Vector3(0, 0, 5)) == 0, "octant +Z = 0");
            Check(DirectionHintRules.Octant(o, new Vector3(5, 0, 5)) == 1, "octant +X+Z = 1");
            Check(DirectionHintRules.Octant(o, new Vector3(5, 0, 0)) == 2, "octant +X = 2");
            Check(DirectionHintRules.Octant(o, new Vector3(0, 0, -5)) == 4, "octant -Z = 4");
            Check(DirectionHintRules.Octant(o, new Vector3(-5, 0, 0)) == 6, "octant -X = 6");
            Check(DirectionHintRules.Octant(o, new Vector3(-5, 0, 5)) == 7, "octant -X+Z = 7");
            Check(DirectionHintRules.Octant(o, new Vector3(0, 3, 0)) == -1, "same spot (height only) = -1");
            Check(DirectionHintRules.Octant(o, new Vector3(1, 0, 2.5f)) == 0 && DirectionHintRules.Octant(o, new Vector3(1, 0, 2.3f)) == 1, "22.5° boundary");
            // The server's hidden point is the listener + 2 m along a 45° snap: the hint must read back the same octant.
            var random = new System.Random(3);
            int mismatches = 0;
            for (int i = 0; i < 720; i++)
            {
                var listener = new Vector3((float)random.NextDouble() * 60, 0, (float)random.NextDouble() * 60);
                var source = new Vector3((float)random.NextDouble() * 60, 0, (float)random.NextDouble() * 60);
                var direction = source - listener;
                float angle = Mathf.Round(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg / 45f) * 45f;
                var hidden = listener + Quaternion.Euler(0, angle, 0) * Vector3.forward * 2f;
                if (DirectionHintRules.Octant(listener, hidden) != DirectionHintRules.Octant(listener, source)) mismatches++;
            }
            Check(mismatches == 0, $"server snap read back ({mismatches} mismatches of 720)");
        }

        static void Relative()
        {
            Check(Mathf.Approximately(DirectionHintRules.RelativeDegrees(2, 0), 90), "east seen facing north = right");
            Check(Mathf.Approximately(DirectionHintRules.RelativeDegrees(0, 90), -90), "north seen facing east = left");
            Check(Mathf.Approximately(Mathf.Abs(DirectionHintRules.RelativeDegrees(4, 0)), 180), "south seen facing north = behind");
            Check(Mathf.Approximately(DirectionHintRules.RelativeDegrees(0, 350), 10), "wrap around 360");
        }

        static void Kinds()
        {
            Check(DirectionHintRules.RingKind("") == DirectionHintRules.KindForced, "ring-only pulse (NoAudio)");
            Check(DirectionHintRules.RingKind("Valve") == DirectionHintRules.KindValve, "Valve");
            Check(DirectionHintRules.RingKind("ValveUnderwater") == DirectionHintRules.KindValveUnderwater, "ValveUnderwater");
            Check(DirectionHintRules.RingKind("Reflow") == DirectionHintRules.KindReflow, "Reflow");
            Check(DirectionHintRules.RingKind("Bandage") == DirectionHintRules.KindBandage && DirectionHintRules.RingKind("Treat") == DirectionHintRules.KindTreat, "Bandage / Treat");
            Check(DirectionHintRules.RingKind("Hurt") == DirectionHintRules.KindDrowning, "Hurt (drowning)");
            Check(DirectionHintRules.RingKind("Moan_1") == DirectionHintRules.KindMoan, "Moan_*");
            foreach (var clip in new[] { "Breath_Runner", "Breath_Seeker", "Concrete_1", "SurfaceSwim", "WaterEntry", "Tile_2", null })
                Check(DirectionHintRules.RingKind(clip) == 0, "not ring display: " + (clip ?? "null"));
        }

        static void States()
        {
            Check(DirectionHintRules.CanShow(true, false, true), "alive in a running round");
            Check(!DirectionHintRules.CanShow(false, false, true), "dead / spectating / escaped");
            Check(!DirectionHintRules.CanShow(true, true, true), "Echo");
            Check(!DirectionHintRules.CanShow(true, false, false), "lobby / result");
        }

        static void Fades()
        {
            float max = DirectionHintRules.MaxAlpha;
            Check(DirectionHintRules.Alpha(-1, 5, 1) == 0, "never lit");
            Check(Mathf.Approximately(DirectionHintRules.Alpha(0, 1, 0), 0), "starts at 0");
            Check(Mathf.Approximately(DirectionHintRules.Alpha(0, 1, .04f), max * .5f), "half way through the 0.08 s fade in");
            Check(Mathf.Approximately(DirectionHintRules.Alpha(0, 1, .5f), max), "held at 0.35");
            Check(Mathf.Approximately(DirectionHintRules.Alpha(0, 1, 1.15f), max * .5f), "half way through the 0.3 s fade out");
            Check(DirectionHintRules.Alpha(0, 1, 1.31f) == 0, "gone after the fade out");
            float start = -1, hold = -1;
            DirectionHintRules.Light(ref start, ref hold, 10, 2);
            Check(start == 10 && hold == 12, "light from off");
            DirectionHintRules.Light(ref start, ref hold, 10.5f, .6f);
            Check(start == 10 && hold == 12, "overlap keeps the longer hold, no restart, no adding");
            DirectionHintRules.Light(ref start, ref hold, 12.15f, .6f);
            Check(Mathf.Abs(DirectionHintRules.Alpha(start, hold, 12.15f) - max * .5f) < 1e-4f && Mathf.Approximately(hold, 12.75f), "relight while fading continues from the current brightness");
        }

        static void Sources()
        {
            var state = new DirectionHintRules.SourceState();
            Check(DirectionHintRules.Decide(state, 2, 0, .5f, false) == DirectionHintRules.Show, "first sound shows");
            Check(DirectionHintRules.Decide(state, 2, .2f, .5f, false) == DirectionHintRules.Ignore, "same source within 0.5 s ignored");
            Check(DirectionHintRules.Decide(state, 2, .5f, .5f, false) == DirectionHintRules.Show, "after 0.5 s shows again");
            var voice = new DirectionHintRules.SourceState();
            Check(DirectionHintRules.Decide(voice, 3, 0, .5f, true) == DirectionHintRules.Show, "first packet shows");
            Check(DirectionHintRules.Decide(voice, 3, .02f, .5f, true) == DirectionHintRules.Extend, "next packet extends");
            Check(DirectionHintRules.Decide(voice, 4, .04f, .5f, true) == DirectionHintRules.Ignore, "packet from another octant within the cooldown ignored");
            var loop = new DirectionHintRules.SourceState();
            Check(DirectionHintRules.Decide(loop, 1, 0, 2.5f, false) == DirectionHintRules.Show && DirectionHintRules.Decide(loop, 1, 2.4f, 2.5f, false) == DirectionHintRules.Ignore &&
                  DirectionHintRules.Decide(loop, 1, 2.5f, 2.5f, false) == DirectionHintRules.Show, "continuing sound every 2.5 s");
            Check(!DirectionHintRules.LoopHidden(10, 12.5f) && DirectionHintRules.LoopHidden(10, 12.7f), "underwater valve ring gap 2.6 s");
        }

        static void Bands()
        {
            const float w = 1920, h = 1080;
            float depth = DirectionHintRules.BandFraction * h;
            DirectionHintRules.Band(0, w, h, out var c, out float rotation, out var size);
            Check(Mathf.Abs(c.x) < .01f && Mathf.Abs(c.y - (h / 2 - depth / 2)) < .01f && rotation == 0 && Mathf.Abs(size.x - h * Mathf.Tan(22.5f * Mathf.Deg2Rad)) < .5f && Mathf.Abs(size.y - depth) < .01f, "ahead: top edge, centred, ±22.5° long");
            DirectionHintRules.Band(90, w, h, out c, out rotation, out size);
            Check(Mathf.Abs(c.x - (w / 2 - depth / 2)) < .01f && Mathf.Abs(c.y) < .01f && rotation == -90, "right: right edge");
            DirectionHintRules.Band(180, w, h, out c, out rotation, out _);
            Check(c.y < 0 && rotation == 180, "behind: bottom edge");
            DirectionHintRules.Band(-90, w, h, out c, out rotation, out _);
            Check(c.x < 0 && rotation == 90, "left: left edge");
            float corner = Mathf.Atan2(w / 2, h / 2) * Mathf.Rad2Deg;
            DirectionHintRules.Band(corner - 5, w, h, out c, out _, out size);
            Check(Mathf.Abs(c.x) <= w / 2 && Mathf.Abs(c.y) <= h / 2 && size.x >= depth, "near a corner stays on screen");
            for (int deg = -180; deg <= 180; deg += 15)
            {
                DirectionHintRules.Band(deg, w, h, out c, out _, out size);
                Check(Mathf.Abs(c.x) <= w / 2 + .01f && Mathf.Abs(c.y) <= h / 2 + .01f, $"band {deg}° centre on screen");
            }
        }
    }
}
