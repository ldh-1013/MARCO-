using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network.Settings
{
    // [11-23 V50] The resolution list of the settings window: the monitor's own modes (Screen.resolutions), one entry per
    // width × height with its highest refresh rate, smallest first. The monitor's native size is always in it, and 1920 × 1080
    // is added when the monitor is at least that large (a full-screen window scales it). Pure: the checks feed it plain sizes.
    public readonly struct ResolutionChoice
    {
        public readonly int Width, Height;
        public readonly double Hz;
        public ResolutionChoice(int width, int height, double hz) { Width = width; Height = height; Hz = hz; }
        public string Text => Width + " × " + Height;
    }

    public static class SettingsResolutions
    {
        public const int FullHdWidth = 1920, FullHdHeight = 1080;

        public static List<ResolutionChoice> Choices(IEnumerable<ResolutionChoice> modes, int nativeWidth, int nativeHeight)
        {
            var list = new List<ResolutionChoice>();
            if (modes != null)
                foreach (var mode in modes)
                {
                    if (mode.Width <= 0 || mode.Height <= 0) continue;
                    int at = IndexOf(list, mode.Width, mode.Height);
                    if (at < 0) list.Add(mode);
                    else if (mode.Hz > list[at].Hz) list[at] = mode;
                }
            if (nativeWidth > 0 && nativeHeight > 0 && IndexOf(list, nativeWidth, nativeHeight) < 0) list.Add(new ResolutionChoice(nativeWidth, nativeHeight, 0));
            if (nativeWidth >= FullHdWidth && nativeHeight >= FullHdHeight && IndexOf(list, FullHdWidth, FullHdHeight) < 0)
                list.Add(new ResolutionChoice(FullHdWidth, FullHdHeight, 0));
            list.Sort((a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.Height.CompareTo(b.Height));
            return list;
        }

        public static int IndexOf(List<ResolutionChoice> list, int width, int height)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Width == width && list[i].Height == height) return i;
            return -1;
        }

        // The size a display mode is applied at: the saved one, else — full screen — the monitor's native size, else the window's.
        public static (int width, int height) SizeFor(bool fullScreen, int savedWidth, int savedHeight, int nativeWidth, int nativeHeight,
            int windowWidth, int windowHeight)
        {
            if (savedWidth > 0 && savedHeight > 0) return (savedWidth, savedHeight);
            if (fullScreen && nativeWidth > 0 && nativeHeight > 0) return (nativeWidth, nativeHeight);
            return (windowWidth, windowHeight);
        }

        // The live monitor: its modes and native size (Display.main.system*, which a window size does not change).
        public static List<ResolutionChoice> Current()
        {
            var modes = new List<ResolutionChoice>();
            foreach (var r in Screen.resolutions) modes.Add(new ResolutionChoice(r.width, r.height, r.refreshRateRatio.value));
            Native(out int w, out int h);
            return Choices(modes, w, h);
        }

        public static void Native(out int width, out int height)
        {
            width = Display.main != null ? Display.main.systemWidth : 0;
            height = Display.main != null ? Display.main.systemHeight : 0;
            if (width <= 0 || height <= 0) { width = Screen.currentResolution.width; height = Screen.currentResolution.height; }
        }

        // The highest refresh rate the monitor offers at this size (none: the current one).
        public static RefreshRate RefreshFor(int width, int height)
        {
            RefreshRate best = Screen.currentResolution.refreshRateRatio; bool found = false;
            foreach (var r in Screen.resolutions)
                if (r.width == width && r.height == height && (!found || r.refreshRateRatio.value > best.value)) { best = r.refreshRateRatio; found = true; }
            return best;
        }
    }
}
