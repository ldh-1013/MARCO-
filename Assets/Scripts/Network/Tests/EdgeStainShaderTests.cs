using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Tests
{
    // V28a A-0: the EdgeStain shader itself, rendered into a float render texture and read back (not the C# reference).
    // Needs a graphics device — run EditMode without -nographics; with no device the test is ignored, not passed.
    public class EdgeStainShaderTests
    {
        private static Texture2D Render(float aspect, float seed, float drift, int height = 216, float cornerDepth = .15f, float edgeDepth = .06f,
            Color? colour = null, float alpha = 1f)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no graphics device (-nographics)");
            var shader = Resources.Load<Shader>("MarcoPresentation/EdgeStain");
            Assert.IsNotNull(shader, "Resources/MarcoPresentation/EdgeStain");
            Assert.IsTrue(shader.isSupported, "shader compiles on this device");
            int width = Mathf.RoundToInt(height * aspect);
            var material = new Material(shader);
            material.SetColor("_Color", colour ?? Color.white);
            material.SetFloat("_Alpha", alpha);
            material.SetFloat("_Seed", seed);
            material.SetFloat("_Drift", drift);
            material.SetFloat("_Aspect", aspect);
            material.SetFloat("_CornerDepth", cornerDepth);
            material.SetFloat("_EdgeDepth", edgeDepth);
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            Graphics.Blit(null, target, material);
            RenderTexture.active = target;
            var read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(material);
            return read;
        }

        [TestCase(16f / 9f)]
        [TestCase(21f / 9f)]
        [TestCase(4f / 3f)]
        public void CentreSeventyPercent_IsZero_AndDepthStaysWithinTheLimit(float aspect)
        {
            foreach (float seed in new[] { 0f, 7.3f, 41.9f })
            {
                var image = Render(aspect, seed, .3f);
                int w = image.width, h = image.height, lit = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float a = image.GetPixel(x, y).a;
                        if (a <= 0f) continue;
                        lit++;
                        float u = (x + .5f) / w, v = (y + .5f) / h;
                        Assert.IsFalse(Mathf.Abs(u - .5f) < .35f && Mathf.Abs(v - .5f) < .35f, $"centre 70 % lit at ({u:F3}, {v:F3}) a={a}");
                        // depth (short-side units) never beyond 15 %
                        float dx = Mathf.Min(u, 1f - u) * aspect / Mathf.Min(aspect, 1f), dy = Mathf.Min(v, 1f - v) / Mathf.Min(aspect, 1f);
                        Assert.LessOrEqual(Mathf.Min(dx, dy), .15f + 1.5f / h, $"deeper than 15 % at ({u:F3}, {v:F3})");
                        // middle of the top / bottom side: never beyond 6 % (+ one pixel)
                        if (Mathf.Abs(u - .5f) < .5f / w + 1e-4f) Assert.LessOrEqual(dy, .06f + 1.5f / h, $"edge middle deeper than 6 % at v={v:F3}");
                    }
                Assert.Greater(lit, 0, "the stain draws something at the edges");
                Object.DestroyImmediate(image);
            }
        }

        // 11-17 후속 5: the 1.5 state is drawn with its own depth — corner 9 %, edge middle 3.5 % — and the centre stays empty.
        [TestCase(16f / 9f)]
        [TestCase(21f / 9f)]
        public void RecoveringDepth_CornerNinePercent_EdgeMiddleThreeAndAHalf(float aspect)
        {
            const float Corner = .09f, Edge = .035f;
            foreach (float seed in new[] { 0f, 7.3f, 41.9f })
            {
                var image = Render(aspect, seed, .3f, 216, Corner, Edge);
                int w = image.width, h = image.height, lit = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (image.GetPixel(x, y).a <= 0f) continue;
                        lit++;
                        float u = (x + .5f) / w, v = (y + .5f) / h;
                        Assert.IsFalse(Mathf.Abs(u - .5f) < .35f && Mathf.Abs(v - .5f) < .35f, $"centre lit at ({u:F3}, {v:F3})");
                        float dx = Mathf.Min(u, 1f - u) * aspect, dy = Mathf.Min(v, 1f - v);
                        Assert.LessOrEqual(Mathf.Min(dx, dy), Corner + 1.5f / h, $"deeper than 9 % at ({u:F3}, {v:F3})");
                        if (Mathf.Abs(u - .5f) < .5f / w + 1e-4f) Assert.LessOrEqual(dy, Edge + 1.5f / h, $"edge middle deeper than 3.5 % at v={v:F3}");
                    }
                Assert.Greater(lit, 0);
                Object.DestroyImmediate(image);
            }
        }

        // V31 B-1 스태미나 소진 수축 — the same shader in black at 0.12: centre 70 % alpha 0, corner ≤ 9 %, edge middle ≤ 3.5 %,
        // never darker than 0.12, colour black.
        [TestCase(16f / 9f)]
        [TestCase(21f / 9f)]
        [TestCase(4f / 3f)]
        public void ExhaustionContraction_Black_CentreZero_NinePercent(float aspect)
        {
            var image = Render(aspect, 23.7f, 0f, 216, Marco.Network.Vision.ExhaustionRules.CornerDepth, Marco.Network.Vision.ExhaustionRules.EdgeDepth,
                Marco.Network.Vision.ExhaustionRules.Colour, Marco.Network.Vision.ExhaustionRules.MaxAlpha);
            int w = image.width, h = image.height, lit = 0; float max = 0f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = image.GetPixel(x, y);
                    if (c.a <= 0f) continue;
                    lit++; max = Mathf.Max(max, c.a);
                    float u = (x + .5f) / w, v = (y + .5f) / h;
                    Assert.IsFalse(Mathf.Abs(u - .5f) < .35f && Mathf.Abs(v - .5f) < .35f, $"centre lit at ({u:F3}, {v:F3})");
                    float dx = Mathf.Min(u, 1f - u) * aspect / Mathf.Min(aspect, 1f), dy = Mathf.Min(v, 1f - v) / Mathf.Min(aspect, 1f);
                    Assert.LessOrEqual(Mathf.Min(dx, dy), .09f + 1.5f / h, $"deeper than 9 % at ({u:F3}, {v:F3})");
                    if (Mathf.Abs(u - .5f) < .5f / w + 1e-4f) Assert.LessOrEqual(dy, .035f + 1.5f / h, $"edge middle deeper than 3.5 % at v={v:F3}");
                    Assert.AreEqual(0f, c.r + c.g + c.b, 1e-5f, "black");
                }
            Assert.Greater(lit, 0, "draws at the edges");
            // the read-back alpha is the source alpha blended onto a cleared target (SrcAlpha OneMinusSrcAlpha → a²); either
            // way the source never exceeds 0.12 and reaches above 0.1 at the very edge
            Assert.LessOrEqual(max, .12f + 1e-4f, "never beyond 0.12");
            Assert.Greater(Mathf.Sqrt(max), .1f, "reaches about 0.12 at the very edge");
            Object.DestroyImmediate(image);
        }

        // VB B-4 8.1 억제 성공 (black 0.10, 9 % / 3.5 %) and 8.2 질식 at its peak (red 0.55, 22 % / 9 %): the same shader,
        // centre 70 % alpha 0, never deeper than asked, never more opaque than asked.
        [TestCase(16f / 9f, 0)]
        [TestCase(21f / 9f, 0)]
        [TestCase(16f / 9f, 1)]
        [TestCase(21f / 9f, 1)]
        [TestCase(4f / 3f, 1)]
        public void BreathEdges_CentreZero_DepthAndOpacityAsAsked(float aspect, int which)
        {
            bool suffocation = which == 1;
            float corner = suffocation ? Marco.Network.Vision.BreathFeelRules.SuffocationPeakDepth.x : Marco.Network.Vision.BreathFeelRules.SuppressCornerDepth;
            float edge = suffocation ? Marco.Network.Vision.BreathFeelRules.SuffocationPeakDepth.y : Marco.Network.Vision.BreathFeelRules.SuppressEdgeDepth;
            float alpha = suffocation ? Marco.Network.Vision.BreathFeelRules.SuffocationAlpha : Marco.Network.Vision.BreathFeelRules.SuppressMaxAlpha;
            Color colour = suffocation ? new Color32(0xFF, 0x3B, 0x4D, 0xFF) : Color.black;
            foreach (float seed in new[] { 0f, 23.7f, 41.9f })
            {
                var image = Render(aspect, seed, 0f, 216, corner, edge, colour, alpha);
                int w = image.width, h = image.height, lit = 0; float max = 0f;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = image.GetPixel(x, y);
                        if (c.a <= 0f) continue;
                        lit++; max = Mathf.Max(max, c.a);
                        float u = (x + .5f) / w, v = (y + .5f) / h;
                        Assert.IsFalse(Mathf.Abs(u - .5f) < .35f && Mathf.Abs(v - .5f) < .35f, $"centre lit at ({u:F3}, {v:F3})");
                        float dx = Mathf.Min(u, 1f - u) * aspect / Mathf.Min(aspect, 1f), dy = Mathf.Min(v, 1f - v) / Mathf.Min(aspect, 1f);
                        Assert.LessOrEqual(Mathf.Min(dx, dy), corner + 1.5f / h, $"deeper than {corner:P0} at ({u:F3}, {v:F3})");
                        if (Mathf.Abs(u - .5f) < .5f / w + 1e-4f) Assert.LessOrEqual(dy, edge + 1.5f / h, $"edge middle deeper than {edge:P1} at v={v:F3}");
                    }
                Assert.Greater(lit, 0);
                // read-back alpha is the source alpha blended on a cleared target (a² or a): never above what was asked
                Assert.LessOrEqual(max, alpha + 1e-4f);
                Object.DestroyImmediate(image);
            }
        }

        // V31 벽 통과 노이즈 — the same edge shape: centre 70 % alpha 0, depth ≤ 15 %.
        [TestCase(16f / 9f)]
        [TestCase(21f / 9f)]
        public void WallPassNoise_CentreUntouched(float aspect)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no graphics device (-nographics)");
            var shader = Resources.Load<Shader>("MarcoPresentation/WallPassNoise");
            Assert.IsNotNull(shader); Assert.IsTrue(shader.isSupported);
            const int h = 216; int w = Mathf.RoundToInt(h * aspect);
            foreach (float frame in new[] { 0f, 17f, 503f })
            {
                var material = new Material(shader);
                material.SetFloat("_Alpha", 1f); material.SetFloat("_Frame", frame); material.SetFloat("_Aspect", aspect);
                var target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                var previous = RenderTexture.active; RenderTexture.active = target;
                GL.Clear(true, true, new Color(0, 0, 0, 0));
                Graphics.Blit(null, target, material);
                var read = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
                read.ReadPixels(new Rect(0, 0, w, h), 0, 0); read.Apply();
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                int lit = 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        if (read.GetPixel(x, y).a <= 0f) continue;
                        lit++;
                        float u = (x + .5f) / w, v = (y + .5f) / h;
                        Assert.IsFalse(Mathf.Abs(u - .5f) < .35f && Mathf.Abs(v - .5f) < .35f, $"centre lit at ({u:F3}, {v:F3})");
                        float dx = Mathf.Min(u, 1f - u) * aspect, dy = Mathf.Min(v, 1f - v);
                        Assert.LessOrEqual(Mathf.Min(dx, dy), .15f + 1.5f / h, $"deeper than 15 % at ({u:F3}, {v:F3})");
                    }
                Assert.Greater(lit, 0);
                Object.DestroyImmediate(material); Object.DestroyImmediate(read);
            }
        }

        [Test]
        public void Corners_DeeperThanEdgeMiddles()
        {
            var image = Render(16f / 9f, 3f, 0f);
            int w = image.width, h = image.height;
            // straight in from the middle of the bottom edge vs. along the diagonal from the bottom-left corner
            int middle = 0; while (middle < h / 2 && image.GetPixel(w / 2, middle).a > 0f) middle++;
            int corner = 0; while (corner < h / 2 && image.GetPixel(corner, corner).a > 0f) corner++;
            TestContext.WriteLine($"lit depth: corner diagonal {corner}px (≈ {corner * 1.414f:F0}px from the corner), edge middle {middle}px (height {h})");
            Assert.Greater(corner, middle);
            Object.DestroyImmediate(image);
        }
    }
}
