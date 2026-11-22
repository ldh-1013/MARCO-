using System.Collections.Generic;
using Marco.Network.Character;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Vision
{
    // 연출 4.2 · 15.4 메아리 전환 셰이더 [11-23 V44]: on a survivor's screen the tagged Runner's living body is kept for the fade instead of
    // being swapped for the hidden ghost model at once; from 0.50 s to 1.00 s after the hit (1.50 – 2.00 s when the target was underwater:
    // the forced rise of 부록 A [태그 판정] takes the first 1.0 s) it turns #EF6A4C, fades to nothing and floats up 0.25 m; then the body is
    // hidden by the usual rule (survivors never see Echo bodies). Local presentation only: the synced status, the server state and every
    // judgement are untouched — only WHEN this client swaps the body changes (NetworkPlayer.ShownStatus holds the living look).
    public static class EchoFadeRules
    {
        public const float FadeStartSeconds = .5f, FadeEndSeconds = 1f, UnderwaterDelaySeconds = 1f;
        // [GAP 연출 고유 시작값 — 연출.md 16.2]: the float-up height (연출 15.4 "살짝 떠오른다")
        public const float RiseMeters = .25f;
        public static readonly Color Tint = FeelRules.SeekerFlashColor;   // 4.2 "적색 #EF6A4C으로 물든다"

        // Seconds from the hit until the body is hidden (= the end of the fade).
        public static float HoldSeconds(bool underwater) => FadeEndSeconds + (underwater ? UnderwaterDelaySeconds : 0f);
        // 0 → 1 over the fade window (0.5 s long), measured from the hit.
        public static float Progress(float t, bool underwater) =>
            Mathf.Clamp01((t - (underwater ? UnderwaterDelaySeconds : 0f) - FadeStartSeconds) / (FadeEndSeconds - FadeStartSeconds));
        // Fade out slowly then quickly (ease-in), turn red quickly then settle (ease-out), float up with ease-out.
        public static float Alpha(float t, bool underwater) { float p = Progress(t, underwater); return 1f - p * p; }
        public static float TintAmount(float t, bool underwater) => FeelRules.EaseOut(Progress(t, underwater));
        public static float Rise(float t, bool underwater) => RiseMeters * FeelRules.EaseOut(Progress(t, underwater));
    }

    // The fade of one body (no NetworkPlayer needed — the EditMode tests drive it). Materials of the living model are the per-instance
    // runtime copies CharacterBody made, so switching them to transparent and back is local to this body.
    public sealed class EchoFadeRun
    {
        struct Saved { public Material Material; public float Surface, Src, Dst, ZWrite; public Color Base; public int Queue; public bool HasSurface, HasBlend, HasZ; }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), SurfaceId = Shader.PropertyToID("_Surface"),
            SrcId = Shader.PropertyToID("_SrcBlend"), DstId = Shader.PropertyToID("_DstBlend"), ZId = Shader.PropertyToID("_ZWrite");

        readonly List<Saved> saved = new(16);
        Transform root; Vector3 rootLocal;
        float startedAt; bool underwater, active, transparent;

        public bool Active => active;
        public bool Underwater => underwater;
        public float HoldSeconds => EchoFadeRules.HoldSeconds(underwater);
        // For the checks and the capture CSV.
        public float CurrentAlpha { get; private set; } = 1f;

        // Looks at `bodyRoot`: its renderers' materials and its local position. False when there is nothing to fade.
        public bool Begin(Transform bodyRoot, bool wasUnderwater, float now)
        {
            Cancel();
            if (bodyRoot == null) return false;
            root = bodyRoot; rootLocal = bodyRoot.localPosition; underwater = wasUnderwater; startedAt = now; active = true; transparent = false;
            CurrentAlpha = 1f;
            foreach (var renderer in bodyRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty(BaseColorId)) continue;
                    bool seen = false;
                    foreach (var s in saved) if (s.Material == material) { seen = true; break; }
                    if (seen) continue;
                    saved.Add(new Saved
                    {
                        Material = material, Base = material.GetColor(BaseColorId), Queue = material.renderQueue,
                        HasSurface = material.HasProperty(SurfaceId), Surface = material.HasProperty(SurfaceId) ? material.GetFloat(SurfaceId) : 0f,
                        HasBlend = material.HasProperty(SrcId) && material.HasProperty(DstId),
                        Src = material.HasProperty(SrcId) ? material.GetFloat(SrcId) : 0f, Dst = material.HasProperty(DstId) ? material.GetFloat(DstId) : 0f,
                        HasZ = material.HasProperty(ZId), ZWrite = material.HasProperty(ZId) ? material.GetFloat(ZId) : 1f,
                    });
                }
            }
            if (saved.Count == 0) { active = false; root = null; return false; }
            return true;
        }

        // False once the fade is over (or the body is gone): the caller then swaps to the hidden body.
        public bool Tick(float now)
        {
            if (!active) return false;
            float t = now - startedAt;
            if (t >= HoldSeconds || root == null) { Cancel(); return false; }
            float alpha = EchoFadeRules.Alpha(t, underwater), tint = EchoFadeRules.TintAmount(t, underwater);
            if (!transparent && tint > 0f) MakeTransparent();
            CurrentAlpha = alpha;
            foreach (var s in saved)
            {
                if (s.Material == null) continue;
                var tinted = Color.Lerp(s.Base, EchoFadeRules.Tint, tint); tinted.a = s.Base.a * alpha;
                s.Material.SetColor(BaseColorId, tinted);
            }
            root.localPosition = rootLocal + Vector3.up * EchoFadeRules.Rise(t, underwater);
            return true;
        }

        void MakeTransparent()
        {
            transparent = true;
            foreach (var s in saved)
            {
                if (s.Material == null) continue;
                if (s.HasSurface) s.Material.SetFloat(SurfaceId, 1f);
                if (s.HasBlend) { s.Material.SetFloat(SrcId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha); s.Material.SetFloat(DstId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
                if (s.HasZ) s.Material.SetFloat(ZId, 0f);
                s.Material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
        }

        // Puts every touched value back (null-safe: a destroyed material or body is skipped).
        public void Cancel()
        {
            foreach (var s in saved)
            {
                if (s.Material == null) continue;
                s.Material.SetColor(BaseColorId, s.Base);
                if (s.HasSurface) s.Material.SetFloat(SurfaceId, s.Surface);
                if (s.HasBlend) { s.Material.SetFloat(SrcId, s.Src); s.Material.SetFloat(DstId, s.Dst); }
                if (s.HasZ) s.Material.SetFloat(ZId, s.ZWrite);
                s.Material.renderQueue = s.Queue;
            }
            saved.Clear();
            if (root != null) root.localPosition = rootLocal;
            root = null; active = false; transparent = false; CurrentAlpha = 1f;
        }

        public bool BodyIs(Transform bodyRoot) => active && root == bodyRoot;
    }

    // On a player other than this machine's own: keeps the living look for the fade after an Alive → Tagged change seen from a survivor's
    // view. NetworkPlayer asks `Holds` in ShownStatus and calls `OnStatus` from OnStatusChanged; nothing here is sent or synced.
    [DefaultExecutionOrder(1200)]
    public sealed class EchoTransitionFade : MonoBehaviour
    {
        // For the checks and the capture CSV.
        public static int Fades { get; private set; }
        readonly EchoFadeRun run = new();
        NetworkPlayer player;
        CharacterBody body;
        float holdUntil = float.NegativeInfinity;

        void Awake() { player = GetComponent<NetworkPlayer>(); body = GetComponent<CharacterBody>(); }

        // The living look is held while the fade runs. Self-expiring (fail-open): it can never outlive the hold by more than a fraction.
        public bool Holds => Time.time < holdUntil;
        public float CurrentAlpha => run.CurrentAlpha;

        // Called before the look is re-applied. `viewer` = this machine's own player.
        public void OnStatus(PlayerStatus previous, PlayerStatus next, NetworkPlayer viewer)
        {
            // On a host the change is reported twice (server side, then client side — the second time with previous == next): ignore it.
            if (previous == next) return;
            if (previous != PlayerStatus.Alive || next != PlayerStatus.Tagged) { if (run.Active || Holds) Stop(false); return; }   // any other change ends a running fade at once
            if (run.Active) return;   // the same Alive → Tagged reported again: already fading
            if (player == null || viewer == null || viewer == player || viewer.HasEchoBody) return;   // survivors' screens only, never the target's own
            if (body == null || body.BodyRoot == null) return;
            bool underwater = player.TryGetComponent<PlayerMovement>(out var movement) && movement.IsDiving;
            if (!run.Begin(body.BodyRoot, underwater, Time.time)) return;
            holdUntil = Time.time + run.HoldSeconds + .25f;
            Fades++;
        }

        void LateUpdate()
        {
            if (!run.Active) { if (Holds) Stop(true); return; }
            // the model was rebuilt (role / look change) or the player is gone: nothing left to fade — swap now
            if (player == null || body == null || !run.BodyIs(body.BodyRoot)) { Stop(true); return; }
            if (!run.Tick(Time.time)) Stop(true);
        }

        void Stop(bool refresh)
        {
            run.Cancel();
            holdUntil = float.NegativeInfinity;
            if (refresh && player != null) player.RefreshLooks();   // the hidden body replaces the living one (EchoBodyRules)
        }

        void OnDisable() { run.Cancel(); holdUntil = float.NegativeInfinity; }
        void OnDestroy() { run.Cancel(); holdUntil = float.NegativeInfinity; }
    }
}
