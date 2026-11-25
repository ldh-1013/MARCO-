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
        public static readonly Color Amber = new Color32(0xFF, 0xB8, 0x4D, 0xFF);   // §16.2 상호작용 오브젝트(앰버)
        public static readonly Color AmberColorBlind = new Color32(0xC9, 0x8A, 0x2E, 0xFF);

        private static readonly int ColorId = Shader.PropertyToID("_Color"), AlphaId = Shader.PropertyToID("_Alpha"),
            SeedId = Shader.PropertyToID("_Seed"), DriftId = Shader.PropertyToID("_Drift"), AspectId = Shader.PropertyToID("_Aspect"),
            CornerDepthId = Shader.PropertyToID("_CornerDepth"), EdgeDepthId = Shader.PropertyToID("_EdgeDepth");

        private Material material;
        // VB B-4 8.2 질식: when the owner's forced-surfacing slow starts (PlayerMovement.SwimPenaltyRemaining — the value the
        // server already sends), the stain closes in red and hands back to the health edge (BreathFeelRules); one stain, one peak.
        public static int Suffocations { get; private set; }
        public static float CurrentSuffocationWeight { get; private set; }
        private float lastPenalty, suffocatedAt = -10f;
        private int trackedHalves = -1;
        private float fromAlpha, toAlpha, changedAt = -10f, seed, seenInjury = -1f;
        private Color fromColor, toColor;
        private Vector2 fromDepth = InjuryEdgeRules.Depth(2), toDepth = InjuryEdgeRules.Depth(2);
        private bool failed, hitChange;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var host = new GameObject("Injury edge overlay") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            host.AddComponent<InjuryEdgeOverlay>();
        }

        // Low-saturation seep, not a glowing band — correction per state in InjuryEdgeRules (연출.md 16.1).
        private static Color StateColor(int halves) => halves == 3
            ? InjuryEdgeRules.StainColor(RippleVisualSettings.ColorBlindMode ? AmberColorBlind : Amber, 3)
            : InjuryEdgeRules.StainColor(RippleVisualSettings.Seeker, 2);

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
                float blend = hitChange ? 1f : Mathf.Clamp01((now - changedAt) / InjuryEdgeRules.ChangeSeconds);
                Color currentColor = Color.Lerp(fromColor, toColor, blend);
                Vector2 currentDepth = Vector2.Lerp(fromDepth, toDepth, blend * blend * (3f - 2f * blend));
                trackedHalves = halves;
                fromAlpha = current;
                toAlpha = InjuryEdgeRules.TargetAlpha(halves);
                fromColor = current > 0f ? currentColor : StateColor(halves);
                toColor = halves > 0 ? StateColor(halves) : fromColor;
                fromDepth = current > 0f ? currentDepth : InjuryEdgeRules.Depth(halves);
                toDepth = halves > 0 ? InjuryEdgeRules.Depth(halves) : fromDepth;   // fading out keeps its shape
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
            float penalty = shown && player.TryGetComponent<PlayerMovement>(out var movement) ? movement.SwimPenaltyRemaining : 0f;
            if (shown && BreathFeelRules.Suffocated(lastPenalty, penalty)) { suffocatedAt = now; Suffocations++; Debug.Log("[MARCO][Feel] suffocation edge"); }
            lastPenalty = penalty;
            float sinceSuffocation = now - suffocatedAt;
            bool suffocating = shown && sinceSuffocation >= 0f && sinceSuffocation < BreathFeelRules.SuffocationSeconds;
            CurrentSuffocationWeight = suffocating ? BreathFeelRules.SuffocationWeight(sinceSuffocation) : 0f;
            // while the suffocation runs, the health edge's own swell is folded into it (a single peak)
            float alpha = InjuryEdgeRules.Displayed(fromAlpha, toAlpha, now - changedAt, hitChange,
                swell > 0f && !suffocating ? now - player.LastInjuryTime : -1f);
            float healthAlpha = alpha;
            if (suffocating) alpha = BreathFeelRules.MixAlpha(healthAlpha, sinceSuffocation);
            if (alpha <= .001f) return;
            if (material == null)
            {
                var shader = Resources.Load<Shader>("MarcoPresentation/EdgeStain");
                if (shader == null) { failed = true; return; }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            Color stain = Color.Lerp(fromColor, toColor, hitChange ? 1f : Mathf.Clamp01((now - changedAt) / InjuryEdgeRules.ChangeSeconds));
            // 질식 red = the 1.0 edge's own colour (#FF3B4D through InjuryEdgeRules), so the hand-over has no colour step
            if (suffocating) stain = Color.Lerp(healthAlpha > .001f ? stain : StateColor(2), StateColor(2), CurrentSuffocationWeight);
            material.SetColor(ColorId, stain);
            material.SetFloat(AlphaId, alpha);
            material.SetFloat(SeedId, seed);
            material.SetFloat(DriftId, now * InjuryEdgeRules.DriftPerSecond);
            material.SetFloat(AspectId, Screen.width / (float)Mathf.Max(1, Screen.height));
            float shape = hitChange ? 1f : Mathf.Clamp01((now - changedAt) / InjuryEdgeRules.ChangeSeconds);
            Vector2 depthNow = Vector2.Lerp(fromDepth, toDepth, shape * shape * (3f - 2f * shape));
            if (suffocating) depthNow = BreathFeelRules.MixDepth(depthNow, healthAlpha > .001f, sinceSuffocation);
            material.SetFloat(CornerDepthId, depthNow.x);
            material.SetFloat(EdgeDepthId, depthNow.y);
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
