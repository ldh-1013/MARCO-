using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Vision
{
    // Writes a RippleShape into a ring strip once. Segment count follows the circumference, not the
    // number of rays, so a 22m ring stays round. Vertex positions hold the fully expanded shape
    // (bounds and checks read it); the shader moves the strip to the current wavefront.
    internal static class RippleMesh
    {
        public const string ShaderPath = "MarcoRipple/RippleRing";
        public static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        public static readonly int Radius = Shader.PropertyToID("_Radius");
        public static readonly int Step = Shader.PropertyToID("_Step");
        public static readonly int Core = Shader.PropertyToID("_Core");
        public static readonly int Trail = Shader.PropertyToID("_Trail");
        public static readonly int Halo = Shader.PropertyToID("_Halo");
        public static readonly int Glow = Shader.PropertyToID("_Glow");
        public static readonly int Double = Shader.PropertyToID("_Double");
        public static readonly int Gap = Shader.PropertyToID("_Gap");
        public static readonly int InnerBrightness = Shader.PropertyToID("_InnerBrightness");
        public static readonly int StopFade = Shader.PropertyToID("_StopFade");
        public static readonly int StopDim = Shader.PropertyToID("_StopDim");
        public static readonly int MinPixels = Shader.PropertyToID("_MinPixels");

        const int MaxVertices = (RippleVisualSettings.MaxSegments + 1) * 2;
        static readonly Vector3[] vertices = new Vector3[MaxVertices];
        static readonly Vector4[] ray = new Vector4[MaxVertices];
        static readonly Vector4[][] heights = { new Vector4[MaxVertices], new Vector4[MaxVertices], new Vector4[MaxVertices], new Vector4[MaxVertices] };
        static readonly int[] triangles = new int[RippleVisualSettings.MaxSegments * 6];
        static readonly float[] profile = new float[RippleShape.MaxSamples];

        static Shader shader;
        static bool shaderMissingReported;

        // The new ring shader, or null when it could not be loaded (callers fall back to the old one).
        public static Shader LoadShader()
        {
            if (shader != null) return shader;
            shader = Resources.Load<Shader>(ShaderPath);
            if (shader == null && !shaderMissingReported)
            {
                shaderMissingReported = true;
                Debug.LogWarning("[MARCO] Ring shader " + ShaderPath + " not found; drawing rings with Marco/WalkingFootRing.");
            }
            return shader;
        }

        public static bool UsesRippleShader(Material material) => material != null && material.shader == shader && shader != null;

        // No camera or no graphics device (server-only, batch -nographics): draw nothing.
        public static bool CanRender() =>
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && Camera.allCamerasCount > 0;

        public static int Segments(float radius) => Mathf.Clamp(
            Mathf.CeilToInt(2f * Mathf.PI * radius / RippleVisualSettings.SegmentLength),
            RippleVisualSettings.MinSegments, RippleVisualSettings.MaxSegments);

        public static void Write(Mesh mesh, RippleShape shape, float innerWidth)
        {
            int segments = Segments(shape.Radius);
            int count = (segments + 1) * 2;
            int rays = Mathf.Max(1, shape.RayCount);
            float jump = RippleVisualSettings.ReachJump(shape.Radius);
            int next = 0;
            for (int i = 0; i <= segments; i++)
            {
                float angle = (i % segments) * Mathf.PI * 2f / segments;
                // Bracketing rays a (at or before this angle) and b (after it), wrapping around.
                while (next < rays && shape.Angle[next] <= angle) next++;
                int a = (next - 1 + rays) % rays, b = next % rays;
                float angleA = shape.Angle[a], angleB = shape.Angle[b];
                float span = angleB - angleA; if (span <= 0) span += Mathf.PI * 2f;
                float offset = angle - angleA; if (offset < 0) offset += Mathf.PI * 2f;
                float t = rays == 1 ? 0 : Mathf.Clamp01(offset / span);
                float reachA = shape.Reach[a], reachB = shape.Reach[b];
                // Small differences blend; a large jump (a wall edge between the two rays) takes the shorter
                // reach so the ring never crosses a wall between samples.
                float reach = Mathf.Abs(reachA - reachB) <= jump ? Mathf.Lerp(reachA, reachB, t) : Mathf.Min(reachA, reachB);
                if (t <= .001f) reach = reachA; else if (t >= .999f) reach = reachB;
                for (int k = 0; k < RippleShape.MaxSamples; k++)
                    profile[k] = Mathf.Lerp(shape.Heights[a * RippleShape.MaxSamples + k], shape.Heights[b * RippleShape.MaxSamples + k], t);

                var direction = RippleReach.Direction(angle);
                float front = Mathf.Min(shape.Radius, reach);
                float inner = Mathf.Max(0, front - innerWidth);
                int v = i * 2;
                vertices[v] = direction * inner + Vector3.up * Height(profile, inner, shape.Step);
                vertices[v + 1] = direction * front + Vector3.up * Height(profile, front, shape.Step);
                ray[v] = new Vector4(direction.x, direction.z, reach, 0);
                ray[v + 1] = new Vector4(direction.x, direction.z, reach, 1);
                for (int c = 0; c < 4; c++)
                {
                    var h = new Vector4(profile[c * 4], profile[c * 4 + 1], profile[c * 4 + 2], profile[c * 4 + 3]);
                    heights[c][v] = h; heights[c][v + 1] = h;
                }
            }
            for (int i = 0; i < segments; i++)
            {
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 1; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            mesh.Clear();
            mesh.SetVertices(vertices, 0, count);
            mesh.SetUVs(0, ray, 0, count);
            for (int c = 0; c < 4; c++) mesh.SetUVs(c + 1, heights[c], 0, count);
            mesh.SetTriangles(triangles, 0, segments * 6, 0, false);
            mesh.RecalculateBounds();
            // The shader pads the strip by up to a few pixels and a halo; keep culling generous.
            var bounds = mesh.bounds;
            bounds.Expand(1f);
            mesh.bounds = bounds;
        }

        static float Height(float[] values, float distance, float step)
        {
            float f = Mathf.Clamp(distance / Mathf.Max(step, 1e-4f), 0, RippleShape.MaxSamples - 1);
            int i = Mathf.Min(Mathf.FloorToInt(f), RippleShape.MaxSamples - 2);
            return Mathf.Lerp(values[i], values[i + 1], f - i);
        }

        // Per-ring constants that never change while the ring is alive.
        public static void ApplyShape(MaterialPropertyBlock properties, RippleShape shape, bool doubleEdge)
        {
            properties.SetFloat(Step, shape.Step);
            properties.SetFloat(Double, doubleEdge ? 1f : 0f);
            properties.SetFloat(Gap, RippleVisualSettings.DoubleGap);
            properties.SetFloat(InnerBrightness, RippleVisualSettings.DoubleInnerBrightness);
            properties.SetFloat(Glow, RippleVisualSettings.Glow);
            properties.SetFloat(Halo, RippleVisualSettings.FrontHalo);
            properties.SetFloat(StopFade, Mathf.Max(.3f, shape.Radius * RippleVisualSettings.StopFadeFraction));
            properties.SetFloat(StopDim, RippleVisualSettings.StoppedDim);
            properties.SetFloat(MinPixels, RippleVisualSettings.MinPixels);
        }

        // Per-frame values. No allocation: the block is reused.
        public static void ApplyFrame(MaterialPropertyBlock properties, Color color, float alpha, float radius, float core, float trail)
        {
            color.a = Mathf.Clamp01(alpha);
            properties.SetColor(BaseColor, color);
            properties.SetFloat(Radius, radius);
            properties.SetFloat(Core, core);
            properties.SetFloat(Trail, trail);
        }
    }
}
