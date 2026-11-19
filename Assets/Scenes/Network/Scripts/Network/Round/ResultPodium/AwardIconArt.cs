using System;
using System.Collections.Generic;
using Marco.Core.WinCondition;
using UnityEngine;

namespace Marco.Network.Round.ResultPodium
{
    // 표시 전용. The three award icons (기획서 8 · 12.5), drawn in code — no image assets. Each is painted once
    // from signed distance shapes into a 256² texture: a white disc with black ink (and white marks outside the
    // disc), anti-aliased by the distance itself and mip-mapped so it stays clean at badge size. The podium
    // glow shader multiplies it by the card colour (amber #FFB84D, or the #E8E8E8 grey for "수상자 없음"),
    // so the icons never bring a colour of their own (16.2).
    //   최다 비명상  screaming face — wide eyes, open mouth — with sound waves on both sides
    //   무성 생존상  "쉿" — closed eyes, a finger over the lips
    //   최고의 낚시상 fishhook on its line, point and barb
    public static class AwardIconArt
    {
        public const int Size = 256;
        private const float Disc = .74f, ScreamDisc = .64f;
        private static readonly Dictionary<AwardKind, Texture2D> cache = new();

        public static Texture2D Get(AwardKind kind)
        {
            if (cache.TryGetValue(kind, out var texture) && texture != null) return texture;
            var canvas = new Canvas();
            switch (kind)
            {
                case AwardKind.MostScreams: PaintScream(canvas); break;
                case AwardKind.SilentSurvivor: PaintShush(canvas); break;
                default: PaintHook(canvas); break;
            }
            texture = canvas.ToTexture("Award icon " + kind);
            cache[kind] = texture;
            return texture;
        }

        // ---------------------------------------------------------------- the three drawings (y up, -1..1)

        private static void PaintScream(Canvas c)
        {
            c.Paint(p => Circle(p, Vector2.zero, ScreamDisc), 1);
            // Eyes wide open, brows lifted.
            c.Paint(p => Ellipse(p, new Vector2(-.21f, .15f), .07f, .105f), 0);
            c.Paint(p => Ellipse(p, new Vector2(.21f, .15f), .07f, .105f), 0);
            c.Paint(p => Capsule(p, new Vector2(-.31f, .34f), new Vector2(-.13f, .39f), .03f), 0);
            c.Paint(p => Capsule(p, new Vector2(.31f, .34f), new Vector2(.13f, .39f), .03f), 0);
            // The scream: a tall open mouth.
            c.Paint(p => Ellipse(p, new Vector2(0, -.25f), .15f, .22f), 0);
            // Sound waves, both sides, outside the face.
            for (int side = 0; side < 2; side++)
            {
                float centre = side == 0 ? 0f : Mathf.PI;
                c.Paint(p => Arc(p, Vector2.zero, .78f, .065f, centre, .42f), 1);
                c.Paint(p => Arc(p, Vector2.zero, .92f, .055f, centre, .3f), 1);
            }
        }

        private static void PaintShush(Canvas c)
        {
            c.Paint(p => Circle(p, Vector2.zero, Disc), 1);
            // Calm closed eyes (‿).
            c.Paint(p => Arc(p, new Vector2(-.25f, .26f), .11f, .055f, -Mathf.PI / 2, 1.05f), 0);
            c.Paint(p => Arc(p, new Vector2(.25f, .26f), .11f, .055f, -Mathf.PI / 2, 1.05f), 0);
            // Pursed lips, then the finger over them, a little to the right as in 🤫: rising from a fist at
            // the bottom rim, inked outline, white inside — so it never reads as a cross.
            c.Paint(p => Ellipse(p, new Vector2(-.04f, -.27f), .13f, .065f), 0);
            var tip = new Vector2(.08f, -.03f);
            var knuckle = new Vector2(.08f, -.5f);
            var fist = new Vector2(.14f, -.78f);
            float rim = Disc - .045f;
            c.Paint(p => Mathf.Max(Mathf.Min(Capsule(p, knuckle, tip, .115f), Ellipse(p, fist, .32f, .22f) - .045f), Circle(p, Vector2.zero, Disc)), 0);
            c.Paint(p => Mathf.Max(Mathf.Min(Capsule(p, knuckle, tip, .07f), Ellipse(p, fist, .32f, .22f)), Circle(p, Vector2.zero, rim)), 1);
            // The nail near the tip.
            c.Paint(p => Arc(p, tip + new Vector2(0, .005f), .04f, .028f, -Mathf.PI / 2, 1.2f), 0);
        }

