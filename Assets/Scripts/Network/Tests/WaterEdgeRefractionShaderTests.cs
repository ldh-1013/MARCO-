using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Tests
{
    // V30 연출 2.2 잠수 가장자리 물 굴절 — the shader itself, rendered into a float render texture with a UV-coded stand-in for
    // _CameraOpaqueTexture (R = u, G = v), so each output pixel tells where it sampled from. Needs a graphics device.
    public class WaterEdgeRefractionShaderTests
    {
        [TestCase(16f / 9f)]
        [TestCase(21f / 9f)]
        public void CentreUntouched_DisplacementAtMostPointFourPercentOfTheWidth(float aspect)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("no graphics device (-nographics)");
            var shader = Resources.Load<Shader>("MarcoPresentation/WaterEdgeRefraction");
            Assert.IsNotNull(shader);
            Assert.IsTrue(shader.isSupported);
            const int h = 216;
            int w = Mathf.RoundToInt(h * aspect);
            var coded = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) pixels[y * w + x] = new Color((x + .5f) / w, (y + .5f) / h, 0f, 1f);
            coded.SetPixels(pixels); coded.Apply();
            var material = new Material(shader);
            material.SetFloat("_Aspect", aspect);
            Shader.SetGlobalTexture("_CameraOpaqueTexture", coded);
            var target = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, new Color(0, 0, 0, 0));
            Graphics.Blit(null, target, material);
            RenderTexture.active = target;
            var read = new Texture2D(w, h, TextureFormat.RGBAFloat, false, true);
            read.ReadPixels(new Rect(0, 0, w, h), 0, 0); read.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            float maxShift = 0f; int edgePixels = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = read.GetPixel(x, y);
                    float u = (x + .5f) / w, v = (y + .5f) / h;
                    if (Mathf.Abs(u - .5f) < .35f && Mathf.Abs(v - .5f) < .35f) { Assert.AreEqual(0f, c.a, 1e-5f, $"centre touched at ({u:F3}, {v:F3})"); continue; }
                    if (c.a < .999f) continue;   // only where the sample replaces the pixel fully (the very edge) is the colour the sample itself
                    edgePixels++;
                    // pixel distance between where this pixel is and where it sampled, in screen-width units
                    float shift = new Vector2((c.r - u) * w, (c.g - v) * h).magnitude / w;
                    maxShift = Mathf.Max(maxShift, shift);
                }
            TestContext.WriteLine($"aspect {aspect:F2}: edge pixels {edgePixels}, max displacement {maxShift * 100f:F3} % of the width");
            Assert.Greater(edgePixels, 0);
            Assert.LessOrEqual(maxShift, .004f + 1.5f / w, "≤ 0.4 % of the width (+ one texel)");
            Object.DestroyImmediate(material); Object.DestroyImmediate(coded); Object.DestroyImmediate(read);
        }
    }
}
