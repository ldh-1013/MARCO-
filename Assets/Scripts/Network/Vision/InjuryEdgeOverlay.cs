using System;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Vision
{
    // V28 A-0 체력 가장자리 표시: the owner Runner's 1.0 (상처, §16.2 술래 레드) / 1.5 (회복 중, §16.2 앰버) state as an organic
    // screen-edge stain (EdgeStain shader, InjuryEdgeRules curves). Local only — reads HealthHalves · LastInjuryTime that the
    // server already sends to the owner. Drawn above the darkness mask (depth 100) and behind the HUD (depth 0).
    public sealed class InjuryEdgeOverlay : MonoBehaviour
    {
        // Low-saturation seep, not a glowing band: the palette colour pulled toward its own luminance and darkened.
        private const float Desaturate = .35f, Darken = .6f;
        public static readonly Color Amber = new Color32(0xFF, 0xB8, 0x4D, 0xFF);   // §16.2 상호작용 오브젝트(앰버)
        public static readonly Color AmberColorBlind = new Color32(0xC9, 0x8A, 0x2E, 0xFF);

        private static readonly int ColorId = Shader.PropertyToID("_Color"), AlphaId = Shader.PropertyToID("_Alpha"),
            SeedId = Shader.PropertyToID("_Seed"), DriftId = Shader.PropertyToID("_Drift"), AspectId = Shader.PropertyToID("_Aspect");

        private Material material;
        private int trackedHalves = -1;
        private float fromAlpha, toAlpha, changedAt = -10f, seed, seenInjury = -1f;
        private Color fromColor, toColor;
        private bool failed, hitChange;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var host = new GameObject("Injury edge overlay") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<InjuryEdgeOverlay>();
        }

        public static Color Stain(Color palette)
        {
            float luminance = palette.r * .299f + palette.g * .587f + palette.b * .114f;
            var c = Color.Lerp(palette, new Color(luminance, luminance, luminance), Desaturate) * Darken;
            c.a = 1f;
            return c;
        }

        private static Color StateColor(int halves) => halves == 3
            ? Stain(RippleVisualSettings.ColorBlindMode ? AmberColorBlind : Amber) : Stain(RippleVisualSettings.Seeker);

        private void OnGUI()
        {
            if (failed || Event.current.type != EventType.Repaint) return;
            try { Draw(); }
            catch (Exception exception) { failed = true; Debug.LogException(exception, this); }
        }

        private void Draw()
        {
            var round = NetworkRound.Instance;
            var player = NetworkPlayer.Local;
            bool shown = round != null && round.Mode != RoundMode.NotStarted && !round.IsResultScreenShown && player != null &&
                player.Role == PlayerRole.Runner && player.Status == PlayerStatus.Alive;
            int halves = shown && player.HealthHalves > 0 && player.HealthHalves < round.FullHalves ? player.HealthHalves : 0;
            float now = Time.time;
            float current = hitChange ? toAlpha : InjuryEdgeRules.Transition(fromAlpha, toAlpha, now - changedAt);
            if (halves != trackedHalves)
            {
                Color currentColor = Color.Lerp(fromColor, toColor, Mathf.Clamp01((now - changedAt) / InjuryEdgeRules.ChangeSeconds));
                trackedHalves = halves;
                fromAlpha = current;
                toAlpha = InjuryEdgeRules.TargetAlpha(halves);
                fromColor = current > 0f ? currentColor : StateColor(halves);
                toColor = halves > 0 ? StateColor(halves) : fromColor;
                changedAt = shown ? now : now - InjuryEdgeRules.ChangeSeconds;   // leaving the round clears at once
                // a hit lowers the lives and stamps LastInjuryTime in the same message (NetworkPlayer.HealthTarget)
                hitChange = shown && halves > 0 && player.LastInjuryTime >= 0f && Mathf.Abs(player.LastInjuryTime - now) < .1f;
            }
            float swell = 0f;
            if (shown && player.LastInjuryTime >= 0f)
            {
                if (player.LastInjuryTime != seenInjury) { seenInjury = player.LastInjuryTime; seed = UnityEngine.Random.Range(0f, 64f); }
                swell = InjuryEdgeRules.Swell(now - player.LastInjuryTime);
            }
            float alpha = InjuryEdgeRules.Displayed(fromAlpha, toAlpha, now - changedAt, hitChange,
                swell > 0f ? now - player.LastInjuryTime : -1f);
            if (alpha <= .001f) return;
            if (material == null)
            {
                var shader = Resources.Load<Shader>("MarcoPresentation/EdgeStain");
                if (shader == null) { failed = true; return; }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            material.SetColor(ColorId, Color.Lerp(fromColor, toColor, hitChange ? 1f : Mathf.Clamp01((now - changedAt) / InjuryEdgeRules.ChangeSeconds)));
            material.SetFloat(AlphaId, alpha);
            material.SetFloat(SeedId, seed);
            material.SetFloat(DriftId, now * InjuryEdgeRules.DriftPerSecond);
            material.SetFloat(AspectId, Screen.width / (float)Mathf.Max(1, Screen.height));
            var saved = GUI.matrix;
            int depth = GUI.depth;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = 50;   // above the darkness (100), behind the HUD (0)
            Graphics.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, material);
            GUI.matrix = saved;
            GUI.depth = depth;
        }

        private void OnDestroy() { if (material != null) Destroy(material); }
    }
}