        private static void PaintHook(Canvas c)
        {
            c.Paint(p => Circle(p, Vector2.zero, Disc), 1);
            var o = new Vector2(.05f, -.06f);           // optical centring of the whole hook
            const float w = .05f;                        // half stroke
            var eye = o + new Vector2(.1f, .47f);
            var shankBottom = o + new Vector2(.1f, -.16f);
            var bend = o + new Vector2(-.07f, -.16f);    // centre of the U
            const float bendRadius = .17f;
            var pointBase = bend + new Vector2(-bendRadius, 0);
            // The line, from the eye up to the rim.
            c.Paint(p => Mathf.Max(Capsule(p, eye + new Vector2(0, .08f), eye + new Vector2(0, 1f), .022f), Circle(p, Vector2.zero, Disc - .02f)), 0);
            c.Paint(p => Ring(p, eye, .085f, .052f), 0);
            c.Paint(p => Capsule(p, eye + new Vector2(0, -.085f), shankBottom, w), 0);
            c.Paint(p => Arc(p, bend, bendRadius, 2 * w, -Mathf.PI / 2, Mathf.PI / 2), 0);
            // Point: tapering up from the end of the bend, barb hooking down inside.
            c.Paint(p => Triangle(p, pointBase + new Vector2(-w, 0), pointBase + new Vector2(w, 0), pointBase + new Vector2(-.01f, .27f)), 0);
            c.Paint(p => Triangle(p, pointBase + new Vector2(.01f, .2f), pointBase + new Vector2(.035f, .07f), pointBase + new Vector2(.13f, .03f)), 0);
        }

        // ---------------------------------------------------------------- distance shapes (negative inside)

        private static float Circle(Vector2 p, Vector2 centre, float radius) => (p - centre).magnitude - radius;

        // Not exact away from the curve, exact enough at the edge for anti-aliasing.
        private static float Ellipse(Vector2 p, Vector2 centre, float rx, float ry)
        {
            var q = p - centre;
            float k = new Vector2(q.x / rx, q.y / ry).magnitude;
            return (k - 1) * Mathf.Min(rx, ry);
        }

        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float radius)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, ba.sqrMagnitude));
            return (pa - ba * h).magnitude - radius;
        }

        private static float Ring(Vector2 p, Vector2 centre, float radius, float thickness) =>
            Mathf.Abs((p - centre).magnitude - radius) - thickness / 2;

        // Stroke along a circle, round ends; `middle` = angle of the arc's middle, `half` = half its span.
        private static float Arc(Vector2 p, Vector2 centre, float radius, float thickness, float middle, float half)
        {
            var q = p - centre;
            float angle = Mathf.Atan2(q.y, q.x);
            float delta = Mathf.Abs(Mathf.DeltaAngle(middle * Mathf.Rad2Deg, angle * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
            if (delta <= half) return Mathf.Abs(q.magnitude - radius) - thickness / 2;
            var end1 = centre + new Vector2(Mathf.Cos(middle + half), Mathf.Sin(middle + half)) * radius;
            var end2 = centre + new Vector2(Mathf.Cos(middle - half), Mathf.Sin(middle - half)) * radius;
            return Mathf.Min((p - end1).magnitude, (p - end2).magnitude) - thickness / 2;
        }

        private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d = Mathf.Min(SegmentDistance(p, a, b), Mathf.Min(SegmentDistance(p, b, c), SegmentDistance(p, c, a)));
            float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(a - c, p - c);
            bool inside = (s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0);
            return inside ? -d : d;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b) => Capsule(p, a, b, 0);
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        // ---------------------------------------------------------------- canvas

        // White/black ink over transparent. Transparent pixels keep white so filtering never darkens the
        // disc's edge.
        private sealed class Canvas
        {
            private readonly float[] luma = new float[Size * Size];
            private readonly float[] alpha = new float[Size * Size];
            private const float Pixel = 2f / Size;

            public Canvas() { for (int i = 0; i < luma.Length; i++) luma[i] = 1; }

            public void Paint(Func<Vector2, float> shape, float ink)
            {
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        var p = new Vector2((x + .5f) * Pixel - 1, (y + .5f) * Pixel - 1);
                        float cover = Mathf.Clamp01(.5f - shape(p) / Pixel);
                        if (cover <= 0) continue;
                        int i = y * Size + x;
                        // Ink over transparent takes the ink colour; over paint it blends.
                        luma[i] = alpha[i] <= 0 ? ink : Mathf.Lerp(luma[i], ink, cover);
                        alpha[i] = alpha[i] + cover * (1 - alpha[i]);
                    }
            }

            public Texture2D ToTexture(string name)
            {
                var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
                {
                    name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2,
                    hideFlags = HideFlags.DontSave,
                };
                var pixels = new Color32[Size * Size];
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte l = (byte)Mathf.RoundToInt(luma[i] * 255), a = (byte)Mathf.RoundToInt(alpha[i] * 255);
                    pixels[i] = new Color32(l, l, l, a);
                }
                texture.SetPixels32(pixels);
                texture.Apply(true, false);
                return texture;
            }
        }
    }
}
