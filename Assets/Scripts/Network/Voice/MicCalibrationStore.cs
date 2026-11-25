using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Marco.Network.Voice
{
    // The few calls MicCalibrationStore needs from PlayerPrefs, so EditMode tests can use a dictionary instead.
    public interface IKeyValueStore
    {
        bool TryGetString(string key, out string value);
        void SetString(string key, string value);
        void Save();
    }

    public sealed class PlayerPrefsKeyValueStore : IKeyValueStore
    {
        public bool TryGetString(string key, out string value)
        {
            value = null;
            try
            {
                if (!PlayerPrefs.HasKey(key)) return false;
                value = PlayerPrefs.GetString(key, null);
                return value != null;
            }
            catch (Exception) { return false; }   // PlayerPrefs failed: play without the saved value
        }

        public void SetString(string key, string value)
        {
            try { PlayerPrefs.SetString(key, value); } catch (Exception) { }
        }

        public void Save()
        {
            try { PlayerPrefs.Save(); } catch (Exception) { }
        }
    }

    // 부록 A [마이크 캘리브레이션] 저장: one offset per microphone device, on this machine only (PlayerPrefs,
    // "marco.settings." like the settings window — 12.6). Values are clamped to ±6 dB; anything unreadable is "none".
    public sealed class MicCalibrationStore
    {
        public const string KeyPrefix = "marco.settings.micOffset.";
        private const int ReadableLength = 40;
        private readonly IKeyValueStore store;

        public MicCalibrationStore(IKeyValueStore store) => this.store = store;

        public static MicCalibrationStore ForPlayerPrefs() => new(new PlayerPrefsKeyValueStore());

        // The saved offset for this device, or null (never measured, unreadable, or no device name).
        public float? Load(string device)
        {
            if (string.IsNullOrEmpty(device) || store == null) return null;
            if (!store.TryGetString(Key(device), out string text)) return null;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return null;
            if (float.IsNaN(value) || float.IsInfinity(value)) return null;
            return MicCalibration.ClampOffset(value);
        }

        public float? LoadVoiceFloor(string device)
        {
            if (string.IsNullOrEmpty(device) || store == null ||
                !store.TryGetString("marco.settings.micVoiceFloor." + DeviceKey(device), out string text)) return null;
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
                value >= -120f && value <= 0f ? value : (float?)null;
        }

        public void Save(string device, float offsetDb, float? voiceFloor = null)
        {
            if (string.IsNullOrEmpty(device) || store == null || float.IsNaN(offsetDb) || float.IsInfinity(offsetDb)) return;
            store.SetString(Key(device), MicCalibration.ClampOffset(offsetDb).ToString("R", CultureInfo.InvariantCulture));
            if (voiceFloor.HasValue)
                store.SetString("marco.settings.micVoiceFloor." + DeviceKey(device),
                    voiceFloor.Value.ToString("R", CultureInfo.InvariantCulture));
            store.Save();
        }

        // marco.settings.micOffset.<readable>-<hash>: ASCII letters and digits kept, anything else '_', at most 40
        // characters, then FNV-1a 32-bit of the full UTF-8 name so two names that read the same never share a key.
        public static string Key(string device) => KeyPrefix + DeviceKey(device);

        public static string DeviceKey(string device)
        {
            device ??= "";
            var readable = new StringBuilder(Math.Min(device.Length, ReadableLength));
            foreach (char c in device)
            {
                if (readable.Length >= ReadableLength) break;
                readable.Append(c < 128 && char.IsLetterOrDigit(c) ? c : '_');
            }
            uint hash = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(device)) hash = (hash ^ b) * 16777619;
            return readable + "-" + hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
