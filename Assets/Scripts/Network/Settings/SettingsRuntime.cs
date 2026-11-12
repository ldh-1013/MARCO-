using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Settings
{
    // Applies the local settings. Every item is applied on its own (one failure never stops the others) and logged
    // once as plain information, never as a warning. Nothing here is networked.
    public static class SettingsRuntime
    {
        // Multiplies the final alpha of rings, source snapshots and the afterglow only (never lifetime, radius or rules).
        public static float RippleAlphaScale { get; private set; } = 1f;
        // × PlayerMovement.mouseSensitivity; 1 and false leave look input exactly as before.
        public static float MouseScale { get; private set; } = 1f;
        public static bool InvertY { get; private set; }

        public static SettingsValues Current { get; private set; }

        // Batch mode and graphics-less runs (editor checks, servers) keep every engine default untouched.
        public static bool Enabled => !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        private static readonly HashSet<string> reported = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            RippleAlphaScale = 1f;
            MouseScale = 1f;
            InvertY = false;
            Current = null;
            reported.Clear();
            frameOverridden = false;
            frameBefore = -1;
        }

        // Before the first scene: the colour-blind ring colours are in place before any ring is made.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadAtStart()
        {
            if (!Enabled) return;
            try
            {
                Current = SettingsStore.Load(out bool corrected);
                if (corrected) Save();
            }
            catch (Exception exception) { Report("load", exception); Current = new SettingsValues(); }
            ApplyAll(true);
        }

        public static void Replace(SettingsValues values)
        {
            Current = values ?? new SettingsValues();
            ApplyAll(false);
        }

        public static void Save()
        {
            try { SettingsStore.Save(Current); }
            catch (Exception exception) { Report("save", exception); }
        }

        // atStart: the display mode / resolution stay as launched unless a value was saved.
        public static void ApplyAll(bool atStart)
        {
            ApplyBrightness();
            ApplyColorBlind();
            ApplyVolume();
            ApplyMouse();
            ApplyFrameLimit();
            ApplyDisplay(atStart);
        }

        public static void ApplyBrightness()
        {
            try { RippleAlphaScale = Mathf.Clamp(Current.Brightness, SettingsValues.BrightnessMin, SettingsValues.BrightnessMax); }
            catch (Exception exception) { Report("brightness", exception); RippleAlphaScale = 1f; }
        }

        // 16.2: Runner #F2E205 / Seeker #3357FF on rings only; rings already on screen keep their colour.
        public static void ApplyColorBlind()
        {
            try { Marco.Network.Vision.RippleVisualSettings.ColorBlindMode = Current.ColorBlind; }
            catch (Exception exception) { Report("colorBlind", exception); }
        }

        public static void ApplyVolume()
        {
            try { AudioListener.volume = Mathf.Clamp01(Current.MasterVolume); }
            catch (Exception exception) { Report("volume", exception); }
        }

        public static void ApplyMouse()
        {
            try
            {
                MouseScale = Mathf.Clamp(Current.MouseScale, SettingsValues.MouseMin, SettingsValues.MouseMax);
                InvertY = Current.InvertY;
            }
            catch (Exception exception) { Report("mouse", exception); MouseScale = 1f; InvertY = false; }
        }

        // FishNet's TimeManager writes its own frame cap (500) into Application.targetFrameRate when a connection
        // starts. "제한 없음" leaves that value alone; a chosen number is kept on top of it (KeepFrameLimit, every frame,
        // one int compare) and the value it replaced comes back when the player picks "제한 없음" again.
        private static bool frameOverridden;
        private static int frameBefore = -1;

        public static void ApplyFrameLimit() => KeepFrameLimit();

        public static void KeepFrameLimit()
        {
            try
            {
                if (Current == null) return;
                int limit = SettingsValues.FrameLimits[Mathf.Clamp(Current.FrameLimit, 0, SettingsValues.FrameLimits.Length - 1)];
                if (limit <= 0)
                {
                    if (frameOverridden) { Application.targetFrameRate = frameBefore; frameOverridden = false; }
                    return;
                }
                if (Application.targetFrameRate == limit) return;
                if (!frameOverridden) frameBefore = Application.targetFrameRate;
                else if (Application.targetFrameRate != limit) frameBefore = Application.targetFrameRate;   // someone else set it again
                Application.targetFrameRate = limit;
                frameOverridden = true;
            }
            catch (Exception exception) { Report("frameLimit", exception); }
        }

        public static void ApplyDisplay(bool atStart)
        {
            try
            {
                if (Current.DisplayMode == SettingsValues.Unset && Current.ResolutionWidth <= 0) return;
                var mode = Current.DisplayMode switch
                {
                    0 => FullScreenMode.ExclusiveFullScreen,
                    1 => FullScreenMode.FullScreenWindow,
                    2 => FullScreenMode.Windowed,
                    _ => Screen.fullScreenMode,
                };
                int width = Current.ResolutionWidth > 0 ? Current.ResolutionWidth : Screen.width;
                int height = Current.ResolutionHeight > 0 ? Current.ResolutionHeight : Screen.height;
                if (atStart && mode == Screen.fullScreenMode && width == Screen.width && height == Screen.height) return;
                Screen.SetResolution(width, height, mode);
            }
            catch (Exception exception) { Report("display", exception); }
        }

        // Horizontal FOV (degrees) to Unity's vertical Camera.fieldOfView for this aspect; false keeps the camera as it is.
        public static bool TryVerticalFov(float horizontalDegrees, float aspect, out float vertical)
        {
            vertical = 0f;
            if (!(aspect > 0f) || float.IsInfinity(aspect) || float.IsNaN(horizontalDegrees)) return false;
            float h = Mathf.Clamp(horizontalDegrees, SettingsValues.FovMin, SettingsValues.FovMax) * Mathf.Deg2Rad;
            vertical = 2f * Mathf.Atan(Mathf.Tan(h * .5f) / aspect) * Mathf.Rad2Deg;
            if (float.IsNaN(vertical)) return false;
            vertical = Mathf.Clamp(vertical, 20f, 120f);
            return true;
        }

        public static void Report(string what, Exception exception)
        {
            try
            {
                if (reported.Count < 32 && reported.Add(what))
                    Debug.Log($"[MARCO][Settings] {what} 적용 실패({exception.GetType().Name}) — 이 항목만 건너뜀");
            }
            catch { /* reporting never throws */ }
        }
    }
}
