using System;
using System.Collections.Generic;
using Marco.Network.Audio;
using Marco.Network.Vision;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Escape
{
    // TEMPORARY — 파문 시스템 생기면 교체.
    // Sudden death 0:30 exit pulses (기획서 6.2-1) drawn as a Shout-grade ring at both exits on every
    // client, with no hearing check (distance · occlusion · 14.2 "판정 통과 청취자" are all skipped).
    // When the server-side pulse delivery path exists, NetworkRound.EmitExitPulses sends through it and
    // this class is deleted. Drawn like every other ring (RippleRing shader, stops at walls, environment
    // colour — exits are world sources); created on first use, no scene edit.
    public sealed class TemporaryExitPulseRings : MonoBehaviour
    {
        private sealed class Ring
        {
            public GameObject GameObject;
            public Renderer Renderer;
            public Mesh Mesh;
            public MaterialPropertyBlock Properties;
            public readonly RippleShape Shape = new();
            public float CreatedAt;
        }

        private static TemporaryExitPulseRings instance;
        private readonly List<Ring> rings = new();
        private readonly List<Vector3> pending = new();
        private readonly SoundWorld world = new();
        private Material material;
        private int pendingSince;
        internal int MaxQueuedFrames { get; private set; }

        public static void ShowAtAllExits()
        {
            try
            {
                if (!RippleMesh.CanRender()) return;
                // A destroyed instance (scene change) compares equal to null and is replaced.
                if (instance == null) instance = new GameObject("Temporary Exit Pulse Rings").AddComponent<TemporaryExitPulseRings>();
                if (instance.pending.Count == 0) instance.pendingSince = Time.frameCount;
                foreach (var exit in ExitView.All)
                    if (exit != null) instance.pending.Add(FloorBelow(exit.transform.position));
                instance.Flush();
            }
            catch (Exception exception) { RippleSafety.Report("TemporaryExitPulseRings.ShowAtAllExits", exception, instance); }
        }

        // Exits are tall placeholder cubes, so put the ring on the floor under them.
        private static Vector3 FloorBelow(Vector3 position)
        {
            if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.03f;
            return position;
        }

        // Build waiting rings while this frame's visual raycast budget lasts; a ring appears only when built.
        private void Flush()
        {
            while (pending.Count > 0)
            {
                var point = pending[0];
                if (RippleBudget.Remaining < RippleReach.Estimate(world, point, RippleVisualSettings.ShoutRadius)) return;
                pending.RemoveAt(0);
                MaxQueuedFrames = Mathf.Max(MaxQueuedFrames, Time.frameCount - pendingSince);
                Emit(point);
            }
        }

        private void Emit(Vector3 position)
        {
            if (!EnsureMaterial()) return;

            Ring ring = null;
            foreach (var candidate in rings)
                if (candidate.GameObject != null && !candidate.GameObject.activeSelf) { ring = candidate; break; }
            if (ring == null)
            {
                var ringObject = new GameObject("Temporary Exit Pulse Ring");
                ringObject.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "Temporary Exit Pulse Ring" };
                ringObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var ringRenderer = ringObject.AddComponent<MeshRenderer>();
                ringRenderer.sharedMaterial = material;
                ringRenderer.shadowCastingMode = ShadowCastingMode.Off;
                ringRenderer.receiveShadows = false;
                ringRenderer.lightProbeUsage = LightProbeUsage.Off;
                ringRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                ring = new Ring { GameObject = ringObject, Renderer = ringRenderer, Mesh = mesh, Properties = new MaterialPropertyBlock() };
                rings.Add(ring);
            }

            int before = world.VisualQueryCount;
            RippleReach.Build(world, position, RippleVisualSettings.ShoutRadius, ring.Shape);
            RippleBudget.Spend(world.VisualQueryCount - before);
            RippleMesh.Write(ring.Mesh, ring.Shape, RippleVisualSettings.CoreWidth + RippleVisualSettings.TrailWidth);
            RippleMesh.ApplyShape(ring.Properties, ring.Shape, false);
            ring.CreatedAt = Time.time;
            ring.GameObject.transform.position = position;
            Draw(ring, 0f);
            ring.GameObject.SetActive(true);
        }

        private void Update()
        {
            try
            {
                if (pending.Count > 0) Flush();
                float now = Time.time;
                foreach (var ring in rings)
                {
                    if (ring.GameObject == null || !ring.GameObject.activeSelf) continue;
                    float t = (now - ring.CreatedAt) / RippleVisualSettings.ShoutSeconds;
                    if (t >= 1f)
                    {
                        ring.GameObject.SetActive(false);
                        continue;
                    }
                    Draw(ring, t);
                }
            }
            catch (Exception exception) { RippleSafety.Report("TemporaryExitPulseRings.Update", exception, this); }
        }

        // Same motion as every expanding ring (RippleVisualSettings).
        private static void Draw(Ring ring, float t)
        {
            float age = t * RippleVisualSettings.ShoutSeconds;
            float fadeIn = Mathf.Clamp01(age / RippleVisualSettings.FadeInSeconds);
            float radius = Mathf.Lerp(RippleVisualSettings.StartRadius, RippleVisualSettings.ShoutRadius,
                1f - Mathf.Pow(1f - t, RippleVisualSettings.ExpandPower));
            float alpha = RippleVisualSettings.MaxAlpha * fadeIn * Mathf.Pow(1f - t, RippleVisualSettings.FadePower);
            float thin = Mathf.Lerp(1f, RippleVisualSettings.ThinAtEnd, t);
            RippleMesh.ApplyFrame(ring.Properties, RippleVisualSettings.EnvironmentColor, alpha, radius,
                RippleVisualSettings.CoreWidth * thin, RippleVisualSettings.TrailWidth * thin);
            ring.Renderer.SetPropertyBlock(ring.Properties);
        }

        private bool EnsureMaterial()
        {
            if (material != null) return true;
            var shader = RippleMesh.LoadShader();
            if (shader == null) shader = Shader.Find("Marco/WalkingFootRing");
            if (shader == null)
            {
                Debug.LogWarning("[MARCO] 임시 출구 링: 링 셰이더를 찾지 못함");
                return false;
            }
            material = new Material(shader) { name = "Temporary Exit Pulse Ring Material", renderQueue = (int)RenderQueue.Transparent };
            return true;
        }

        private void OnDestroy()
        {
            try
            {
                if (instance == this) instance = null;
                foreach (var ring in rings) if (ring.Mesh != null) Destroy(ring.Mesh);
                if (material != null) Destroy(material);
            }
            catch (Exception exception) { RippleSafety.Report("TemporaryExitPulseRings.OnDestroy", exception, this); }
        }
    }
}
