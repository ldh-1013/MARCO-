using System.Collections.Generic;
using Marco.Network.Escape;
using Marco.Network.Round;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Marco.Network.Editor
{
    // Batch-mode stand-in for the IMGUI layer of the render checks: renders a camera offscreen, then paints
    // what NetworkTestHud and EscapeeTransition would draw over it on the 1920x1080 reference canvas —
    // the round panel, the escapee banner (same rects and text as the game) and the cyan overlay on top.
    // Text uses Malgun Gothic, the font Windows draws the HUD's Korean with.
    internal static class HudComposite
    {
        public const int Width = 1920, Height = 1080;
        private static readonly Color Panel = new(0f, 0f, 0f, .6f);           // TestHudSkin.PanelColor
        private static readonly Color OpenGreen = new(.3f, .85f, .4f);        // NetworkTestHud.OpenColor
        private static readonly Color ClosedRed = new(.95f, .3f, .3f);        // NetworkTestHud.SeekerColor
        private static readonly Dictionary<(int, bool), Font> fonts = new();

        public static Texture2D RenderCamera(Camera camera)
        {
            var rt = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // banner: NetworkTestHud.EchoBodyBannerTitle (null = none). overlay: EscapeeTransition.OverlayAlpha.
        public static void Draw(Texture2D image, NetworkRound round, string banner, float overlay)
        {
            var pixels = image.GetPixels();
            // Round panel as NetworkTestHud.DrawRoundStatus draws it in a full round.
            var status = new Rect((Width - 640) / 2f, 24, 640, NetworkTestHud.RoundPanelHeight);
            Fill(pixels, status, Panel);
            if (round != null)
            {
                int seconds = Mathf.Max(0, Mathf.CeilToInt(round.RemainingSeconds));
                string top = round.Mode == RoundMode.Full ? $"남은 시간  {seconds / 60}:{seconds % 60:00}" : "밸브 전용 모드";
                Text(pixels, new Rect(status.x + 16, status.y + 16, status.width - 32, 52), top, 40, true, Color.white);
                Text(pixels, new Rect(status.x + 16, status.y + 72, status.width - 32, 36), round.GateOpen ? "게이트 열림" : "게이트 닫힘",
                    28, true, round.GateOpen ? OpenGreen : ClosedRed);
            }
            if (banner != null)
            {
                var rect = NetworkTestHud.EchoBodyBannerRect(Width, status.yMax);
                Fill(pixels, rect, Panel);
                Text(pixels, NetworkTestHud.EchoBodyBannerTitleRect(rect), banner, 40, true, Color.white);
                Text(pixels, NetworkTestHud.EchoBodyBannerDetailRect(rect), NetworkTestHud.EchoBodyBannerDetail, 20, false, Color.white);
            }
            if (overlay > 0f)
            {
                var cyan = EscapeFade.Cyan;
                Fill(pixels, new Rect(0, 0, Width, Height), new Color(cyan.r, cyan.g, cyan.b, overlay));
            }
            image.SetPixels(pixels);
            image.Apply();
        }

        // rect in IMGUI space (top-left origin); the texture's rows run bottom-up.
        private static void Fill(Color[] pixels, Rect rect, Color color)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt(rect.xMin), 0, Width), x1 = Mathf.Clamp(Mathf.RoundToInt(rect.xMax), 0, Width);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(rect.yMin), 0, Height), y1 = Mathf.Clamp(Mathf.RoundToInt(rect.yMax), 0, Height);
            for (int y = y0; y < y1; y++)
            {
                int row = (Height - 1 - y) * Width;
                for (int x = x0; x < x1; x++) pixels[row + x] = Color.Lerp(pixels[row + x], color, color.a);
            }
        }

        // Centered in rect (TextAnchor.MiddleCenter), glyphs copied from the font atlas.
        private static void Text(Color[] pixels, Rect rect, string text, int size, bool bold, Color color)
        {
            var font = FontFor(size, bold);
            font.RequestCharactersInTexture(text, size, bold ? FontStyle.Bold : FontStyle.Normal);
            var atlas = ReadAtlas(font);
            var style = bold ? FontStyle.Bold : FontStyle.Normal;
            int width = 0;
            foreach (char c in text) if (font.GetCharacterInfo(c, out var info, size, style)) width += info.advance;
            float x = rect.center.x - width / 2f;
            // Baseline so the cap height sits in the middle of the rect.
            float baseline = rect.center.y + size * .36f;
            foreach (char c in text)
            {
                if (!font.GetCharacterInfo(c, out var info, size, style)) continue;
                for (int gy = info.minY; gy < info.maxY; gy++)
                    for (int gx = info.minX; gx < info.maxX; gx++)
                    {
                        float u = Mathf.InverseLerp(info.minX, info.maxX, gx + .5f);
                        float v = Mathf.InverseLerp(info.minY, info.maxY, gy + .5f);
                        Vector2 uv = Vector2.Lerp(Vector2.Lerp(info.uvBottomLeft, info.uvBottomRight, u),
                            Vector2.Lerp(info.uvTopLeft, info.uvTopRight, u), v);
                        float a = atlas.GetPixelBilinear(uv.x, uv.y).a * color.a;
                        if (a <= 0f) continue;
                        int px = Mathf.RoundToInt(x) + gx, py = Mathf.RoundToInt(baseline) - gy - 1;
                        if (px < 0 || px >= Width || py < 0 || py >= Height) continue;
                        int index = (Height - 1 - py) * Width + px;
                        pixels[index] = Color.Lerp(pixels[index], color, a);
                    }
                x += info.advance;
            }
            Object.DestroyImmediate(atlas);
        }

        private static Font FontFor(int size, bool bold)
        {
            if (!fonts.TryGetValue((size, bold), out var font) || font == null)
                fonts[(size, bold)] = font = Font.CreateDynamicFontFromOSFont(bold ? new[] { "Malgun Gothic Bold", "Malgun Gothic" } : new[] { "Malgun Gothic" }, size);
            return font;
        }

        // The dynamic atlas lives on the GPU only; copy it back to read glyph coverage.
        private static Texture2D ReadAtlas(Font font)
        {
            var source = font.material.mainTexture;
            var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }
    }
}
