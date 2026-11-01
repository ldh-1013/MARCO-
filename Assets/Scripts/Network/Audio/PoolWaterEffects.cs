using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Network.Audio
{
    // One bounded visual pool per client. The water material consumes world-space rings;
    // one particle system handles all entry spray without spawning per-drop GameObjects.
    [ExecuteAlways]
    public sealed class PoolWaterEffects : MonoBehaviour
    {
        public const int Capacity = 16;
        private static PoolWaterEffects instance;
        private readonly Vector4[] ripples = new Vector4[Capacity];
        private readonly Vector4[] bounds = new Vector4[Capacity];
        private int next, count;
        private ParticleSystem spray;
        private static readonly int DataId = Shader.PropertyToID("_MarcoWaterRipples");
        private static readonly int BoundsId = Shader.PropertyToID("_MarcoWaterRippleBounds");
        private static readonly int CountId = Shader.PropertyToID("_MarcoWaterRippleCount");
        private static readonly int ClockId = Shader.PropertyToID("_MarcoWaterRippleClock");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobals()
        {
            instance = null;
            Shader.SetGlobalInteger(CountId, 0);
        }

        public static void Emit(Vector3 point, Bounds pool, float strength, bool splash, Scene scene)
        {
            if (instance == null)
            {
                var root = new GameObject("Pool Water Reactions");
                SceneManager.MoveGameObjectToScene(root, scene);
                instance = root.AddComponent<PoolWaterEffects>();
            }
            instance.Add(point, pool, strength, splash);
        }

        private void Add(Vector3 point, Bounds pool, float strength, bool splash)
        {
            ripples[next] = new Vector4(point.x, point.z, Time.time, strength);
            bounds[next] = new Vector4(pool.min.x, pool.min.z, pool.max.x, pool.max.z);
            next = (next + 1) % Capacity;
            count = Mathf.Min(count + 1, Capacity);
            Upload();
            if (splash) Spray(point, strength);
        }

        private void LateUpdate() => Upload();

        private void Upload()
        {
            Shader.SetGlobalVectorArray(DataId, ripples);
            Shader.SetGlobalVectorArray(BoundsId, bounds);
            Shader.SetGlobalInteger(CountId, count);
            Shader.SetGlobalFloat(ClockId, Time.time);
        }

        private void Spray(Vector3 point, float strength)
        {
            if (spray == null)
            {
                var material = Resources.Load<Material>("MarcoWater/Splash");
                if (material == null) return;
                var child = new GameObject("Entry Spray");
                child.transform.SetParent(transform, false);
                spray = child.AddComponent<ParticleSystem>();
                spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = spray.main;
                main.playOnAwake = false;
                main.loop = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 256;
                main.gravityModifier = .65f;
                var emission = spray.emission; emission.enabled = false;
                var shape = spray.shape; shape.enabled = false;
                var colors = spray.colorOverLifetime; colors.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(.75f, 0), new GradientAlphaKey(.5f, .4f), new GradientAlphaKey(0, 1) });
                colors.color = gradient;
                var renderer = spray.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = material;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            spray.Play();
            int drops = Mathf.RoundToInt(12 + strength * 18);
            for (int i = 0; i < drops; i++)
            {
                float angle = i * 2.399963f;
                var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                // Deterministic variation avoids perturbing gameplay's Random stream.
                float variation = .65f + (i % 7) * .055f;
                var drop = new ParticleSystem.EmitParams
                {
                    position = point + radial * .12f,
                    velocity = radial * (strength * variation) + Vector3.up * (1.3f + strength * variation * 1.8f),
                    startLifetime = .35f + variation * .35f,
                    startSize = .025f + strength * .035f * variation,
                    startColor = new Color(.7f, .93f, 1f, .8f)
                };
                spray.Emit(drop, 1);
            }
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            Shader.SetGlobalInteger(CountId, 0);
            instance = null;
        }
    }
}

