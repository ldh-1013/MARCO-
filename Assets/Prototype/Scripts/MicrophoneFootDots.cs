using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Prototype
{
    /// <summary>Local unlit floor dots emitted at the speaker's feet.</summary>
    public sealed class MicrophoneFootDots : MonoBehaviour
    {
        private const int DiscSegments = 32;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private sealed class Dot
        {
            public GameObject gameObject;
            public Renderer renderer;
            public MaterialPropertyBlock properties;
            public float createdAt;
        }

        private readonly System.Collections.Generic.List<Dot> dots = new();
        private Color dotColor = Color.white;
        private float dotRadius = 0.25f;
        private float lifetime = 5f;
        private Material material;
        private Mesh discMesh;

        public void Configure(Color color, float radius, float secondsAlive, int poolSize)
        {
            dotColor = color;
            dotRadius = Mathf.Max(0.02f, radius);
            lifetime = Mathf.Max(0.1f, secondsAlive);
            EnsurePool(Mathf.Max(1, poolSize));
        }

        public void SpawnDot(Vector3 worldPosition)
        {
            EnsurePool(dots.Count == 0 ? 1 : dots.Count);
            if (dots.Count == 0) return;

            Dot dot = null;
            foreach (Dot candidate in dots)
            {
                if (!candidate.gameObject.activeSelf) { dot = candidate; break; }
            }
            if (dot == null)
            {
                dot = dots[0];
                for (int i = 1; i < dots.Count; i++)
                    if (dots[i].createdAt < dot.createdAt) dot = dots[i];
            }

            dot.createdAt = Time.unscaledTime;
            dot.gameObject.transform.position = worldPosition;
            dot.gameObject.transform.localScale = Vector3.one * dotRadius;
            dot.gameObject.SetActive(true);
            SetAlpha(dot, 1f);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            float fadeDuration = Mathf.Min(0.5f, lifetime * 0.2f);
            foreach (Dot dot in dots)
            {
                if (!dot.gameObject.activeSelf) continue;
                float age = now - dot.createdAt;
                if (age >= lifetime)
                {
                    dot.gameObject.SetActive(false);
                    continue;
                }

                float fadeStart = lifetime - fadeDuration;
                float alpha = age <= fadeStart ? 1f : 1f - Mathf.InverseLerp(fadeStart, lifetime, age);
                SetAlpha(dot, alpha);
            }
        }

        private void EnsurePool(int requiredCount)
        {
            if (discMesh == null) discMesh = CreateDiscMesh();
            if (material == null)
            {
                Shader shader = Shader.Find("Marco/MicrophoneFootDot");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return;

                material = new Material(shader) { name = "Local Microphone Foot Dot Material" };
                material.renderQueue = (int)RenderQueue.Transparent;
                if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
                if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            }

            while (dots.Count < requiredCount)
            {
                GameObject dotObject = new GameObject("Local Microphone Foot Dot");
                dotObject.AddComponent<MeshFilter>().sharedMesh = discMesh;
                Renderer dotRenderer = dotObject.AddComponent<MeshRenderer>();
                dotRenderer.sharedMaterial = material;
                dotRenderer.shadowCastingMode = ShadowCastingMode.Off;
                dotRenderer.receiveShadows = false;
                var dot = new Dot { gameObject = dotObject, renderer = dotRenderer, properties = new MaterialPropertyBlock() };
                dots.Add(dot);
                dotObject.SetActive(false);
            }
        }

        private static Mesh CreateDiscMesh()
        {
            var vertices = new Vector3[DiscSegments + 1];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[DiscSegments * 3];
            vertices[0] = Vector3.zero;
            normals[0] = Vector3.up;
            for (int i = 0; i < DiscSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / DiscSegments;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                normals[i + 1] = Vector3.up;
                int triangle = i * 3;
                triangles[triangle] = 0;
                triangles[triangle + 1] = (i + 1) % DiscSegments + 1;
                triangles[triangle + 2] = i + 1;
            }

            var mesh = new Mesh { name = "Microphone Foot Dot Disc" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void SetAlpha(Dot dot, float alpha)
        {
            Color color = dotColor;
            color.a *= alpha;
            dot.properties.SetColor(BaseColorId, color);
            dot.renderer.SetPropertyBlock(dot.properties);
        }

        private void OnDestroy()
        {
            foreach (Dot dot in dots)
                if (dot.gameObject != null)
                {
                    if (Application.isPlaying) Destroy(dot.gameObject);
                    else DestroyImmediate(dot.gameObject);
                }

            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            if (discMesh != null)
            {
                if (Application.isPlaying) Destroy(discMesh);
                else DestroyImmediate(discMesh);
            }
        }
    }
}
