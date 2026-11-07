using System;
using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Vision
{
    // Screen-edge drawing for DirectionHints (§5.4). One screen-space overlay Canvas made at runtime — no prefab, scene
    // or asset — with 8 quads per channel driven straight through CanvasRenderer (engine module; the Marco.Network
    // assembly does not reference UGUI) and the built-in UI/Default shader (always included). The OnGUI HUD (breath
    // gauge, panels) renders after every canvas, so the hint always sits under it. The canvas is off while nothing is lit.
    public sealed class DirectionHintOverlay : MonoBehaviour
    {
        public const string ObjectName = "Direction Hint Overlay";
        public const int SortingOrder = -100;   // under any other overlay canvas
        static DirectionHintOverlay instance;
        // §3.4 Seeker direction gauge: add its channel here (it shares the edge, the band and the fades).
        static readonly DirectionHints.Channel[] channels = { DirectionHints.Hint };

        Canvas canvas;
        RectTransform[] rects;
        CanvasRenderer[] renderers;
        Mesh mesh;
        Material material;
        Texture2D texture;
        NetworkPlayer viewOwner;
        Transform viewTransform;

        public static void Ensure()
        {
            if (!ReferenceEquals(instance, null)) return;
            try
            {
                var go = new GameObject(ObjectName);
                DontDestroyOnLoad(go);
                instance = go.AddComponent<DirectionHintOverlay>();
            }
            catch (Exception exception) { DirectionHints.ReportFault("DirectionHintOverlay.Ensure", exception); }
        }

        void Awake()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.pixelPerfect = false;
            texture = BuildTexture();
            mesh = BuildQuad();
            material = new Material(Shader.Find("UI/Default")) { name = "Direction Hint", hideFlags = HideFlags.DontSave };
            int count = channels.Length * 8;
            rects = new RectTransform[count];
            renderers = new CanvasRenderer[count];
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject("Hint " + i, typeof(RectTransform), typeof(CanvasRenderer));
                child.transform.SetParent(transform, false);
                var rect = (RectTransform)child.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = Vector2.one;
                rects[i] = rect;
                renderers[i] = child.GetComponent<CanvasRenderer>();
            }
            Apply();
            canvas.enabled = false;
        }

        // Geometry, material and colour of every quad (again whenever the canvas turns back on).
        void Apply()
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                renderer.SetMesh(mesh);
                renderer.materialCount = 1;
                renderer.SetMaterial(material, 0);
                renderer.SetTexture(texture);
                var color = channels[i / 8].Color;
                renderer.SetColor(new Color(color.r, color.g, color.b, 1f));
                renderer.SetAlpha(0f);
            }
        }

        void Update()
        {
            try { Draw(); }
            catch (Exception exception)
            {
                DirectionHints.ReportFault("DirectionHintOverlay.Update", exception);
                if (canvas != null) canvas.enabled = false;
            }
        }

        void Draw()
        {
            float now = Time.time;
            var local = NetworkPlayer.Local;
            if (local != null) DirectionHints.Sample(local.transform.position, now);
            if (!RippleClientFeatures.DirectionHint || !DirectionHints.CanShowNow())
            {
                for (int c = 0; c < channels.Length; c++) channels[c].Clear();
                if (canvas.enabled) canvas.enabled = false;
                return;
            }
            bool any = false;
            for (int c = 0; c < channels.Length; c++) any |= channels[c].Any(now);
            if (!any)
            {
                if (canvas.enabled) canvas.enabled = false;
                return;
            }
            if (!canvas.enabled)
            {
                canvas.enabled = true;
                Apply();
            }
            float viewYaw = ViewYaw(local);
            float width = Screen.width, height = Screen.height;
            for (int c = 0; c < channels.Length; c++)
            {
                var channel = channels[c];
                for (int octant = 0; octant < 8; octant++)
                {
                    int index = c * 8 + octant;
                    float alpha = DirectionHintRules.Alpha(channel.Start[octant], channel.HoldUntil[octant], now);
                    if (alpha <= 0f) { renderers[index].SetAlpha(0f); continue; }
                    DirectionHintRules.Band(DirectionHintRules.RelativeDegrees(octant, viewYaw), width, height,
                        out var center, out float rotation, out var size);
                    var rect = rects[index];
                    rect.anchoredPosition = center;
                    rect.localEulerAngles = new Vector3(0f, 0f, rotation);
                    rect.localScale = new Vector3(size.x, size.y, 1f);
                    renderers[index].SetAlpha(alpha);
                }
            }
        }

        // The local player's view (its own camera; the body when no camera is found).
        float ViewYaw(NetworkPlayer local)
        {
            if (local == null) return 0f;
            if (viewOwner != local || viewTransform == null)
            {
                viewOwner = local;
                var view = local.GetComponentInChildren<Camera>(true);
                viewTransform = view != null ? view.transform : local.transform;
            }
            return viewTransform.eulerAngles.y;
        }

        // Soft band: brightest at the screen edge (v = 1), gone inward (v = 0); both ends fade.
        static Texture2D BuildTexture()
        {
            const int w = 128, h = 32;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "Direction Hint Band", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + .5f) / w, v = (y + .5f) / h;
                    float ends = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / .25f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - u) / .25f));
                    pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * ends * v * v));
                }
            t.SetPixels32(pixels);
            t.Apply(false, true);
            return t;
        }

        // Unit quad; +Y (uv v = 1) is the side that faces the screen edge.
        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "Direction Hint Quad", hideFlags = HideFlags.DontSave };
            m.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.colors32 = new[] { new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return m;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            if (mesh != null) Destroy(mesh);
            if (material != null) Destroy(material);
            if (texture != null) Destroy(texture);
        }
    }
}
