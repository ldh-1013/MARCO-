using Marco.Network.Round;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Vision
{
    // Local display only. One reusable mesh, rendered only into the owning player's camera.
    // The existing ring shader is unlit and depth-tested, so this neither lights terrain nor draws through walls.
    public sealed class DecoyTrajectoryPreview
    {
        private const int Sides = 8;
        private readonly Vector3[] points = new Vector3[DecoyConfig.PreviewMaxDots];
        private readonly Vector3[] vertices = new Vector3[DecoyConfig.PreviewMaxDots * Sides];
        private readonly int[] triangles = new int[DecoyConfig.PreviewMaxDots * (Sides - 2) * 3];
        private Mesh mesh;
        private Material material;

        public void Draw(Camera camera, Vector3 origin, Vector3 aim, Shader shader)
        {
            if (camera == null || shader == null) return;
            int count = DecoyBallistics.Predict(origin, aim, points, out bool landed);
            if (count == 0) return;
            if (mesh == null) { mesh = new Mesh { name = "Local dotted throw trajectory" }; mesh.MarkDynamic(); }
            if (material == null)
            {
                material = new Material(shader) { name = "Local throw preview" };
                material.SetColor("_BaseColor", new Color(.85f, .95f, 1f, .9f));
            }
            Vector3 right = camera.transform.right, up = camera.transform.up;
            for (int i = 0; i < count; i++)
            {
                float radius = landed && i == count - 1 ? DecoyConfig.PreviewLandingRadius : DecoyConfig.PreviewDotRadius;
                int start = i * Sides;
                for (int j = 0; j < Sides; j++)
                {
                    float angle = j * Mathf.PI * 2f / Sides;
                    vertices[start + j] = points[i] + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
                }
                for (int j = 0; j < Sides - 2; j++)
                {
                    int index = (i * (Sides - 2) + j) * 3;
                    triangles[index] = start; triangles[index + 1] = start + j + 1; triangles[index + 2] = start + j + 2;
                }
            }
            mesh.Clear();
            mesh.SetVertices(vertices, 0, count * Sides);
            mesh.SetTriangles(triangles, 0, count * (Sides - 2) * 3, 0);
            Graphics.DrawMesh(mesh, Matrix4x4.identity, material, 0, camera, 0, null,
                ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
        }

        public void Dispose()
        {
            if (mesh != null) { if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh); }
            if (material != null) { if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material); }
            mesh = null; material = null;
        }
    }
}
