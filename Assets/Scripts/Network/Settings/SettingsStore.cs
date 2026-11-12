using System;
using UnityEngine;

namespace Marco.Network.Settings
{
    // 기획서 12.6 설정 화면 — the values one player keeps on this machine (PlayerPrefs, "marco.settings."). Local only:
    // nothing here reaches the server or another player.
    public sealed class SettingsValues
    {
        // FOV is the horizontal angle (12.6 · 4.1 "90°"; the axis was not written in the spec — horizontal, 11-13).
        public const float FovMin = 75f, FovMax = 100f, FovDefault = 90f;
        // Ripple contrast: an alpha multiplier on rings, source snapshots and the afterglow only.
        public const float BrightnessMin = .8f, BrightnessMax = 1.2f, BrightnessDefault = 1f;
        public const float VolumeDefault = .8f;   // 12.6 마스터 볼륨 80%
        public const float MouseMin = .2f, MouseMax = 3f, MouseDefault = 1f;   // × PlayerMovement.mouseSensitivity (2.2)
        public static readonly int[] FrameLimits = { 60, 120, 144, 240, -1 };   // -1 = no limit
        public const int FrameLimitDefault = 4;
        // Display mode / resolution: -1 = leave the window as it was launched (command line, harness windows).
        public const int Unset = -1;

        public float Fov = FovDefault;
        public float Brightness = BrightnessDefault;
        public bool ColorBlind;
        public int DisplayMode = Unset;          // 0 전체 화면, 1 전체 화면 창모드, 2 창 모드
        public int ResolutionWidth = Unset, ResolutionHeight = Unset;
        public int FrameLimit = FrameLimitDefault;
        public bool ShowFps;
        public float MasterVolume = VolumeDefault;
        public float MouseScale = MouseDefault;
        public bool InvertY;

        public SettingsValues Copy() => (SettingsValues)MemberwiseClone();
    }

    public static class SettingsStore
    {
        private const string Prefix = "marco.settings.";

        // Out-of-range, NaN or broken values come back as the default; corrected tells the caller to write them back.
        public static SettingsValues Load(out bool corrected)
        {
            corrected = false;
            var v = new SettingsValues();
            try
            {
                v.Fov = ReadFloat("fov", SettingsValues.FovMin, SettingsValues.FovMax, SettingsValues.FovDefault, ref corrected);
                v.Brightness = ReadFloat("brightness", SettingsValues.BrightnessMin, SettingsValues.BrightnessMax, SettingsValues.BrightnessDefault, ref corrected);
                v.ColorBlind = ReadInt("colorBlind", 0, 1, 0, ref corrected) == 1;
                v.DisplayMode = ReadInt("displayMode", SettingsValues.Unset, 2, SettingsValues.Unset, ref corrected);
                v.ResolutionWidth = ReadInt("resolutionWidth", SettingsValues.Unset, 16384, SettingsValues.Unset, ref corrected);
                v.ResolutionHeight = ReadInt("resolutionHeight", SettingsValues.Unset, 16384, SettingsValues.Unset, ref corrected);
                if ((v.ResolutionWidth > 0) != (v.ResolutionHeight > 0) || (v.ResolutionWidth > 0 && (v.ResolutionWidth < 320 || v.ResolutionHeight < 200)))
                {
                    v.ResolutionWidth = v.ResolutionHeight = SettingsValues.Unset;
                    corrected = true;
                }
                v.FrameLimit = ReadInt("frameLimit", 0, SettingsValues.FrameLimits.Length - 1, SettingsValues.FrameLimitDefault, ref corrected);
                v.ShowFps = ReadInt("showFps", 0, 1, 0, ref corrected) == 1;
                v.MasterVolume = ReadFloat("masterVolume", 0f, 1f, SettingsValues.VolumeDefault, ref corrected);
                v.MouseScale = ReadFloat("mouseScale", SettingsValues.MouseMin, SettingsValues.MouseMax, SettingsValues.MouseDefault, ref corrected);
                v.InvertY = ReadInt("invertY", 0, 1, 0, ref corrected) == 1;
            }
            catch (Exception)
            {
                // PlayerPrefs itself failed: play with the defaults.
                v = new SettingsValues();
                corrected = true;
            }
            return v;
        }

        public static void Save(SettingsValues v)
        {
            if (v == null) return;
            PlayerPrefs.SetFloat(Prefix + "fov", v.Fov);
            PlayerPrefs.SetFloat(Prefix + "brightness", v.Brightness);
            PlayerPrefs.SetInt(Prefix + "colorBlind", v.ColorBlind ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "displayMode", v.DisplayMode);
            PlayerPrefs.SetInt(Prefix + "resolutionWidth", v.ResolutionWidth);
            PlayerPrefs.SetInt(Prefix + "resolutionHeight", v.ResolutionHeight);
            PlayerPrefs.SetInt(Prefix + "frameLimit", v.FrameLimit);
            PlayerPrefs.SetInt(Prefix + "showFps", v.ShowFps ? 1 : 0);
            PlayerPrefs.SetFloat(Prefix + "masterVolume", v.MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "mouseScale", v.MouseScale);
            PlayerPrefs.SetInt(Prefix + "invertY", v.InvertY ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static float ReadFloat(string key, float min, float max, float fallback, ref bool corrected)
        {
            if (!PlayerPrefs.HasKey(Prefix + key)) return fallback;
            float value = PlayerPrefs.GetFloat(Prefix + key, float.NaN);
            if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) { corrected = true; return fallback; }
            return value;
        }

        private static int ReadInt(string key, int min, int max, int fallback, ref bool corrected)
        {
            if (!PlayerPrefs.HasKey(Prefix + key)) return fallback;
            int value = PlayerPrefs.GetInt(Prefix + key, int.MinValue);
            if (value < min || value > max) { corrected = true; return fallback; }
            return value;
        }
    }
}
