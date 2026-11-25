using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Tests
{
    // VB B-1 파문 렌더링 결함 점검: the real ring shader (Marco/RippleRing) and the real strip writer (RippleMesh, reached by
    // reflection — it is internal) rendered off screen with fixed camera matrices, so every candidate defect can be put
    // under exactly the condition that shows it (distance, grazing angle, resolution, sub-pixel motion, overlap, afterglow
    // alpha, a floor at the same height). Nothing here changes a ring value: the parameters are the game's own
    // (RippleVisualSettings). Gamma colour space, no tonemapping (project settings): a display value is clamp01(v) × 255.
    // With the environment variable MARCO_RIPPLE_CAPTURE set to a folder, the renders and a metrics CSV are written there.
    // Needs a graphics device (run EditMode without -nographics); with none the tests are ignored, not passed.
    public class RippleRenderInspectionTests
    {
        static readonly Assembly Net = typeof(Marco.Network.Vision.WalkBobRules).Assembly;
        static readonly Type MeshT = Net.GetType("Marco.Network.Vision.RippleMesh", true);
        static readonly Type ShapeT = Net.GetType("Marco.Network.Vision.RippleShape", true);
        static readonly Type SettingsT = Net.GetType("Marco.Network.Vision.RippleVisualSettings", true);
        static readonly StringBuilder Csv = new();
        static string CaptureDir => Environment.GetEnvironmentVariable("MARCO_RIPPLE_CAPTURE");

        static T S<T>(string name)
        {
            var f = SettingsT.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f != null) return (T)f.GetValue(null);
            return (T)SettingsT.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        }

        // ── one ring ──────────────────────────────────────────────────────────────────────────────────────────────

        public sealed class Ring
        {
            public float Radius, Front, Alpha = .85f, Core = .05f, Trail = .35f; public Color Color = new(.91f, .91f, .91f);
            public bool Double, Floor; public float Mode; public float WallStopAt = -1f; public Vector3 Centre;
        }

        static Mesh Build(Ring r, out MaterialPropertyBlock block)
        {
            object shape = Activator.CreateInstance(ShapeT, true);
            ShapeT.GetMethod("Reset").Invoke(shape, new object[] { r.Radius, 9 });
            int rays = 24;
            ShapeT.GetField("RayCount").SetValue(shape, rays);
            var angle = (float[])ShapeT.GetField("Angle").GetValue(shape);
            var reach = (float[])ShapeT.GetField("Reach").GetValue(shape);
            var wall = (bool[])ShapeT.GetField("Wall").GetValue(shape);
            for (int i = 0; i < rays; i++)
            {
                angle[i] = i * Mathf.PI * 2f / rays;
                reach[i] = r.WallStopAt > 0f ? r.WallStopAt : r.Radius + 10f;
                wall[i] = r.WallStopAt > 0f;
            }
            var mesh = new Mesh();
            float inner = r.Floor ? S<float>("FloorCoreWidth") + S<float>("FloorTrailWidth") : S<float>("CoreWidth") + S<float>("TrailWidth");
            MeshT.GetMethod("Write", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { mesh, shape, inner });
            block = new MaterialPropertyBlock();
            MeshT.GetMethod("ApplyShape", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { block, shape, r.Double });
            block.SetFloat("_Mode", r.Mode);
            MeshT.GetMethod("ApplyFrame", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { block, r.Color, r.Alpha, r.Front, r.Core, r.Trail });
            return mesh;
        }

        // ── one view ──────────────────────────────────────────────────────────────────────────────────────────────

        public sealed class View
        {
            public int Width = 1920, Height = 1080; public Vector3 Eye = new(0, 1.6f, -8f); public Vector3 Look = Vector3.zero;
            public float Fov = 60f, Near = .3f; public bool FloorPlane; public float FloorY;
            public Matrix4x4 V, P;
            public void Matrices()
            {
                var rot = Quaternion.LookRotation(Look - Eye, Vector3.up);
                V = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(Eye, rot, Vector3.one).inverse;
                P = Matrix4x4.Perspective(Fov, Width / (float)Height, Near, 500f);
            }
            // Screen pixel (x right, y up from the bottom) of a world point; false behind the camera.
            public bool Project(Vector3 w, out Vector2 px)
            {
                var c = P * (V * new Vector4(w.x, w.y, w.z, 1f));
                px = default;
                if (c.w <= 1e-4f) return false;
                px = new Vector2((c.x / c.w * .5f + .5f) * Width, (c.y / c.w * .5f + .5f) * Height);
                return true;
            }
        }

        static Shader ringShader;
        static Material ringMat, floorMat;
        static Mesh quad;

        static void Setup()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no graphics device (-nographics)");
            ringShader ??= Resources.Load<Shader>("MarcoRipple/RippleRing");
            Assert.IsNotNull(ringShader); Assert.IsTrue(ringShader.isSupported);
            ringMat ??= new Material(ringShader);
            if (floorMat == null)
            {
                var unlit = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.IsNotNull(unlit);
                floorMat = new Material(unlit); floorMat.SetColor("_BaseColor", new Color(.02f, .02f, .02f, 1f));
            }
            if (quad == null)
            {
                quad = new Mesh();
                quad.vertices = new[] { new Vector3(-200, 0, -200), new Vector3(200, 0, -200), new Vector3(-200, 0, 200), new Vector3(200, 0, 200) };
                quad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                quad.RecalculateBounds();
            }
        }

        // Linear HDR values (RGBA float), row 0 = bottom. A real Camera through the URP render request (as the game draws
        // it), so every built-in the shader reads (_ScreenParams, matrices) is the engine's own.
        static Color[] Render(View v, IList<Ring> rings)
        {
            Setup();
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(v.Width, v.Height, RenderTextureFormat.ARGBHalf, 24) { sRGB = false });
            var objects = new List<UnityEngine.Object>();
            try
            {
                var camGo = new GameObject("inspection camera"); objects.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.transform.SetPositionAndRotation(v.Eye, Quaternion.LookRotation(v.Look - v.Eye, Vector3.up));
                cam.fieldOfView = v.Fov; cam.nearClipPlane = v.Near; cam.farClipPlane = 500f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.allowHDR = true; cam.allowMSAA = false;
                cam.targetTexture = rt; cam.aspect = v.Width / (float)v.Height;
                v.V = cam.worldToCameraMatrix; v.P = cam.projectionMatrix;
                if (v.FloorPlane)
                {
                    var f = new GameObject("floor"); objects.Add(f);
                    f.AddComponent<MeshFilter>().sharedMesh = quad; f.AddComponent<MeshRenderer>().sharedMaterial = floorMat;
                    f.transform.position = new Vector3(0, v.FloorY, 0);
                }
                foreach (var r in rings)
                {
                    var mesh = Build(r, out var block); objects.Add(mesh);
                    var go = new GameObject("ring"); objects.Add(go);
                    go.transform.position = r.Centre;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = ringMat; mr.SetPropertyBlock(block);
                }
                SubmitSingleCamera(cam, rt);
                var read = new Texture2D(v.Width, v.Height, TextureFormat.RGBAFloat, false, true);
                var prev = RenderTexture.active; RenderTexture.active = rt;
                read.ReadPixels(new Rect(0, 0, v.Width, v.Height), 0, 0); read.Apply();
                RenderTexture.active = prev;
                var px = read.GetPixels();
                UnityEngine.Object.DestroyImmediate(read);
                // Some platforms flip Y for render textures: put the image the way View.Project expects it.
                if (Flipped(v, rings, px)) px = FlipY(px, v.Width, v.Height);
                return px;
            }
            finally
            {
                foreach (var o in objects) if (o != null) UnityEngine.Object.DestroyImmediate(o);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }),
        // by reflection (this test assembly does not reference URP).
        static void SubmitSingleCamera(Camera cam, RenderTexture rt)
        {
            var requestType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipeline+SingleCameraRequest, Unity.RenderPipelines.Universal.Runtime", true);
            object request = Activator.CreateInstance(requestType);
            requestType.GetField("destination").SetValue(request, rt);
            MethodInfo submit = null;
            foreach (var m in typeof(RenderPipeline).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "SubmitRenderRequest" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2) submit = m;
            Assert.IsNotNull(submit, "RenderPipeline.SubmitRenderRequest");
            submit.MakeGenericMethod(requestType).Invoke(null, new[] { cam, request });
        }

        static Color[] FlipY(Color[] px, int w, int h)
        {
            var o = new Color[px.Length];
            for (int y = 0; y < h; y++) Array.Copy(px, y * w, o, (h - 1 - y) * w, w);
            return o;
        }

        static bool Flipped(View v, IList<Ring> rings, Color[] px)
        {
            if (rings.Count == 0) return false;
            float a = 0, b = 0;
            foreach (var p in RingPath(v, rings[0], 180))
            {
                int x = Mathf.Clamp((int)p.x, 0, v.Width - 1), y = Mathf.Clamp((int)p.y, 0, v.Height - 1);
                a += Peak(px, v, x, y, 3); b += Peak(px, v, x, v.Height - 1 - y, 3);
            }
            return b > a * 1.5f;
        }

        // Screen points along the drawn line (the wavefront, or the wall stop) at `count` angles, visible ones only.
        static List<Vector2> RingPath(View v, Ring r, int count)
        {
            var list = new List<Vector2>();
            float at = r.Mode > 1.5f ? r.WallStopAt : Mathf.Min(r.Front, r.WallStopAt > 0 ? r.WallStopAt : float.MaxValue);
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var w = r.Centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * at;
                if (v.Project(w, out var p) && p.x >= 2 && p.y >= 2 && p.x < v.Width - 2 && p.y < v.Height - 2) list.Add(p);
            }
            return list;
        }

        static float Lum(Color c) => Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        static float Display(float v) => Mathf.Round(Mathf.Clamp01(v) * 255f);

        static float Peak(Color[] px, View v, int x, int y, int r)
        {
            float m = 0;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx < 0 || yy < 0 || xx >= v.Width || yy >= v.Height) continue;
                    m = Mathf.Max(m, Lum(px[yy * v.Width + xx]));
                }
            return m;
        }

        static void Save(string name, Color[] px, View v)
        {
            if (string.IsNullOrEmpty(CaptureDir)) return;
            Directory.CreateDirectory(CaptureDir);
            var t = new Texture2D(v.Width, v.Height, TextureFormat.RGBA32, false);
            var o = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                o[i] = new Color32((byte)Display(c.r), (byte)Display(c.g), (byte)Display(c.b), 255);
            }
            t.SetPixels32(o); t.Apply();
            File.WriteAllBytes(Path.Combine(CaptureDir, name + ".png"), t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
        }

        static void Metric(string test, string key, float value)
        {
            Csv.AppendLine($"{test},{key},{value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrEmpty(CaptureDir)) { Directory.CreateDirectory(CaptureDir); File.WriteAllText(Path.Combine(CaptureDir, "metrics.csv"), "test,key,value\n" + Csv); }
            Debug.Log($"[RippleInspect] {test} {key} = {value}");
        }

        // The three screens: environment (Echo sonar), Runner cyan (own ring at 35 % alpha, half width), Seeker red double edge.
        static IEnumerable<(string name, Ring ring)> Looks(float radius, float front)
        {
            yield return ("echo", new Ring { Radius = radius, Front = front, Color = S<Color>("EnvironmentColor") });
            yield return ("runner_self", new Ring { Radius = radius, Front = front, Color = S<Color>("RunnerColor"), Alpha = .85f * .35f, Core = .025f, Trail = .175f });
            yield return ("seeker", new Ring { Radius = radius, Front = front, Color = S<Color>("SeekerColor"), Double = true });
        }

        // ── 1. stair steps along the line, at several distances / grazing angles ─────────────────────────────────

        static float Roughness(Color[] px, View v, Ring r)
        {
            // Integrated brightness across the line at each path point (3-pixel neighbourhood); a stair-stepped line
            // alternates between thin and thick crossings, a covered (anti-aliased) one stays even.
            var path = RingPath(v, r, 1440);
            var s = new List<float>();
            foreach (var p in path)
            {
                float sum = 0;
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = (int)p.x + dx, y = (int)p.y + dy;
                        if (x < 0 || y < 0 || x >= v.Width || y >= v.Height) continue;
                        sum += Lum(px[y * v.Width + x]);
                    }
                s.Add(sum);
            }
            if (s.Count < 10) return float.NaN;
            float rough = 0, mean = 0;
            for (int i = 1; i < s.Count - 1; i++) { rough += Mathf.Abs(s[i] - (s[i - 1] + s[i + 1]) * .5f); mean += s[i]; }
            return mean > 0 ? rough / mean : float.NaN;
        }

        [Test]
        public void StairSteps_AlongTheLine()
        {
            foreach (float dist in new[] { 6f, 15f, 30f })
                foreach (var (name, ring) in Looks(5f, 5f))
                {
                    ring.Trail = 0f; // the core alone shows the edge
                    var v = new View { Eye = new Vector3(0, 1.6f, -dist), Look = new Vector3(0, 0, 0) };
                    var px = Render(v, new[] { ring });
                    float r = Roughness(px, v, ring);
                    Metric("stairs", $"{name}_d{dist}", r);
                    Save($"B1_stairs_{name}_d{dist}", px, v);
                    Assert.IsFalse(float.IsNaN(r), "the line was found");
                    Assert.Less(r, .05f, $"{name} at {dist} m: an anti-aliased line, no stair steps");
                }
        }

        // ── 2. shimmer while moving: sub-pixel camera motion and slow expansion ───────────────────────────────────

        [Test]
        public void Shimmer_WhileMoving()
        {
            foreach (var (name, ring) in Looks(5f, 5f))
            {
                var totals = new List<float>(); var pixelFlicker = 0f;
                Color[] last = null;
                for (int f = 0; f < 12; f++)
                {
                    var v = new View { Width = 960, Height = 540, Eye = new Vector3(f * .0013f, 1.6f, -12f), Look = new Vector3(f * .0013f, 0, 0) };
                    ring.Front = 5f - f * .004f;
                    var px = Render(v, new[] { ring });
                    float t = 0; foreach (var c in px) t += Lum(c); totals.Add(t);
                    if (last != null)
                    {
                        // brightest single-pixel change in the line (a shimmering stair step shows up as one jumping pixel)
                        float m = 0; for (int i = 0; i < px.Length; i++) m = Mathf.Max(m, Mathf.Abs(Lum(px[i]) - Lum(last[i])));
                        pixelFlicker = Mathf.Max(pixelFlicker, m);
                    }
                    last = px;
                }
                float mean = 0; foreach (var t in totals) mean += t; mean /= totals.Count;
                float sd = 0; foreach (var t in totals) sd += (t - mean) * (t - mean); sd = Mathf.Sqrt(sd / totals.Count);
                Metric("shimmer", $"{name}_total_cv", sd / mean);
                Assert.Less(sd / mean, .01f, $"{name}: no shimmer while moving");
                Metric("shimmer", $"{name}_max_pixel_step", pixelFlicker);
            }
        }

        // ── 3. far away: the line must not break up ──────────────────────────────────────────────────────────────

        [Test]
        public void FarAway_NoGaps()
        {
            foreach (float dist in new[] { 20f, 40f, 60f })
                foreach (var (name, ring) in Looks(5f, 5f))
                {
                    var v = new View { Eye = new Vector3(0, 1.6f, -dist), Look = new Vector3(0, 0, 0) };
                    var px = Render(v, new[] { ring });
                    var path = RingPath(v, ring, 720);
                    int lit = 0; float min = float.MaxValue;
                    foreach (var p in path) { float pk = Peak(px, v, (int)p.x, (int)p.y, 2); if (Display(pk) >= 2) lit++; min = Mathf.Min(min, pk); }
                    Metric("far", $"{name}_d{dist}_lit_fraction", path.Count > 0 ? lit / (float)path.Count : 0);
                    // [VB B-1 fixed] far away at a grazing angle the line broke into dashes (60 m: 82 – 94 % lit)
                    Assert.GreaterOrEqual(lit / (float)Mathf.Max(1, path.Count), .99f, $"{name} at {dist} m: the line holds together");
                    Metric("far", $"{name}_d{dist}_min_display", Display(min));
                    Save($"B1_far_{name}_d{dist}", px, v);
                }
        }

        // ── 4. thickness against resolution ─────────────────────────────────────────────────────────────────────

        static float ThicknessAtNearPoint(Color[] px, View v, Ring r)
        {
            // the near arc's crossing on the centre column: count rows over half the column's peak
            var w = r.Centre + new Vector3(0, 0, -r.Front);
            v.Project(w, out var p);
            int x = Mathf.Clamp((int)p.x, 0, v.Width - 1);
            float peak = 0; int y0 = Mathf.Clamp((int)p.y - 60, 0, v.Height - 1), y1 = Mathf.Clamp((int)p.y + 60, 0, v.Height - 1);
            for (int y = y0; y <= y1; y++) peak = Mathf.Max(peak, Lum(px[y * v.Width + x]));
            int rows = 0; for (int y = y0; y <= y1; y++) if (Lum(px[y * v.Width + x]) >= peak * .5f) rows++;
            return rows;
        }

        [Test]
        public void Thickness_AcrossResolutions()
        {
            foreach (var (name, ring) in Looks(5f, 5f))
                foreach (float dist in new[] { 8f, 40f })
                    foreach (var (w, h) in new[] { (960, 540), (1920, 1080), (3840, 2160) })
                    {
                        ring.Trail = 0f;
                        var v = new View { Width = w, Height = h, Eye = new Vector3(0, 1.6f, -dist), Look = Vector3.zero };
                        var px = Render(v, new[] { ring });
                        float rows = ThicknessAtNearPoint(px, v, ring);
                        Metric("thickness", $"{name}_d{dist}_{h}p_px", rows);
                        Metric("thickness", $"{name}_d{dist}_{h}p_fraction", rows / h);
                    }
        }

        // ── 5. overlapping rings (up to 8): how much is clamped at the display ───────────────────────────────────

        [Test]
        public void Overlap_Saturation()
        {
            foreach (var (name, baseRing) in Looks(5f, 4.6f))
                foreach (int n in new[] { 1, 2, 3, 8 })
                {
                    var rings = new List<Ring>();
                    for (int i = 0; i < n; i++)
                        rings.Add(new Ring { Radius = 5f, Front = 4.6f - i * .02f, Color = baseRing.Color, Alpha = baseRing.Alpha, Core = baseRing.Core, Trail = baseRing.Trail, Double = baseRing.Double });
                    var v = new View { Width = 960, Height = 540, Eye = new Vector3(0, 6f, -6f), Look = Vector3.zero };
                    var px = Render(v, rings);
                    int lit = 0, clamped = 0; float maxV = 0;
                    foreach (var c in px) { float l = Lum(c); if (l > .01f) { lit++; if (l >= 1f) clamped++; } maxV = Mathf.Max(maxV, l); }
                    Metric("overlap", $"{name}_n{n}_clamped_fraction", lit > 0 ? clamped / (float)lit : 0);
                    Metric("overlap", $"{name}_n{n}_max_linear", maxV);
                    Save($"B1_overlap_{name}_n{n}", px, v);
                }
        }

        // ── 6. afterglow: 15 % → 0 % over 12 s, display steps per frame ──────────────────────────────────────────

        [Test]
        public void Afterglow_Steps()
        {
            var v = new View { Width = 960, Height = 540, Eye = new Vector3(0, 1.6f, -6f), Look = Vector3.zero };
            float alpha0 = S<float>("AfterglowAlpha"), seconds = S<float>("AfterglowSeconds"), fadeIn = S<float>("AfterglowFadeIn");
            var codes = new List<float>(); float lastCode = -1; int ups = 0, maxJump = 0;
            for (float age = 0; age < seconds; age += .25f)
            {
                float a = alpha0 * Mathf.Clamp01(age / fadeIn) * (1f - age / seconds);
                var ring = new Ring { Radius = 6f, Front = 1e4f, WallStopAt = 4f, Mode = 2f, Alpha = a, Core = S<float>("CoreWidth"), Trail = 0f, Color = S<Color>("EnvironmentColor") };
                var px = Render(v, new[] { ring });
                float pk = 0; foreach (var p in RingPath(v, ring, 360)) pk = Mathf.Max(pk, Peak(px, v, (int)p.x, (int)p.y, 2));
                float code = Display(pk); codes.Add(code);
                if (lastCode >= 0 && age - .25f > fadeIn + .01f) { if (code > lastCode) ups++; maxJump = Mathf.Max(maxJump, (int)Mathf.Abs(code - lastCode)); }
                lastCode = code;
                if (Mathf.Abs(age - 1f) < .01f) Save("B1_afterglow_1s", px, v);
            }
            var distinct = new HashSet<float>(codes);
            Metric("afterglow", "start_code_after_fadein", codes[Mathf.CeilToInt(fadeIn / .25f) + 1]);
            Metric("afterglow", "distinct_codes", distinct.Count);
            Metric("afterglow", "max_code_jump_per_0.25s", maxJump);
            Metric("afterglow", "upward_steps_after_fadein", ups);
            Assert.AreEqual(0, ups, "the afterglow only ever dims"); Assert.LessOrEqual(maxJump, 1, "one display step at a time — no banding jumps");
        }

        // ── 7. a floor at the same height: no Z fighting at any distance ────────────────────────────────────────

        [Test]
        public void SameHeightFloor_NoZFighting()
        {
            var looks = new List<(string, Ring)>(Looks(5f, 5f))
            {
                // which property of the own ring matters over a floor: its colour, or its alpha / width
                ("runner_colour_full", new Ring { Radius = 5f, Front = 5f, Color = S<Color>("RunnerColor") }),
                ("echo_colour_self", new Ring { Radius = 5f, Front = 5f, Color = S<Color>("EnvironmentColor"), Alpha = .85f * .35f, Core = .025f, Trail = .175f }),
            };
            foreach (float dist in new[] { 3f, 10f, 30f, 60f })
                foreach (var (name, ring) in looks)
                {
                    var v = new View { Width = 960, Height = 540, Eye = new Vector3(0, 1.6f, -dist), Look = Vector3.zero };
                    var bare = Render(v, new[] { ring });
                    v.FloorPlane = true;
                    var withFloor = Render(v, new[] { ring });
                    var floorOnly = Render(v, new Ring[0]);
                    // the ring's own light over the floor, against the ring alone (1 = nothing hidden by the floor's depth)
                    float a = 0, b = 0;
                    for (int i = 0; i < bare.Length; i++)
                    {
                        var c = bare[i]; a += c.r + c.g + c.b;
                        var w = withFloor[i]; var f = floorOnly[i];
                        b += Mathf.Max(0, w.r - f.r) + Mathf.Max(0, w.g - f.g) + Mathf.Max(0, w.b - f.b);
                    }
                    Metric("zfight", $"{name}_d{dist}_kept", a > 0 ? b / a : 0);
                    Assert.GreaterOrEqual(b / a, .99f, $"{name} at {dist} m: nothing lost to a floor at the same height");
                    if (dist == 30f) Save($"B1_zfight_{name}_d{dist}", withFloor, v);
                }
        }

        // ── 8. near the camera: the Echo eye 0.4 m above the floor, a ring passing under it ─────────────────────

        [Test]
        public void NearTheCamera()
        {
            foreach (var (name, ring) in Looks(3f, 2.5f))
            {
                var v = new View { Width = 960, Height = 540, Eye = new Vector3(0, .4f, -2.0f), Look = new Vector3(0, 0, 0) };
                var px = Render(v, new[] { ring });
                var path = RingPath(v, ring, 720); int lit = 0;
                foreach (var p in path) if (Display(Peak(px, v, (int)p.x, (int)p.y, 2)) >= 2) lit++;
                Metric("near", $"{name}_visible_path_points", path.Count);
                Metric("near", $"{name}_lit_fraction", path.Count > 0 ? lit / (float)path.Count : 0);
                Assert.GreaterOrEqual(lit / (float)Mathf.Max(1, path.Count), .99f);
                Save($"B1_near_{name}", px, v);
            }
        }

        // ── 9. end of the expansion: the alpha at the last frames (C# curve) ─────────────────────────────────────

        [Test]
        public void EndOfExpansion_NoPop()
        {
            float max = S<float>("MaxAlpha"), power = S<float>("FadePower");
            foreach (float lifetime in new[] { .4f, 1.2f, 2.5f })
                foreach (float fps in new[] { 30f, 60f, 144f })
                {
                    float t = 1f - (1f / fps) / lifetime;
                    float last = max * Mathf.Pow(1f - t, power);
                    Metric("end", $"life{lifetime}_fps{fps}_last_alpha_x255", last * 255f * .91f);
                }
        }
    }
}
