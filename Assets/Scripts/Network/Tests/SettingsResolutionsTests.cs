using System.Collections.Generic;
using Marco.Network.Settings;
using NUnit.Framework;

namespace Marco.Network.Tests
{
    // [11-23 V50] the settings window's resolution list (SettingsResolutions): one entry per width × height at its highest
    // refresh rate, smallest first, the monitor's native size and 1920 × 1080 (when the monitor is that large) always in it.
    public sealed class SettingsResolutionsTests
    {
        static ResolutionChoice R(int w, int h, double hz) => new(w, h, hz);

        [Test]
        public void Choices_OnePerSize_HighestRefresh_SmallestFirst()
        {
            var list = SettingsResolutions.Choices(new[] { R(1920, 1080, 60), R(1280, 720, 60), R(1920, 1080, 239.76), R(1280, 720, 144), R(1600, 900, 60) }, 1920, 1080);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual((1280, 720), (list[0].Width, list[0].Height)); Assert.AreEqual(144, list[0].Hz, 1e-9);
            Assert.AreEqual((1600, 900), (list[1].Width, list[1].Height));
            Assert.AreEqual((1920, 1080), (list[2].Width, list[2].Height)); Assert.AreEqual(239.76, list[2].Hz, 1e-9);
        }

        [Test]
        public void Choices_AddTheNativeSizeAndFullHd_WhenTheMonitorListLacksThem()
        {
            // The case reported in the manual test: a list without 1920 × 1080 on a 1920 × 1080 monitor.
            var list = SettingsResolutions.Choices(new[] { R(1280, 720, 60), R(1024, 768, 60) }, 1920, 1080);
            Assert.GreaterOrEqual(SettingsResolutions.IndexOf(list, 1920, 1080), 0);
            Assert.AreEqual(1, CountOf(list, 1920, 1080));
            // A larger monitor keeps its native size and still offers 1920 × 1080 (a full-screen window scales it).
            var big = SettingsResolutions.Choices(new[] { R(1280, 720, 60) }, 2560, 1440);
            Assert.GreaterOrEqual(SettingsResolutions.IndexOf(big, 2560, 1440), 0);
            Assert.GreaterOrEqual(SettingsResolutions.IndexOf(big, 1920, 1080), 0);
            // A smaller monitor is not offered a size it cannot show.
            var small = SettingsResolutions.Choices(new[] { R(1280, 720, 60) }, 1366, 768);
            Assert.Less(SettingsResolutions.IndexOf(small, 1920, 1080), 0);
            Assert.GreaterOrEqual(SettingsResolutions.IndexOf(small, 1366, 768), 0);
        }

        [Test]
        public void Choices_IgnoreEmptyModes_AndANullList()
        {
            Assert.AreEqual(1, SettingsResolutions.Choices(new[] { R(0, 0, 60), R(-1, 720, 60) }, 1280, 720).Count);
            Assert.AreEqual(1, SettingsResolutions.Choices(null, 1280, 720).Count);
            Assert.AreEqual(0, SettingsResolutions.Choices(null, 0, 0).Count);
        }

        [Test]
        public void SizeFor_FullScreenWithoutASavedSize_IsTheNativeSize_NotTheWindow()
        {
            Assert.AreEqual((1920, 1080), SettingsResolutions.SizeFor(true, -1, -1, 1920, 1080, 960, 520));
            Assert.AreEqual((1600, 900), SettingsResolutions.SizeFor(true, 1600, 900, 1920, 1080, 960, 520), "a saved size wins");
            Assert.AreEqual((960, 520), SettingsResolutions.SizeFor(false, -1, -1, 1920, 1080, 960, 520), "a window keeps its size");
            Assert.AreEqual((1280, 720), SettingsResolutions.SizeFor(false, 1280, 720, 1920, 1080, 960, 520));
        }

        static int CountOf(List<ResolutionChoice> list, int w, int h) { int n = 0; foreach (var c in list) if (c.Width == w && c.Height == h) n++; return n; }
    }
}
