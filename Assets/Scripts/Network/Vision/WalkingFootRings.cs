using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Audio;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Vision
{
    // Floor-only movement marks (§5.5-3). Voice, clicker and environment lighting stay independent.
    public sealed class WalkingFootRings : MonoBehaviour
    {
        [SerializeField] private Shader ringShader;
        public Shader RingShader => ringShader;
        private readonly MovementPulseClock clock = new();
        private readonly SoundWorld world = new();
        private readonly List<Ring> rings = new();
        private Material material;
        private readonly List<Ring> pool = new();
        private NetworkPlayer owner;
        private bool localMoving;
        public void SetLocalMoving(bool moving) => localMoving = moving;
        private sealed class Ring
        {
            public GameObject Object;
            public Mesh Mesh;
            public Renderer Renderer;
            public MaterialPropertyBlock Properties = new();
            public float Started, Lifetime;
            public float StoppedAt = -1;
            public int Source;
            public bool Seeker, Contact, Expand, Sound;
            public Color Tint;
            public float Radius;
            public Vector3[][] Paths;
            public Vector3[] Vertices;
        }
        private void Start() => owner = GetComponentInParent<NetworkPlayer>();
        private void Update()
        {
            if (owner == null || owner != NetworkPlayer.Local) { Clear(); return; }
            var round = NetworkRound.Instance;
            bool active = round != null && round.Mode != RoundMode.NotStarted &&
                round.Result == RoundResult.InProgress && (owner.Status == PlayerStatus.Alive || owner.HasEchoBody);
            int water = world.WaterState(owner, out float level);
            bool grounded = world.TryFloorNear(owner, out var floor, .09f);
            var velocity = owner.GetComponent<CharacterController>()?.velocity ?? Vector3.zero;
            float speed = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
            bool fast = MovementPulseClock.IsFastMovement(owner.Role, speed, water == 1);
            clock.Advance(owner.transform.position, Time.deltaTime, active && !owner.IsEcho && water != 2 && (grounded || water == 1), fast, out bool emit);
            fast = clock.Fast;
            if (!active) { Clear(); return; }
            if (emit && !round.UsesPoolAudio)
            {
                var point = owner.transform.position;
                point.y = water == 1 ? level + .025f : owner.GetComponent<CharacterController>().bounds.min.y + .025f;
                Show(owner.PlayerId, point, fast, owner.Role == PlayerRole.Seeker);
            }
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var ring = rings[i];
                if (ring.Object == null) { Remove(i); continue; }
                if (!ring.Contact && ring.Source == owner.PlayerId)
                {
                    if (round.UsesPoolAudio ? localMoving : clock.Moving) ring.StoppedAt = -1;
                    else if (ring.StoppedAt < 0) ring.StoppedAt = Time.time;
                    // Brief collision/network samples must not erase a ring in a single frame.
                    if (ring.StoppedAt >= 0 && Time.time-ring.StoppedAt >= .12f) { Remove(i); continue; }
                }
                float t = (Time.time - ring.Started) / ring.Lifetime;
                if (t >= 1) { Remove(i); continue; }
                if (ring.Expand)
                    UpdateWave(ring.Mesh, ring.Vertices, ring.Paths, ring.Radius,
                        Mathf.Lerp(.03f, ring.Radius, 1f - (1f-t)*(1f-t)), ring.Seeker);
                Color color = ring.Tint;
                float stopFade = ring.StoppedAt < 0 ? 1 : Mathf.Clamp01(1-(Time.time-ring.StoppedAt)/.12f);
                ring.Properties.SetColor("_BaseColor", new Color(color.r, color.g, color.b, .8f * (1-t) * stopFade));
                ring.Renderer.SetPropertyBlock(ring.Properties);
            }
        }
        private static Color ringColor(bool seeker) => seeker ? new Color(.94f,.42f,.30f) : new Color(.50f,.91f,.88f);
        public void Show(int source, Vector3 point, bool fast, bool seeker) =>
            ShowInternal(source, point, fast, seeker, false);

        public void ShowContact(int source, Vector3 point, bool seeker, bool landing)
        {
            var ring = ShowInternal(source, point, landing, seeker, true);
            if (ring != null) ring.Object.name = (landing ? "Landing Floor Pulse " : "Jump Floor Pulse ") + source;
        }

        // Shared with the incoming drowning warning; surface swimming is already emitted by
        // the movement clock/server path, so do not add a second swimming emitter here.
        public void EmitWaterPulse(Vector3 origin, float radius)
        {
            var ring = ShowInternal(int.MinValue, origin, true, false, true, radius);
            if (ring != null) { ring.Lifetime = .8f; ring.Object.name = "Water Warning Pulse"; }
        }

        // Item callers share the new sound-wave renderer and pooled cleanup.
        public void EmitSoundPulse(Vector3 origin, float radius, float duration) => ShowSound(origin, radius, duration);
        public void ClearPulses() => Clear();

        public void ShowSound(Vector3 point, float radius, float duration, bool? voiceFromSeeker = null)
        {
            if (radius <= 0 || duration <= 0) return;
            var ring = ShowInternal(int.MinValue, point, true, false, true, radius, true, voiceFromSeeker);
            if (ring == null) return;
            ring.Lifetime = duration;
            ring.Object.name = "Sound Pulse";
        }

        private Ring ShowInternal(int source, Vector3 point, bool fast, bool seeker, bool contact,
            float radiusOverride = 0, bool sound = false, bool? voiceFromSeeker = null)
        {
            if (owner == null || owner != NetworkPlayer.Local) return null;
            // Walking floor marks are owner-only, including accidental remote calls.
            // Contact rings have their own audible takeoff/landing audience.
            if (!fast && !contact && source != owner.PlayerId) return null;
            if (sound)
            {
                int count = 0, oldest = -1;
                for (int i = 0; i < rings.Count; i++)
                    if (rings[i].Sound) { count++; if (oldest < 0) oldest = i; }
                if (count >= (owner.HasEchoBody ? 8 : 16)) Remove(oldest);
            }
            else
                for (int i = rings.Count-1; i >= 0; i--)
                    if (!rings[i].Sound && rings[i].Source == source) Remove(i);
            if (!sound && source != owner.PlayerId)
            {
                int remote=0, oldest=-1;
                for (int i=0; i<rings.Count; i++) if (!rings[i].Sound && rings[i].Source != owner.PlayerId) { remote++; if(oldest<0) oldest=i; }
                if (remote >= 3) Remove(oldest);
            }
            if (material == null)
            {
                var shader = ringShader != null ? ringShader : Shader.Find("Marco/WalkingFootRing");
                if (shader == null) return null;
                material = new Material(shader) { name = "Movement Floor Pulse" };
            }
            Ring ring;
            if (pool.Count > 0)
            {
                ring = pool[pool.Count-1]; pool.RemoveAt(pool.Count-1);
            }
            else
            {
                var go = new GameObject();
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                go.AddComponent<MeshFilter>();
                ring = new Ring { Object = go, Renderer = renderer };
            }
            if (ring.Object == null)
            {
                if (ring.Mesh != null) Destroy(ring.Mesh);
                return ShowInternal(source, point, fast, seeker, contact, radiusOverride, sound, voiceFromSeeker);
            }
            ring.Object.name = "Movement Floor Pulse " + source;
            ring.Object.transform.position = point;
            float radius = radiusOverride > 0 ? radiusOverride : fast ? 5f : 1.5f;
            ring.Paths = sound ? BuildSoundPaths(point, radius, ring.Paths) : BuildPathsInto(point, radius, ring.Paths);
            int vertexCount = (Segments+1)*2*(seeker ? 2 : 1);
            if (ring.Mesh == null || ring.Vertices.Length != vertexCount)
            {
                if (ring.Mesh != null) Destroy(ring.Mesh);
                ring.Mesh = CreateMesh(seeker); ring.Vertices = ring.Mesh.vertices;
                ring.Object.GetComponent<MeshFilter>().sharedMesh = ring.Mesh;
            }
            ring.Started = Time.time; ring.Lifetime = fast ? .4f : .5f; ring.StoppedAt = -1;
            ring.Source = source; ring.Seeker = seeker; ring.Contact = contact; ring.Radius = radius;
            ring.Sound = sound;
            ring.Expand = contact;
            UpdateWave(ring.Mesh, ring.Vertices, ring.Paths, radius, ring.Expand ? .03f : radius, seeker);
            ring.Tint = voiceFromSeeker switch
            {
                true => new Color(.30f, .63f, 1f),
                false => new Color(1f, .87f, .20f),
                null => ringColor(seeker)
            };
            var color = ring.Tint;
            color.a = .8f;
            ring.Properties.SetColor("_BaseColor", color); ring.Renderer.SetPropertyBlock(ring.Properties);
            ring.Object.SetActive(true); rings.Add(ring);
            return ring;
        }
        private const int Segments = 64;

        private Vector3[][] BuildSoundPaths(Vector3 origin, float radius, Vector3[][] paths)
        {
            paths ??= new Vector3[Segments + 1][];
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2 / Segments;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                if (paths[i] == null || paths[i].Length != 2) paths[i] = new Vector3[2];
                paths[i][0] = Vector3.zero;
                paths[i][1] = direction * world.VisualReach(origin + Vector3.up * .06f, direction, radius);
            }
            paths[Segments] = paths[0];
            return paths;
        }
        private Mesh BuildMesh(Vector3 origin, float radius, bool doubleEdge)
        {
            var mesh = CreateMesh(doubleEdge);
            UpdateWave(mesh, mesh.vertices, BuildPaths(origin, radius), radius, radius, doubleEdge);
            return mesh;
        }
        private Vector3[][] BuildPaths(Vector3 origin, float radius) => BuildPathsInto(origin, radius, null);
        private Vector3[][] BuildPathsInto(Vector3 origin, float radius, Vector3[][] paths)
        {
            int samples = Mathf.CeilToInt(radius / .5f);
            paths ??= new Vector3[Segments + 1][];
            bool onFloor = world.VisualFloor(origin, out var floorOrigin);
            if (!onFloor) floorOrigin = origin;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i * Mathf.PI * 2 / Segments;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var path = paths[i];
                if (path == null || path.Length != samples+1) path = paths[i] = new Vector3[samples+1];
                Vector3 previous = floorOrigin;
                path[0] = previous - origin;
                bool stopped = false;
                for (int j = 1; j <= samples; j++)
                {
                    Vector3 next = origin + direction * (radius * j / samples);
                    next.y = previous.y;
                    if (!stopped && onFloor && !world.VisualFloor(next, out next)) stopped = true;
                    if (!stopped)
                    {
                        Vector3 delta = next - previous;
                        float length = delta.magnitude;
                        if (length > .0001f)
                        {
                            float reach = world.VisualReach(previous + Vector3.up * .1f, delta / length, length);
                            if (reach < length - .001f)
                            {
                                next = previous + delta / length * Mathf.Max(0, reach - .025f);
                                stopped = true;
                            }
                        }
                        previous = next;
                    }
                    path[j] = previous - origin;
                }
            }
            paths[Segments] = paths[0]; // Reuse the seam, including its floor/occlusion samples.
            return paths;
        }
        private static Mesh CreateMesh(bool doubleEdge)
        {
            int bands = doubleEdge ? 2 : 1;
            var triangles = new int[Segments * 6 * bands];
            for (int band = 0; band < bands; band++) for (int i = 0; i < Segments; i++)
            {
                int v = band * (Segments+1)*2 + i*2;
                int t = band*Segments*6+i*6;
                triangles[t]=v; triangles[t+1]=v+1; triangles[t+2]=v+2;
                triangles[t+3]=v+1; triangles[t+4]=v+3; triangles[t+5]=v+2;
            }
            var mesh = new Mesh { name = "Expanding Wall Clipped Floor Pulse" };
            mesh.MarkDynamic();
            mesh.vertices = new Vector3[(Segments+1)*2*bands];
            mesh.triangles = triangles;
            return mesh;
        }
        private static Vector3 PointAt(Vector3[] path, float distance, float radius)
        {
            float sample = Mathf.Clamp01(distance / radius) * (path.Length-1);
            int index = Mathf.Min(Mathf.FloorToInt(sample), path.Length-2);
            return Vector3.Lerp(path[index], path[index+1], sample-index);
        }
        private static void UpdateWave(Mesh mesh, Vector3[] vertices, Vector3[][] paths,
            float radius, float current, bool doubleEdge)
        {
            int bands = doubleEdge ? 2 : 1;
            for (int band=0; band<bands; band++) for (int i=0; i<=Segments; i++)
            {
                float outer = Mathf.Max(0,current-band*.13f);
                int v=band*(Segments+1)*2+i*2;
                vertices[v]=PointAt(paths[i],Mathf.Max(0,outer-.045f),radius);
                vertices[v+1]=PointAt(paths[i],outer,radius);
            }
            mesh.vertices=vertices; mesh.RecalculateBounds();
        }
        private void Remove(int i)
        {
            var ring = rings[i]; rings.RemoveAt(i);
            if (ring.Object == null)
            {
                if (ring.Mesh != null) Destroy(ring.Mesh);
                return;
            }
            ring.Object.SetActive(false); pool.Add(ring);
        }
        private void Clear() { for(int i=rings.Count-1;i>=0;i--)Remove(i); }
        private void OnDisable()=>Clear();
        private void OnDestroy()
        {
            Clear();
            foreach (var ring in pool) { Destroy(ring.Object); if (ring.Mesh != null) Destroy(ring.Mesh); }
            pool.Clear(); if (material != null) Destroy(material);
        }
    }
}
