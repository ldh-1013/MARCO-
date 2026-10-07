using System;
using Marco.Prototype;

internal static class HunterAnimationTimingTests
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static int Main()
    {
        var playback = new SingleAttackPlayback();
        Check(playback.Sample(0) == 0 && !playback.IsPlaying, "No attack before input");
        playback.Begin(10, 0.45, 0.8);
        Check(playback.Sample(10) == 0 && playback.IsPlaying, "Input starts first punch");
        Check(Math.Abs(playback.Sample(10.225) - 0.4) < 0.00001, "Half motion samples half first punch");
        for (int frame = 0; frame < 45; frame++)
        {
            double sample = playback.Sample(10 + frame * 0.01);
            Check(sample >= 0 && sample < 0.8, "Cannot enter second punch in the 4.84-second source");
        }
        Check(playback.Sample(10.451) == 0 && !playback.IsPlaying, "Return to locomotion after 0.45 seconds");
        Check(playback.Sample(14.84) == 0 && !playback.IsPlaying, "No repeat throughout remaining source");
        playback.Begin(20, 0.45, 0.8);
        Check(playback.Sample(20) == 0 && playback.IsPlaying, "Next accepted attack restarts at zero");
        Check(playback.Sample(25) == 0 && !playback.IsPlaying, "Long frame does not leave attack stuck");
        playback.Begin(30, 0, 0);
        Check(playback.Sample(30.02) == 0 && !playback.IsPlaying, "Invalid durations safely clamped");
        Console.WriteLine("PASS: " + checks + " attack playback assertions");
        return 0;
    }
}
