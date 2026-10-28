using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Escape
{
    // TEMPORARY — 파문 시스템 생기면 교체.
    // Sudden death 0:30 exit pulses (기획서 6.2-1) drawn as an expanding floor ring at both exits on every
    // client, with no hearing check (distance · occlusion · 14.2 "판정 통과 청취자" are all skipped).
    // When the server-side pulse delivery path exists, NetworkRound.EmitExitPulses sends through it and
    // this class is deleted. Reuses the WalkingFootRings shader by name; created on first use, no scene edit.
    public sealed class TemporaryExitPulseRings : MonoBehaviour
    {
        private const int RingSegments = 64;
        private const float InnerRadius = 0.96f;
        // Shout grade (기획서 부록 A [소리 등급]). Only the ring size; the real radius belongs to the pulse system.
        private const float ShoutRadius = 22f;
        private const float Lifetime = 1.5f;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color RingColor = new(0.75f, 0.9f, 1f, 0.8f);

        private sealed class Ring
        {
            public GameObject GameObject;
            public Renderer Renderer;
            public MaterialPropertyBlock Properties;
            public float CreatedAt;
        }

        private static TemporaryExitPulseRings instance;
        private readonly List<Ring> rings = new();
        private Material material;
        private Mesh mesh;

        public static void ShowAtAllExits()
        {
            if (instance == null) instance = new GameObject("Temporary Exit Pulse Rings").AddComponent<TemporaryExitPulseRings>();
            foreach (var exit in ExitView.All) instance.Spawn(FloorBelow(exit.transform.position));
        }

        // Exits are tall placeholder cubes, so put the ring on the floor under them.
        private static Vector3 FloorBelow(Vector3 position)
        {
            if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.03f;
            return position;
        }

        private void Spawn(Vector3 position)
        {
            if (!EnsureResources()) return;

            Ring ring = null;
            foreach (var candidate in rings)
                if (!candidate.GameObject.activeSelf) { ring = candidate; break; }
            if (ring == null)
            {
                var ringObject = new GameObject("Temporary Exit Pulse Ring");
                ringObject.transform.SetParent(transform, false);
                ringObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var ringRenderer = ringObject.AddComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = material;
                ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
                ringRenderer.receiveShadows = false;
                ring = new Ring { GameObject = ringObject, Renderer = ringRenderer, Properties = new MaterialPropertyBlock() };
                rings.Add(ring);
            }

            ring.CreatedAt = Time.unscaledTime;
            ring.GameObject.transform.position = position;
            ring.GameObject.transform.localScale = Vector3.one * 0.1f;
            ring.GameObject.SetActive(true);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            foreach (var ring in rings)
            {
                if (!ring.GameObject.activeSelf) continue;
                float t = (now - ring.CreatedAt) / Lifetime;
                if (t >= 1f)
                {
                    ring.GameObject.SetActive(false);
                    continue;
                }

                float expansion = 1f - (1f - t) * (1f - t);
                ring.GameObject.transform.localScale = Vector3.one * Mathf.Lerp(0.1f, ShoutRadius, expansion);
                var color = RingColor;
                color.a *= 1f - t;
                ring.Properties.SetColor(BaseColorId, color);
                ring.Renderer.SetPropertyBlock(ring.Properties);
            }
        }

        private bool EnsureResources()
        {
            if (mesh == null) mesh = CreateRingMesh();
            if (material != null) return true;
            var shader = Shader.Find("Marco/WalkingFootRing");
            if (shader == null)
            {
                Debug.LogWarning("[MARCO] 임시 출구 링: Marco/WalkingFootRing 셰이더를 찾지 못함");
                return false;
            }
            material = new Material(shader) { name = "Temporary Exit Pulse Ring Material", renderQueue = (int)RenderQueue.Transparent };
            return true;
        }

        private static Mesh CreateRingMesh()
        {
            var vertices = new Vector3[RingSegments * 2];
            var normals = new Vector3[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[RingSegments * 6];
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                float x = Mathf.Cos(angle), z = Mathf.Sin(angle);
                int inner = i * 2, outer = inner + 1;
                vertices[inner] = new Vector3(x * InnerRadius, 0f, z * InnerRadius);
                vertices[outer] = new Vector3(x, 0f, z);
                normals[inner] = normals[outer] = Vector3.up;
                uv[inner] = new Vector2(i / (float)RingSegments, 0f);
                uv[outer] = new Vector2(i / (float)RingSegments, 1f);

                int nextInner = (i + 1) % RingSegments * 2, nextOuter = nextInner + 1, ti = i * 6;
                triangles[ti] = inner;
                triangles[ti + 1] = outer;
                triangles[ti + 2] = nextInner;
                triangles[ti + 3] = outer;
                triangles[ti + 4] = nextOuter;
                triangles[ti + 5] = nextInner;
            }

            var ringMesh = new Mesh { name = "Temporary Exit Pulse Ring" };
            ringMesh.vertices = vertices;
            ringMesh.normals = normals;
            ringMesh.uv = uv;
            ringMesh.triangles = triangles;
            ringMesh.RecalculateBounds();
            return ringMesh;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (material != null) Destroy(material);
            if (mesh != null) Destroy(mesh);
        }
    }
}
