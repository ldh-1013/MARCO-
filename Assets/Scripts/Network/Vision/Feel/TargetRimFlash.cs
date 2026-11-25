using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network.Vision
{
    // 연출 4.5 · 15.3 첫 적중 대상 몸 플래시 [11-22 V43]: on the Seeker's screen the body of the first-hit target lights its
    // standing role rim (Runner #7FE7E0 — no new colour) for 0.2 s, through the character shader's _FeelRim slot
    // (MaterialPropertyBlock, local only, nothing sent). It is a transient: 40 ms ease-out rise, critically damped fall, exactly 0 at
    // 0.2 s, and _FeelRim is written back to 0 when it ends — the body carries no rim at any other time (V41, BodyRim off).
    // Driven by the existing PlayerInteractor.LocalSwingConfirmed (Wounded) through CameraFeelStack; reads only.
    public static class TargetRimRules
    {
        // 4.5 "0.2초 밝게" is the spec; the rise and the strength are [GAP 연출 고유 시작값 — 연출.md 16.2] (the strength is the
        // Seeker's own fingertip rim's, FeelRules.RimIntensity, so the two flashes of one hit read alike).
        public const float Seconds = .2f, RiseSeconds = .04f, Intensity = 1.2f;
        // 부록 A [태그 판정]: the authority accepts a target up to 0.8 m beyond the client's 1.6 m check.
        public const float LatencyMarginMeters = .8f;

        public static float Weight(float t) => FeelRules.Impulse(t, RiseSeconds, Seconds) * FeelRules.FeelIntensity;
        public static Color RimColor(float t) => SourceSnapshots.RunnerRim * (Intensity * Weight(t));
    }

    public sealed class TargetRimFlash
    {
        // For the checks and the capture CSV.
        public static int Flashes { get; private set; }
        public static float CurrentWeight { get; private set; }

        private const float Never = -1000f;
        private readonly List<Renderer> renderers = new(16);
        private MaterialPropertyBlock block;
        private float startedAt = Never;
        private bool written;

        public bool Active => startedAt > Never;

        // `body` = the renderers of the target's 3rd-person model (and capsule).
        public void Begin(IEnumerable<Renderer> body, float now)
        {
            Release();
            foreach (var renderer in body) if (renderer != null) renderers.Add(renderer);
            if (renderers.Count == 0) return;
            startedAt = now; Flashes++;
        }

        public void Tick(float now)
        {
            if (!Active) { CurrentWeight = 0f; return; }
            float t = now - startedAt;
            if (t >= TargetRimRules.Seconds) { Release(); return; }
            CurrentWeight = TargetRimRules.Weight(t);
            Write(TargetRimRules.RimColor(t));
        }

        // Writes 0 back (the shader default) and forgets the target — also on a round end or a rebind.
        public void Release()
        {
            if (written) Write(Color.black);
            written = false; startedAt = Never; CurrentWeight = 0f;
            renderers.Clear();
        }

        private void Write(Color rim)
        {
            block ??= new MaterialPropertyBlock();
            int id = Shader.PropertyToID("_FeelRim");
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetColor(id, rim);
                renderer.SetPropertyBlock(block);
            }
            written = rim.maxColorComponent > 0f;
        }
    }
}
