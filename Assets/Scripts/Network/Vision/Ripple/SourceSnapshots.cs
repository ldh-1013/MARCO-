using System;
using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using Marco.Network.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Vision
{
    // §5.5-2 발생원 스냅샷 · §14.2 [10-05]: the frozen silhouette of the character that made a ring-display sound,
    // drawn on the listener's screen with the ring. Client only. The server decides who may be seen (the last two
    // ints of the ring RPCs, SourceSnapshotRules); this only draws. Nothing is sent, nothing is in a scene or prefab.
    //   pose   the remote body's pose at the moment the RPC arrives (SkinnedMeshRenderer.BakeMesh into pooled meshes),
    //          fixed at the body's position and rotation of that moment — it does not follow the character
    //   colour the §16.1 role rim colour, Runner #7FE7E0 / Seeker #EF6A4C
    //   time   the perceived duration the ring got, with the ring's own curve: 0.85 × fade-in 0.05 s × (1 − t)^1.25
    //   draw   unlit flat colour with normal depth test (no X-ray: hidden behind walls), no shadows, no particles —
    //          MARCO/ResultPodiumGlow _Shape 7 (Resources, reused unchanged)
    //   D1     a new snapshot of a source replaces that source's previous one
    //   D2     at most Cap at once (technical memory cap), the oldest goes first
    // Only in the survivor view of a running round (SurvivorBodyVisibility.Active); everything is released otherwise.
    // Every entry point is guarded: a fault never reaches the RPC that called it.
    public sealed class SourceSnapshots : MonoBehaviour
    {
        public const int Cap = 8;   // D2: technical cap (memory), not a design value
        public static readonly Color RunnerRim = new Color32(0x7F, 0xE7, 0xE0, 0xFF);   // §16.1 도망자 림라이트
        public static readonly Color SeekerRim = new Color32(0xEF, 0x6A, 0x4C, 0xFF);   // §16.1 술래 림라이트
        public const int FakeRunnerId = -2;

        struct Part
        {
            public Mesh Mesh;
            public bool Pooled;
            public Matrix4x4 Matrix;
            public int SubMeshes;
        }

        sealed class Snapshot
        {
            public bool Live;
            public int PlayerId;
            public float Start, Duration;
            public Color Color;
            public readonly List<Part> Parts = new(8);
        }

        static readonly Snapshot[] slots = MakeSlots();
        static readonly Stack<Mesh> pool = new(16);
        static readonly int ColorId = Shader.PropertyToID("_Color"), ShapeId = Shader.PropertyToID("_Shape");
        static SourceSnapshots instance;
        static Material material;
        static MaterialPropertyBlock block;
        static bool unusable;
        static GameObject fakeAnchor;
        static SkinnedMeshRenderer[] fakeSkins;

        // For the checks and the temporary logs.
        public static int Shown { get; private set; }
        public static int Replaced { get; private set; }
        public static int Evicted { get; private set; }
        public static int MeshesCreated { get; private set; }
        public static int PoolCount => pool.Count;
        public static int LiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < slots.Length; i++) if (slots[i].Live) n++;
                return n;
            }
        }

        static Snapshot[] MakeSlots()
        {
            var array = new Snapshot[Cap];
            for (int i = 0; i < Cap; i++) array[i] = new Snapshot();
            return array;
        }

        static void Ensure()
        {
            if (!ReferenceEquals(instance, null)) return;
            var go = new GameObject("Source Snapshots");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SourceSnapshots>();
        }

        static bool Ready()
        {
            if (unusable) return false;
            if (material != null) return true;
            var shader = Shader.Find("MARCO/ResultPodiumGlow");
            if (shader == null || !shader.isSupported) { unusable = true; return false; }
            material = new Material(shader) { name = "Source Snapshot", hideFlags = HideFlags.DontSave };
            block = new MaterialPropertyBlock();
            return true;
        }

        // One source for this listener (called from the ring RPCs with a PlayerId the server sent).
        public static void Show(int playerId, float duration)
        {
            try
            {
                if (playerId < 0 || !(duration > 0f) || !SurvivorBodyVisibility.Active()) return;
                var local = NetworkPlayer.Local;
                if (local == null || local.PlayerId == playerId || local.HasEchoBody) return;
                if (!NetworkPlayer.TryGet(playerId, out var source) || source == null) return;
                if (!SurvivorBodyVisibility.TryGetBody(source, out var renderers) || !Ready()) return;
                Ensure();
                var slot = Take(playerId);
                for (int i = 0; i < renderers.Count; i++) Add(slot, renderers[i]);
                Begin(slot, playerId, duration, source.Role == PlayerRole.Seeker ? SeekerRim : RunnerRim);
            }
            catch (Exception exception) { DirectionHints.ReportFault("SourceSnapshots.Show", exception); }
        }

        // The two ints of a ring RPC: each one shown unless it is -1 (none).
        public static void ShowBoth(int snapshotA, int snapshotB, float duration)
        {
            if (snapshotA >= 0) Show(snapshotA, duration);
            if (snapshotB >= 0 && snapshotB != snapshotA) Show(snapshotB, duration);
        }

        // §5.5-2 메아리 노크: a Runner-coloured silhouette in the Runner idle pose at the knock point. Built, not called yet.
        public static void ShowFakeRunner(Vector3 point, float yaw, float duration)
        {
            try
            {
                if (!(duration > 0f) || !SurvivorBodyVisibility.Active() || !Ready() || !FakeRunnerReady()) return;
                Ensure();
                var slot = Take(FakeRunnerId);
                var pose = Matrix4x4.TRS(point, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
                var anchor = fakeAnchor.transform;
                for (int i = 0; i < fakeSkins.Length; i++)
                {
                    var skin = fakeSkins[i];
                    if (skin == null || skin.sharedMesh == null) continue;
                    var mesh = Rent();
                    skin.BakeMesh(mesh, true);
                    // The template lies at the origin: its local pose relative to the anchor moved to the knock point.
                    var local = anchor.worldToLocalMatrix * Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                    slot.Parts.Add(new Part { Mesh = mesh, Pooled = true, Matrix = pose * local, SubMeshes = mesh.subMeshCount });
                }
                Begin(slot, FakeRunnerId, duration, RunnerRim);
            }
            catch (Exception exception) { DirectionHints.ReportFault("SourceSnapshots.ShowFakeRunner", exception); }
        }

        // An inactive copy of a Runner model in its idle pose (scripts never run on an inactive object).
        static bool FakeRunnerReady()
        {
            if (fakeSkins != null) return fakeSkins.Length > 0;
            CharacterAsset asset = null;
            var all = NetworkPlayer.All;
            for (int i = 0; i < all.Count && asset == null; i++)
            {
                var player = all[i];
                if (player == null || player.Role != PlayerRole.Runner) continue;
                var body = player.GetComponent<CharacterBody>();
                if (body != null && body.Definition != null && body.Definition.visualPrefab != null) asset = body.Definition;
            }
            if (asset == null) return false;
            fakeAnchor = new GameObject("Source Snapshot Runner Template");
            fakeAnchor.SetActive(false);
            DontDestroyOnLoad(fakeAnchor);
            var model = Instantiate(asset.visualPrefab, fakeAnchor.transform, false);
            model.transform.localPosition = asset.modelOffset;
            model.transform.localScale = asset.modelScale;
            if (asset.idleClip != null) asset.idleClip.SampleAnimation(model, 0f);
            fakeSkins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            return fakeSkins.Length > 0;
        }

        static void Add(Snapshot slot, Renderer renderer)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return;
            if (renderer is SkinnedMeshRenderer skin)
            {
                if (skin.sharedMesh == null) return;
                var mesh = Rent();
                skin.BakeMesh(mesh, true);
                var t = skin.transform;
                slot.Parts.Add(new Part { Mesh = mesh, Pooled = true, Matrix = Matrix4x4.TRS(t.position, t.rotation, Vector3.one), SubMeshes = mesh.subMeshCount });
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                slot.Parts.Add(new Part { Mesh = filter.sharedMesh, Pooled = false, Matrix = renderer.localToWorldMatrix, SubMeshes = filter.sharedMesh.subMeshCount });
        }

        static void Begin(Snapshot slot, int playerId, float duration, Color color)
        {
            if (slot.Parts.Count == 0) { Release(slot); return; }
            slot.Live = true;
            slot.PlayerId = playerId;
            slot.Start = Time.time;
            slot.Duration = duration;
            slot.Color = color;
            Shown++;
        }

        // D1 the same source's slot, else a free one, else (D2) the oldest.
        static Snapshot Take(int playerId)
        {
            Snapshot free = null, oldest = null;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot.Live && slot.PlayerId == playerId) { Replaced++; Release(slot); return slot; }
                if (!slot.Live) { free ??= slot; continue; }
                if (oldest == null || slot.Start < oldest.Start) oldest = slot;
            }
            if (free != null) return free;
            Evicted++;
            Release(oldest);
            return oldest;
        }

        static Mesh Rent()
        {
            if (pool.Count > 0) return pool.Pop();
            MeshesCreated++;
            return new Mesh { name = "Source Snapshot", hideFlags = HideFlags.DontSave };
        }

        static void Release(Snapshot slot)
        {
            for (int i = 0; i < slot.Parts.Count; i++)
                if (slot.Parts[i].Pooled && slot.Parts[i].Mesh != null) pool.Push(slot.Parts[i].Mesh);
            slot.Parts.Clear();
            slot.Live = false;
        }

        public static void Clear()
        {
            for (int i = 0; i < slots.Length; i++) if (slots[i].Live || slots[i].Parts.Count > 0) Release(slots[i]);
        }

        void LateUpdate()
        {
            try { Draw(); }
            catch (Exception exception)
            {
                DirectionHints.ReportFault("SourceSnapshots.LateUpdate", exception);
                Clear();
            }
        }

        static void Draw()
        {
            bool active = SurvivorBodyVisibility.Active();
            float now = Time.time;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (!slot.Live) continue;
                float age = now - slot.Start, t = age / slot.Duration;
                if (!active || t >= 1f) { Release(slot); continue; }
                // The ring's curve (RippleVisualSettings, WalkingFootRings sound rings).
                float alpha = RippleVisualSettings.MaxAlpha * Mathf.Clamp01(age / RippleVisualSettings.FadeInSeconds) *
                    Mathf.Pow(1f - Mathf.Clamp01(t), RippleVisualSettings.FadePower);
                var c = slot.Color;
                block.SetColor(ColorId, new Color(c.r, c.g, c.b, alpha));
                block.SetFloat(ShapeId, 7f);
                for (int p = 0; p < slot.Parts.Count; p++)
                {
                    var part = slot.Parts[p];
                    if (part.Mesh == null) continue;
                    for (int s = 0; s < part.SubMeshes; s++)
                        Graphics.DrawMesh(part.Mesh, part.Matrix, material, 0, null, s, block, ShadowCastingMode.Off, false);
                }
            }
        }

        void OnDestroy()
        {
            if (instance != this) return;
            Clear();
            while (pool.Count > 0) { var mesh = pool.Pop(); if (mesh != null) Destroy(mesh); }
            instance = null;
        }
    }
}
