using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Vision
{
    // Expanding floor rings while the owner walks on the ground. Owner only: it sits on the player
    // camera, which only the owner activates. Copied from Prototype WalkingFootRings; the emission
    // that lived in PrototypeFirstPersonController now reads the CharacterController here.
    public sealed class WalkingFootRings : MonoBehaviour
    {
        private const int RingSegments = 48;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private sealed class Ring
        {
            public GameObject gameObject;
            public Renderer renderer;
            public MaterialPropertyBlock properties;
            public float createdAt;
        }

        private readonly List<Ring> rings = new();
        [SerializeField] private Shader ringShader;
        [SerializeField, Min(0.05f)] private float walkingRingInterval = 0.45f;
        [SerializeField, Min(0.05f)] private float maxRadius = 1f;
        [SerializeField, Min(0.05f)] private float lifetime = 0.8f;
        private CharacterController controller;
        private float nextFootRingTime;
        private Material material;
        private Mesh ringMesh;
        private Color ringColor = new Color(0.75f, 0.9f, 1f, 0.8f);

        public Shader RingShader => ringShader;

        private void Start()
        {
            controller = GetComponentInParent<CharacterController>();
            EnsurePool(8);
        }

        private void SpawnRing(Vector3 worldPosition)
        {
            EnsurePool(rings.Count == 0 ? 1 : rings.Count);
            if (rings.Count == 0) return;

            Ring ring = null;
            foreach (Ring candidate in rings)
                if (!candidate.gameObject.activeSelf) { ring = candidate; break; }
            if (ring == null)
            {
                ring = rings[0];
                for (int i = 1; i < rings.Count; i++)
                    if (rings[i].createdAt < ring.createdAt) ring = rings[i];
            }

            ring.createdAt = Time.unscaledTime;
            ring.gameObject.transform.position = worldPosition;
            ring.gameObject.transform.localScale = Vector3.one * 0.03f;
            ring.gameObject.SetActive(true);
            SetAlpha(ring, ringColor.a);
        }

        private void Update()
        {
            EmitWhileWalking();
            float now = Time.unscaledTime;
            foreach (Ring ring in rings)
            {
                if (!ring.gameObject.activeSelf) continue;
                float t = (now - ring.createdAt) / lifetime;
                if (t >= 1f)
                {
                    ring.gameObject.SetActive(false);
                    continue;
                }

                float expansion = 1f - (1f - t) * (1f - t);
                float radius = Mathf.Lerp(0.03f, maxRadius, expansion);
                ring.gameObject.transform.localScale = Vector3.one * radius;
                float alpha = ringColor.a * (1f - t);
                SetAlpha(ring, alpha);
            }
        }

        private void EmitWhileWalking()
        {
            bool walkingOnGround = controller != null && controller.isGrounded
                && Vector3.ProjectOnPlane(controller.velocity, Vector3.up).sqrMagnitude > 0.01f;
            if (!walkingOnGround) { nextFootRingTime = 0f; return; }
            nextFootRingTime -= Time.deltaTime;
            if (nextFootRingTime > 0f) return;
            Vector3 position = controller.transform.position;
            SpawnRing(new Vector3(position.x, controller.bounds.min.y + 0.025f, position.z));
            nextFootRingTime = walkingRingInterval;
        }

        private void EnsurePool(int requiredCount)
        {
            if (ringMesh == null) ringMesh = CreateRingMesh();
            if (material == null)
            {
                Shader shader = ringShader != null ? ringShader : Shader.Find("Marco/WalkingFootRing");
                if (shader == null) return;
                material = new Material(shader) { name = "Local Walking Foot Ring Material" };
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            while (rings.Count < requiredCount)
            {
                var ringObject = new GameObject("Local Walking Foot Ring");
                ringObject.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                var ringRenderer = ringObject.AddComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = material;
                ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
                ringRenderer.receiveShadows = false;
                rings.Add(new Ring { gameObject = ringObject, renderer = ringRenderer, properties = new MaterialPropertyBlock() });
                ringObject.SetActive(false);
            }
        }

        private static Mesh CreateRingMesh()
        {
            const float innerRadius = 0.9f;
            var vertices = new Vector3[RingSegments * 2];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[RingSegments * 6];
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);
                int inner = i * 2;
                int outer = inner + 1;
                vertices[inner] = new Vector3(x * innerRadius, 0f, z * innerRadius);
                vertices[outer] = new Vector3(x, 0f, z);
                normals[inner] = normals[outer] = Vector3.up;
                uv[inner] = new Vector2(i / (float)RingSegments, 0f);
                uv[outer] = new Vector2(i / (float)RingSegments, 1f);

                int nextInner = ((i + 1) % RingSegments) * 2;
                int nextOuter = nextInner + 1;
                int ti = i * 6;
                triangles[ti] = inner;
                triangles[ti + 1] = outer;
                triangles[ti + 2] = nextInner;
                triangles[ti + 3] = outer;
                triangles[ti + 4] = nextOuter;
                triangles[ti + 5] = nextInner;
            }

            var mesh = new Mesh { name = "Walking Foot Ring Annulus" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void SetAlpha(Ring ring, float alpha)
        {
            Color color = ringColor;
            color.a = alpha;
            ring.properties.SetColor(BaseColorId, color);
            ring.renderer.SetPropertyBlock(ring.properties);
        }

        private void OnDestroy()
        {
            foreach (Ring ring in rings)
                if (ring.gameObject != null)
                {
                    if (Application.isPlaying) Destroy(ring.gameObject);
                    else DestroyImmediate(ring.gameObject);
                }
            if (material != null)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            if (ringMesh != null)
            {
                if (Application.isPlaying) Destroy(ringMesh);
                else DestroyImmediate(ringMesh);
            }
        }
    }
}
