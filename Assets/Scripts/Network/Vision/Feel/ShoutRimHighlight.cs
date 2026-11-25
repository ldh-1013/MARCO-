using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Vision
{
    // 기획서 §16.1 · §3.5 · 연출 8.1 술래 고함 실루엣 하이라이트 [11-23 V44 · V44c]: when a Seeker's shout reaches this client, the Seeker's
    // body lights the Seeker rim (#EF6A4C — no new colour) for 0.5 s and lets it go again, through the character shader's _FeelRim slot
    // (MaterialPropertyBlock, local only, nothing sent). A transient: 80 ms ease-out rise, then an ease-out fall that is exactly 0 at 0.5 s;
    // _FeelRim is written back to 0 when it ends — never a standing rim. The rim is multiplied by the body's current light visibility (the
    // listener's own voice / ripple light, the _Marco* globals the body shader itself reads), so outside the light it is not lit at all —
    // a shout does not show where the Seeker is to someone who cannot see that spot.
    // Driven by PlayerMovement.RemoteShoutHeard (read-only, raised once per shout per listener from the existing AbilitySoundTarget).
    public static class ShoutRimRules
    {
        // 기획서 · 연출.md give no value (8.1 only says "적색 실루엣 하이라이트"): 0.5 s ("짧게"), rise 80 ms, strength 1.0
        // [GAP 연출 고유 시작값 — 연출.md 16.2; V44c: the V44 length "perceived pulse duration" (~2.5 s) was too long and leaked a position].
        public const float RiseSeconds = .08f, Seconds = .5f, Intensity = 1f;
        public static readonly Color Colour = FeelRules.SeekerFlashColor;   // §16.1 술래 림 #EF6A4C

        // 0 before the shout and from `Seconds` on; an ease-out rise to 1, then an ease-out fall ((1 − x)², fast first and softly at the end).
        public static float Weight(float t)
        {
            if (!(t >= 0f) || t >= Seconds) return 0f;
            if (t < RiseSeconds) return FeelRules.EaseOut(t / RiseSeconds);
            float rest = 1f - (t - RiseSeconds) / (Seconds - RiseSeconds);
            return rest * rest;   // exactly 1 at the end of the rise, exactly 0 at Seconds, no bounce
        }

        // The body shader's own light at a point `distance` from the light: pow(saturate(1 − d / R), falloff) × intensity (clamped 0 … 1).
        // `worldMode` false (no mic-light world — lobby) = the body is drawn plainly: 1.
        public static float LightVisibility(float distance, float radius, float intensity, float falloff, bool worldMode)
        {
            if (!worldMode) return 1f;
            if (!(radius > .001f)) return 0f;
            float attenuation = Mathf.Pow(Mathf.Clamp01(1f - distance / radius), Mathf.Max(.01f, falloff));
            return Mathf.Clamp01(attenuation * Mathf.Max(0f, intensity));
        }

        // Below this the body is effectively dark: the rim is not written at all.
        public const float MinVisibility = .02f;
        public static Color RimColor(float t, float visibility) =>
            visibility < MinVisibility ? Color.black : Colour * (Intensity * Weight(t) * Mathf.Clamp01(visibility) * FeelRules.FeelIntensity);
    }

    // Binds every player's PlayerMovement.RemoteShoutHeard (players come and go), runs one highlight per Seeker and writes the rim.
    public sealed class ShoutRimHighlight : MonoBehaviour
    {
        sealed class Run
        {
            public readonly List<Renderer> Renderers = new(16);
            public float StartedAt;
            public bool Written;
        }

        // For the checks and the capture CSV.
        public static int Highlights { get; private set; }
        public static float CurrentWeight { get; private set; }
        public static int ActiveRuns { get; private set; }
        // The light visibility of the last frame's brightest run (0 … 1) — for the checks and the capture CSV.
        public static float LastVisibility { get; private set; }
        static readonly int LightPositionId = Shader.PropertyToID("_MarcoPlayerPosition"), LightRadiusId = Shader.PropertyToID("_MarcoLightRadius"),
            LightIntensityId = Shader.PropertyToID("_MarcoLightIntensity"), LightFalloffId = Shader.PropertyToID("_MarcoFalloffPower"), WorldModeId = Shader.PropertyToID("_MarcoMinusEnabled");

        static ShoutRimHighlight instance;
        static readonly int FeelRimId = Shader.PropertyToID("_FeelRim");
        readonly Dictionary<PlayerMovement, System.Action<float>> bound = new();
        readonly Dictionary<NetworkPlayer, Run> runs = new();
        readonly List<NetworkPlayer> done = new(4);
        readonly List<PlayerMovement> gone = new(4);
        MaterialPropertyBlock block;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (instance != null) return;
            var go = new GameObject("Shout rim highlight") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ShoutRimHighlight>();
        }

        void LateUpdate()
        {
            var all = NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var player = all[i];
                if (player == null || !player.TryGetComponent<PlayerMovement>(out var movement) || bound.ContainsKey(movement)) continue;
                var owner = player;
                System.Action<float> handler = duration => Begin(owner, duration);
                movement.RemoteShoutHeard += handler;
                bound[movement] = handler;
            }
            gone.Clear();
            foreach (var pair in bound) if (pair.Key == null) gone.Add(pair.Key);
            foreach (var dead in gone) bound.Remove(dead);

            float weight = 0f, brightest = 0f; done.Clear();
            foreach (var pair in runs)
            {
                var run = pair.Value;
                float t = Time.time - run.StartedAt;
                if (pair.Key == null || t >= ShoutRimRules.Seconds || !Showable(pair.Key)) { done.Add(pair.Key); continue; }
                float visibility = Visibility(run);
                brightest = Mathf.Max(brightest, visibility);
                weight = Mathf.Max(weight, ShoutRimRules.Weight(t) * visibility);
                Write(run, ShoutRimRules.RimColor(t, visibility));
            }
            foreach (var key in done) End(key);
            CurrentWeight = weight; ActiveRuns = runs.Count; LastVisibility = brightest;
        }

        // A living Seeker only (a Seeker that left, died or changed role stops the highlight at once).
        static bool Showable(NetworkPlayer player) =>
            player.Role == PlayerRole.Seeker && player.Status == PlayerStatus.Alive && !player.IsEcho && NetworkPlayer.Local != player;

        void Begin(NetworkPlayer seeker, float perceivedDuration)
        {
            if (seeker == null || !Showable(seeker) || !seeker.TryGetComponent<CharacterBody>(out var body) || body.BodyRoot == null) return;
            End(seeker);   // a second shout while one still glows restarts it (the cooldown is 45 s — this is only a fail-safe)
            var run = new Run { StartedAt = Time.time };
            run.Renderers.AddRange(body.BodyRoot.GetComponentsInChildren<Renderer>(true));
            if (run.Renderers.Count == 0) return;
            runs[seeker] = run; Highlights++;
            Write(run, ShoutRimRules.RimColor(0f, Visibility(run)));
        }

        // How lit the Seeker's body is for this listener right now: the nearest point of the body to the light, through the body shader's own rule.
        static float Visibility(Run run)
        {
            bool worldMode = Shader.GetGlobalFloat(WorldModeId) > .5f;
            if (!worldMode) return ShoutRimRules.LightVisibility(0f, 0f, 0f, 1f, false);
            Vector3 light = Shader.GetGlobalVector(LightPositionId);
            float best = float.MaxValue;
            for (int i = 0; i < run.Renderers.Count; i++)
            {
                var renderer = run.Renderers[i];
                if (renderer == null || !renderer.enabled) continue;
                best = Mathf.Min(best, Vector3.Distance(renderer.bounds.ClosestPoint(light), light));
            }
            if (best == float.MaxValue) return 0f;
            return ShoutRimRules.LightVisibility(best, Shader.GetGlobalFloat(LightRadiusId), Shader.GetGlobalFloat(LightIntensityId), Shader.GetGlobalFloat(LightFalloffId), true);
        }

        void End(NetworkPlayer seeker)
        {
            if (seeker == null || !runs.TryGetValue(seeker, out var run)) { if (seeker == null) PurgeNull(); return; }
            if (run.Written) Write(run, Color.black);
            runs.Remove(seeker);
        }

        void PurgeNull() { var drop = new List<NetworkPlayer>(); foreach (var pair in runs) if (pair.Key == null) drop.Add(pair.Key); foreach (var key in drop) runs.Remove(key); }

        void Write(Run run, Color rim)
        {
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < run.Renderers.Count; i++)
            {
                var renderer = run.Renderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetColor(FeelRimId, rim);
                renderer.SetPropertyBlock(block);
            }
            run.Written = rim.maxColorComponent > 0f;
        }

        void OnDestroy()
        {
            foreach (var key in new List<NetworkPlayer>(runs.Keys)) End(key);
            foreach (var pair in bound) if (pair.Key != null) pair.Key.RemoteShoutHeard -= pair.Value;
            bound.Clear();
            if (instance == this) instance = null;
        }
    }
}
