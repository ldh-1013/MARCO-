using System;
using System.Collections.Generic;
using Marco.Network.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Vision
{
    /// <summary>Leaves a local, fading wire outline of nearby map terrain after a Clicker flash.</summary>
    public sealed class TerrainAfterglow : MonoBehaviour
    {
        private const int MaximumConcurrentLayers = 8;
        private const float VertexQuantization = 10000f;
        private const float WorldEdgeQuantization = 1000f;
        private const float CreaseNormalDot = 0.94f;
        private static readonly int TerrainMarkerId = Shader.PropertyToID("_MarcoClickerTerrain");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private struct EdgeSegment
        {
            public Vector3 A;
            public Vector3 B;

            public EdgeSegment(Vector3 a, Vector3 b) { A = a; B = b; }
        }

        private struct PositionKey : IEquatable<PositionKey>, IComparable<PositionKey>
        {
            private readonly int x, y, z;

            public PositionKey(Vector3 position, float quantization)
            {
                x = Mathf.RoundToInt(position.x * quantization);
                y = Mathf.RoundToInt(position.y * quantization);
                z = Mathf.RoundToInt(position.z * quantization);
            }

            public bool Equals(PositionKey other) => x == other.x && y == other.y && z == other.z;
            public int CompareTo(PositionKey other)
            {
                int compare = x.CompareTo(other.x);
                if (compare != 0) return compare;
                compare = y.CompareTo(other.y);
                return compare != 0 ? compare : z.CompareTo(other.z);
            }
            public override bool Equals(object obj) => obj is PositionKey other && Equals(other);
            public override int GetHashCode() => unchecked((x * 397 ^ y) * 397 ^ z);
        }

        private struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly PositionKey first, second;

            public EdgeKey(Vector3 a, Vector3 b, float quantization)
            {
                var ka = new PositionKey(a, quantization);
                var kb = new PositionKey(b, quantization);
                if (ka.CompareTo(kb) <= 0) { first = ka; second = kb; }
                else { first = kb; second = ka; }
            }

            public bool Equals(EdgeKey other) => first.Equals(other.first) && second.Equals(other.second);
            public override bool Equals(object obj) => obj is EdgeKey other && Equals(other);
            public override int GetHashCode() => unchecked(first.GetHashCode() * 397 ^ second.GetHashCode());
        }

        private struct EdgeUse
        {
            public Vector3 A, B, FirstNormal;
            public int FaceCount;
            public bool IsCrease;
        }

        private sealed class AfterglowLayer
        {
            public GameObject GameObject;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Properties;
            public Vector3[] Endpoints;
            public Vector3[] Vertices;
            public int[] Triangles;
            public float FadeStartsAt;
            public float Lifetime;
            public float PeakOpacity;
        }

        private readonly List<AfterglowLayer> layers = new();
        private readonly Dictionary<Mesh, EdgeSegment[]> edgeCache = new();
        private MaterialPropertyBlock scratchProperties;
        private Camera ownerCamera;
        private Material afterglowMaterial;

        private void Awake()
        {
            scratchProperties = new MaterialPropertyBlock();
            ownerCamera = GetComponent<Camera>() ?? GetComponentInChildren<Camera>(true);
        }

        /// <summary>Captures terrain edges now and shows them once the flash ends.</summary>
        public void CaptureFlash(Vector3 origin, float radius, float flashDuration, float lifetime, float peakOpacity)
        {
            if (radius <= 0f || lifetime <= 0f) return;
            List<Vector3> endpoints = CollectTerrainEdges(origin, radius);
            if (endpoints.Count == 0) return;

            EnsureMaterial();
            if (afterglowMaterial == null) return;
            while (layers.Count >= MaximumConcurrentLayers) RemoveLayer(0);

            int segmentCount = endpoints.Count / 2;
            var layer = new AfterglowLayer
            {
                Endpoints = endpoints.ToArray(),
                Vertices = new Vector3[segmentCount * 4],
                Triangles = BuildTriangles(segmentCount),
                FadeStartsAt = Time.unscaledTime + Mathf.Max(0f, flashDuration),
                Lifetime = lifetime,
                PeakOpacity = Mathf.Clamp01(peakOpacity),
                Properties = new MaterialPropertyBlock()
            };
            // Keep prior terrain outlines visible if another flash overlaps this afterglow.
            layer.Properties.SetFloat(TerrainMarkerId, 1f);
            layer.GameObject = new GameObject("찰칵이 지형 잔상");
            layer.Mesh = new Mesh { name = "찰칵이 지형 모서리 잔상" };
            if (layer.Vertices.Length > 65535) layer.Mesh.indexFormat = IndexFormat.UInt32;
            layer.Mesh.vertices = layer.Vertices;
            layer.Mesh.triangles = layer.Triangles;
            layer.Mesh.RecalculateBounds();
            layer.GameObject.AddComponent<MeshFilter>().sharedMesh = layer.Mesh;
            layer.Renderer = layer.GameObject.AddComponent<MeshRenderer>();
            layer.Renderer.sharedMaterial = afterglowMaterial;
            layer.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            layer.Renderer.receiveShadows = false;
            layer.Renderer.enabled = false;
            layers.Add(layer);
        }

        private List<Vector3> CollectTerrainEdges(Vector3 origin, float radius)
        {
            var endpoints = new List<Vector3>();
            var seenWorldEdges = new HashSet<EdgeKey>();
            float radiusSquared = radius * radius;
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !IsMarkedTerrain(renderer)) continue;
                if (renderer.bounds.SqrDistance(origin) > radiusSquared) continue;

                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;

                foreach (EdgeSegment localEdge in GetMeshEdges(mesh))
                {
                    Vector3 a = renderer.transform.TransformPoint(localEdge.A);
                    Vector3 b = renderer.transform.TransformPoint(localEdge.B);
                    if ((b - a).sqrMagnitude < 0.0025f || !ClipToSphere(ref a, ref b, origin, radius)) continue;
                    if (!seenWorldEdges.Add(new EdgeKey(a, b, WorldEdgeQuantization))) continue;
                    endpoints.Add(a);
                    endpoints.Add(b);
                }
            }

            return endpoints;
        }

        private bool IsMarkedTerrain(Renderer renderer)
        {
            if (scratchProperties == null) scratchProperties = new MaterialPropertyBlock();
            if (renderer.GetComponentInParent<NetworkPlayer>(true) != null) return false;
            renderer.GetPropertyBlock(scratchProperties);
            return scratchProperties.GetFloat(TerrainMarkerId) > 0.5f;
        }

        private EdgeSegment[] GetMeshEdges(Mesh mesh)
        {
            if (edgeCache.TryGetValue(mesh, out EdgeSegment[] cached)) return cached;
            if (!mesh.isReadable)
            {
                edgeCache[mesh] = Array.Empty<EdgeSegment>();
                return edgeCache[mesh];
            }

            var uses = new Dictionary<EdgeKey, EdgeUse>();
            try
            {
                Vector3[] vertices = mesh.vertices;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                    int[] indices = mesh.GetIndices(submesh);
                    for (int index = 0; index + 2 < indices.Length; index += 3)
                    {
                        Vector3 a = vertices[indices[index]];
                        Vector3 b = vertices[indices[index + 1]];
                        Vector3 c = vertices[indices[index + 2]];
                        Vector3 cross = Vector3.Cross(b - a, c - a);
                        if (cross.sqrMagnitude < 0.000001f) continue;
                        Vector3 normal = cross.normalized;
                        AddEdge(uses, a, b, normal);
                        AddEdge(uses, b, c, normal);
                        AddEdge(uses, c, a, normal);
                    }
                }
            }
            catch (UnityException)
            {
                edgeCache[mesh] = Array.Empty<EdgeSegment>();
                return edgeCache[mesh];
            }

            var edges = new List<EdgeSegment>();
            foreach (EdgeUse use in uses.Values)
                if (use.FaceCount == 1 || use.IsCrease) edges.Add(new EdgeSegment(use.A, use.B));
            EdgeSegment[] result = edges.ToArray();
            edgeCache[mesh] = result;
            return result;
        }

        private static void AddEdge(Dictionary<EdgeKey, EdgeUse> uses, Vector3 a, Vector3 b, Vector3 faceNormal)
        {
            var key = new EdgeKey(a, b, VertexQuantization);
            if (uses.TryGetValue(key, out EdgeUse use))
            {
                if (Vector3.Dot(use.FirstNormal, faceNormal) < CreaseNormalDot) use.IsCrease = true;
                use.FaceCount++;
                uses[key] = use;
            }
            else
            {
                uses.Add(key, new EdgeUse { A = a, B = b, FirstNormal = faceNormal, FaceCount = 1 });
            }
        }

        private static bool ClipToSphere(ref Vector3 a, ref Vector3 b, Vector3 center, float radius)
        {
            Vector3 start = a;
            Vector3 direction = b - start;
            float aa = Vector3.Dot(direction, direction);
            if (aa < 0.000001f) return (start - center).sqrMagnitude <= radius * radius;

            Vector3 offset = start - center;
            float bb = 2f * Vector3.Dot(offset, direction);
            float cc = Vector3.Dot(offset, offset) - radius * radius;
            float discriminant = bb * bb - 4f * aa * cc;
            if (discriminant < 0f) return cc <= 0f;

            float root = Mathf.Sqrt(discriminant);
            float enter = Mathf.Max(0f, (-bb - root) / (2f * aa));
            float exit = Mathf.Min(1f, (-bb + root) / (2f * aa));
            if (enter > exit) return false;
            a = start + direction * enter;
            b = start + direction * exit;
            return true;
        }

        private void EnsureMaterial()
        {
            if (afterglowMaterial != null) return;
            Shader shader = GetComponent<WalkingFootRings>()?.RingShader;
            if (shader == null) shader = Shader.Find("Marco/WalkingFootRing");
            if (shader == null)
            {
                Debug.LogWarning("Clicker Afterglow needs the shared world-ring shader.", this);
                return;
            }
            afterglowMaterial = new Material(shader) { name = "찰칵이 지형 잔상 Material" };
            afterglowMaterial.renderQueue = (int)RenderQueue.Transparent;
        }

        private static int[] BuildTriangles(int segmentCount)
        {
            var triangles = new int[segmentCount * 6];
            for (int segment = 0; segment < segmentCount; segment++)
            {
                int vertex = segment * 4;
                int index = segment * 6;
                triangles[index] = vertex;
                triangles[index + 1] = vertex + 1;
                triangles[index + 2] = vertex + 2;
                triangles[index + 3] = vertex;
                triangles[index + 4] = vertex + 2;
                triangles[index + 5] = vertex + 3;
            }
            return triangles;
        }

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                AfterglowLayer layer = layers[i];
                float elapsed = now - layer.FadeStartsAt;
                if (elapsed >= layer.Lifetime)
                {
                    RemoveLayer(i);
                    continue;
                }
                if (elapsed < 0f) continue;

                layer.Renderer.enabled = true;
                float linearFade = 1f - Mathf.Clamp01(elapsed / layer.Lifetime);
                layer.Properties.SetColor(BaseColorId, new Color(1f, 1f, 1f, layer.PeakOpacity * linearFade));
                layer.Renderer.SetPropertyBlock(layer.Properties);
                UpdateBillboardVertices(layer);
            }
        }

        private void UpdateBillboardVertices(AfterglowLayer layer)
        {
            Transform cameraTransform = ownerCamera != null ? ownerCamera.transform : transform;
            for (int segment = 0; segment < layer.Endpoints.Length / 2; segment++)
            {
                Vector3 a = layer.Endpoints[segment * 2];
                Vector3 b = layer.Endpoints[segment * 2 + 1];
                Vector3 direction = (b - a).normalized;
                Vector3 midpoint = (a + b) * 0.5f;
                Vector3 towardCamera = cameraTransform.position - midpoint;
                Vector3 side = Vector3.Cross(direction, towardCamera).normalized;
                if (side.sqrMagnitude < 0.5f) side = Vector3.Cross(direction, cameraTransform.up).normalized;
                if (side.sqrMagnitude < 0.5f) side = Vector3.Cross(direction, cameraTransform.right).normalized;
                float halfWidth = Mathf.Clamp(towardCamera.magnitude * 0.0015f, 0.003f, 0.008f);
                side *= halfWidth;

                int vertex = segment * 4;
                layer.Vertices[vertex] = a - side;
                layer.Vertices[vertex + 1] = a + side;
                layer.Vertices[vertex + 2] = b + side;
                layer.Vertices[vertex + 3] = b - side;
            }

            layer.Mesh.vertices = layer.Vertices;
            layer.Mesh.RecalculateBounds();
        }

        private void RemoveLayer(int index)
        {
            AfterglowLayer layer = layers[index];
            layers.RemoveAt(index);
            DestroyRuntimeObject(layer.GameObject);
            DestroyRuntimeObject(layer.Mesh);
        }

        public void ClearAll()
        {
            for (int i = layers.Count - 1; i >= 0; i--) RemoveLayer(i);
            edgeCache.Clear();
            if (afterglowMaterial != null)
            {
                DestroyRuntimeObject(afterglowMaterial);
                afterglowMaterial = null;
            }
        }

        private void OnDisable() => ClearAll();
        private void OnDestroy() => ClearAll();

        private static void DestroyRuntimeObject(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
