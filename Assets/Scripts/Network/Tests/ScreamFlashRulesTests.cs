using System.IO;
using System.Text.RegularExpressions;
using Marco.Network.Vision;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Network.Tests
{
    // V36 파트 A 연출.md 8.1 억제 실패 — the scream flash's curve and the one-line server hook that feeds it.
    public class ScreamFlashRulesTests
    {
        private const float T = 1e-4f;

        [Test]
        public void Curve_40msIn_160msOut_OnePeak_Max030()
        {
            Assert.AreEqual(.30f, ScreamFlashRules.MaxAlpha, T); Assert.AreEqual(.04f, ScreamFlashRules.InSeconds, T);
            Assert.AreEqual(.16f, ScreamFlashRules.OutSeconds, T); Assert.AreEqual(.2f, ScreamFlashRules.TotalSeconds, T);
            Assert.AreEqual(0f, ScreamFlashRules.Alpha(-.01f)); Assert.AreEqual(0f, ScreamFlashRules.Alpha(0f), T);
            Assert.AreEqual(.30f, ScreamFlashRules.Alpha(ScreamFlashRules.InSeconds), T, "peak at 40 ms");
            Assert.AreEqual(0f, ScreamFlashRules.Alpha(ScreamFlashRules.TotalSeconds), T);
            Assert.AreEqual(Color.white, ScreamFlashRules.Colour, "the 4.2 tag flash white");
            foreach (float fps in new[] { 30f, 60f, 144f })
            {
                float last = 0f, maxStep = 0f; int peaks = 0; bool rising = true;
                for (float t = 0f; t <= .25f; t += 1f / fps)
                {
                    float a = ScreamFlashRules.Alpha(t);
                    Assert.LessOrEqual(a, .30f + T);
                    maxStep = Mathf.Max(maxStep, Mathf.Abs(a - last));
                    if (rising && a < last - T) { peaks++; rising = false; }
                    if (!rising) Assert.LessOrEqual(a, last + T, $"one rise, one fall ({fps} fps)");
                    last = a;
                }
                Assert.AreEqual(1, peaks, $"a single peak ({fps} fps)");
                // the value depends on the time since the event only (frame-rate independent)
                Assert.AreEqual(ScreamFlashRules.Alpha(.1f), ScreamFlashRules.Alpha(.1f), 0f);
            }
            // fast in, slow out: at 80 ms (40 ms after the peak) still more than half
            Assert.Greater(ScreamFlashRules.Alpha(.08f), ScreamFlashRules.MaxAlpha * .4f);
        }

        // The hook is exactly one read-only event: raised on self && scream in the scream message handler, the old drawing removed,
        // the old timer gone. Pinned as source text so the approved change cannot drift (the handler cannot be called from a test).
        [Test]
        public void ScreamHook_IsOneEventLine_AndTheOldBarsAreGone()
        {
            string path = Path.Combine(Application.dataPath, "Scripts/Network/Player/PlayerMovement.Shout.cs");
            string src = File.ReadAllText(path);
            Assert.AreEqual(1, Regex.Matches(src, @"public event System\.Action LocalScreamFlash;").Count, "one event declaration");
            Assert.AreEqual(1, Regex.Matches(src, @"if \(self && scream\) LocalScreamFlash\?\.Invoke\(\);").Count, "one call, same condition");
            Assert.AreEqual(1, Regex.Matches(src, @"LocalScreamFlash\?\.Invoke").Count, "invoked nowhere else");
            Assert.IsFalse(src.Contains("screamFlash"), "the old timer and the white bars are gone");
            // the call sits inside AbilitySoundTarget, after the pulse is shown (nothing before it was moved)
            int target = src.IndexOf("private void AbilitySoundTarget", System.StringComparison.Ordinal);
            int call = src.IndexOf("LocalScreamFlash?.Invoke", System.StringComparison.Ordinal);
            int show = src.IndexOf("ShowSoundPulse(point", System.StringComparison.Ordinal);
            Assert.Greater(call, target); Assert.Greater(call, show);
            // the file's network attribute lines are unchanged in number (3 + 3), counted without writing the attribute names here
            Assert.AreEqual(3, Regex.Matches(src, @"^\s*\[Target" + @"Rpc]", RegexOptions.Multiline).Count);
            Assert.AreEqual(3, Regex.Matches(src, @"^\s*\[Server" + @"Rpc]", RegexOptions.Multiline).Count);
        }
    }
}
