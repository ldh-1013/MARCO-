using System;
using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Audio;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Valves;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Vision
{
    // Every ring pulse this client draws (기획서 §5.3 · §5.5-1 · §5.5-3 · §16.2 · §16.4): fixed floor rings
    // for movement, expanding rings for voice / intentional / item sounds. Presentation only — the server
    // already decided who receives a ring, how far and for how long. Voice lighting is a separate system.
    // Callers include sound and voice callbacks, so nothing here may throw (RippleSafety).
    public sealed class WalkingFootRings : MonoBehaviour
    {
        [SerializeField] private Shader ringShader;
        // Stays on the old shader: clicker afterglow and decoy previews draw with it.
        public Shader RingShader => ringShader;
        private readonly MovementPulseClock clock = new();
        private readonly SoundWorld world = new();
        private readonly List<Ring> rings = new();
        private readonly List<Ring> pool = new();
        private readonly List<Request> queue = new();
        private readonly List<Afterglow> afterglows = new();
        private readonly List<Afterglow> afterglowPool = new();
        private Material material;
        private NetworkPlayer owner;
        private CharacterController ownerBody;
        private bool localMoving;
        // Rings that had to wait for the raycast budget: worst wait in rendered frames, and how many waited > 2.
        internal int MaxQueuedFrames { get; private set; }
        internal int DelayedOverTwoFrames { get; private set; }
        // Own rings (§5.3 0ms) never wait: how many were created and the worst wait in frames (always 0).
        internal int OwnRingsCreated { get; private set; }
        internal int MaxOwnWaitFrames { get; private set; }
        // Fake decoy footsteps behave exactly like one remote player's Sprint rings (D8).
        private const int DecoyStepSource = int.MinValue + 1;

        public void SetLocalMoving(bool moving) => localMoving = moving;

        private sealed class Ring
        {
            public GameObject Object;
            public Mesh Mesh;
            public Renderer Renderer;
            public MaterialPropertyBlock Properties = new();
            public readonly RippleShape Shape = new();
            public float Started, Lifetime;
            public float StoppedAt = -1;
            public int Source;
            public bool Seeker, Contact, Expand, Sound, Self;
            public Color Tint;
            public float Radius;
            // Wall reveal strip (child object, same material, _Mode 1).
            public GameObject WallObject;
            public Renderer WallRenderer;
            public Mesh WallMesh;
            public MaterialPropertyBlock WallProperties = new();
            public bool HasWall;
        }

        // Own ring-display rings leave the wall-floor lines they reached (§16.4 잔상, _Mode 2).
        private sealed class Afterglow
        {
            public GameObject Object;
            public Renderer Renderer;
            public Mesh Mesh;
            public MaterialPropertyBlock Properties = new();
            public float Started;
        }

        private enum Kind : byte { Floor, Contact, Sound }
        private enum Label : byte { Movement, Landing, Sound, Water, DecoyStep, DecoyLanding }
        private struct Request
        {
            public Kind Kind;
            public Label Label;
            public int Source, Frame;
            public Vector3 Point;
            public float Radius, Lifetime;
            public bool DoubleEdge, Self, Own;
            public Color Tint;
        }

        private void Start()
        {
            try
            {
                owner = GetComponentInParent<NetworkPlayer>();
                if (owner != null) ownerBody = owner.GetComponent<CharacterController>();
            }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.Start", exception, this); }
        }

        private void Update()
        {
            try { Tick(); }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.Update", exception, this); }
        }

        private void Tick()
        {
            if (owner == null || owner != NetworkPlayer.Local) { Clear(); return; }
            var round = NetworkRound.Instance;
            bool active = round != null && round.Mode != RoundMode.NotStarted &&
                round.Result == RoundResult.InProgress && (owner.Status == PlayerStatus.Alive || owner.HasEchoBody);
            // Maps with pool audio emit the owner's floor rings from RoundAudioPlayback; only other maps need
            // this clock (and its per-frame floor/water probes, which allocate).
            bool fast = false, emit = false;
            int water = 0;
            float level = 0;
            if (round == null || !round.UsesPoolAudio)
            {
                water = world.WaterState(owner, out level);
                bool grounded = world.TryFloorNear(owner, out _, .09f);
                var velocity = ownerBody != null ? ownerBody.velocity : Vector3.zero;
                float speed = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
                fast = MovementPulseClock.IsFastMovement(owner.Role, speed, water == 1);
                clock.Advance(owner.transform.position, Time.deltaTime, active && !owner.IsEcho && water != 2 && (grounded || water == 1), fast, out emit);
                fast = clock.Fast;
            }
            if (!active) { Clear(); return; }
            if (emit && !round.UsesPoolAudio && ownerBody != null)
            {
                var point = owner.transform.position;
                point.y = water == 1 ? level + .025f : ownerBody.bounds.min.y + .025f;
                Show(owner.PlayerId, point, fast, owner.Role == PlayerRole.Seeker);
            }
            ProcessQueue();
            float now = Time.time;
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var ring = rings[i];
                if (ring.Object == null) { Remove(i); continue; }
                if (!ring.Contact && ring.Source == owner.PlayerId)
                {
                    if (round.UsesPoolAudio ? localMoving : clock.Moving) ring.StoppedAt = -1;
                    else if (ring.StoppedAt < 0) ring.StoppedAt = now;
                    // Brief collision/network samples must not erase a ring in a single frame.
                    if (ring.StoppedAt >= 0 && now - ring.StoppedAt >= RippleVisualSettings.StopFadeSeconds) { Remove(i); continue; }
                }
                float age = now - ring.Started;
                float t = age / ring.Lifetime;
                if (t >= 1)
                {
                    if (ring.Self && ring.Sound) StartAfterglow(ring);
                    Remove(i);
                    continue;
                }
                Draw(ring, age, t, now);
            }
            DrawAfterglows(now);
        }

        // ── Entry points (called by movement, sound, voice and item code) ───────────────────────

        public void Show(int source, Vector3 point, bool fast, bool seeker)
        {
            try { RequestFloor(source, point, fast, seeker, false, Label.Movement); }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.Show", exception, this); }
        }

        // D3: a landing is drawn as a Sprint-grade floor ring; a take-off draws nothing (no SoundType for it,
        // and an expanding ring would read as a voice).
        public void ShowContact(int source, Vector3 point, bool seeker, bool landing)
        {
            try { if (landing) RequestFloor(source, point, true, seeker, true, Label.Landing); }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.ShowContact", exception, this); }
        }

        // Shared by the drowning warning and the decoy device; the radius tells the callers apart (D8).
        // Surface swimming is already emitted by the movement clock/server path; no second emitter here.
        public void EmitWaterPulse(Vector3 origin, float radius)
        {
            try
            {
                if (!CanUse() || radius <= 0) return;
                if (Mathf.Approximately(radius, DecoyConfig.FootstepRingRadius))
                    RequestFloor(DecoyStepSource, origin, true, false, false, Label.DecoyStep);
                else if (Mathf.Approximately(radius, DecoyConfig.ImpactRingRadius))
                    RequestSound(origin, radius, RippleVisualSettings.TalkSeconds, RippleVisualSettings.Runner, false, false, Label.DecoyLanding);
                else
                {
                    Classify(origin, out var tint, out bool doubleEdge);
                    RequestSound(origin, radius, RippleVisualSettings.ShoutSeconds, tint, doubleEdge, IsSelf(origin), Label.Water);
                }
            }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.EmitWaterPulse", exception, this); }
        }

        // Sound-can replay: a voice ring in the Runner colour with the server's radius and duration (D8).
        public void EmitSoundPulse(Vector3 origin, float radius, float duration)
        {
            try
            {
                if (!CanUse() || radius <= 0 || duration <= 0) return;
                RequestSound(origin, radius, duration, RippleVisualSettings.Runner, false, false, Label.Sound);
            }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.EmitSoundPulse", exception, this); }
        }

        public void ClearPulses()
        {
            try { Clear(); }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.ClearPulses", exception, this); }
        }

        public void ShowSound(Vector3 point, float radius, float duration, bool? voiceFromSeeker = null)
        {
            try
            {
                if (!CanUse() || radius <= 0 || duration <= 0) return;
                Color tint;
                bool doubleEdge;
                if (voiceFromSeeker.HasValue)
                {
                    doubleEdge = voiceFromSeeker.Value;
                    tint = doubleEdge ? RippleVisualSettings.Seeker : RippleVisualSettings.Runner;
                }
                else Classify(point, out tint, out doubleEdge);
                RequestSound(point, radius, duration, tint, doubleEdge, IsSelf(point), Label.Sound);
            }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.ShowSound", exception, this); }
        }

        // ── Requests and the raycast budget ───────────────────────────────

        private bool CanUse() => owner != null && owner == NetworkPlayer.Local && RippleMesh.CanRender();

        private void RequestFloor(int source, Vector3 point, bool fast, bool seeker, bool contact, Label label)
        {
            if (!CanUse()) return;
            // Walking floor marks are owner-only, including accidental remote calls.
            if (!fast && !contact && source != owner.PlayerId) return;
            Submit(new Request
            {
                Kind = contact ? Kind.Contact : Kind.Floor, Label = label, Source = source, Point = point,
                Radius = fast ? RippleVisualSettings.SprintRingRadius : RippleVisualSettings.WalkRingRadius,
                Lifetime = fast ? RippleVisualSettings.SprintRingSeconds : RippleVisualSettings.WalkRingSeconds,
                DoubleEdge = seeker, Tint = seeker ? RippleVisualSettings.Seeker : RippleVisualSettings.Runner,
                Frame = Time.frameCount
            }, source == owner.PlayerId);
        }

        private void RequestSound(Vector3 point, float radius, float duration, Color tint, bool doubleEdge, bool self, Label label)
        {
            Submit(new Request
            {
                Kind = Kind.Sound, Label = label, Source = int.MinValue, Point = point, Radius = radius, Lifetime = duration,
                DoubleEdge = doubleEdge, Self = self, Tint = tint, Frame = Time.frameCount
            }, self);
        }

        // Own rings render at once (§5.3 0ms). Others wait in order when this frame's raycasts are spent,
        // and appear only when fully built — never late in parts.
        private void Submit(Request request, bool own)
        {
            request.Own = own;
            if (own || (queue.Count == 0 && RippleBudget.Remaining >= RippleReach.Estimate(world, request.Point, request.Radius)))
                Create(request);
            else queue.Add(request);
        }

        private void ProcessQueue()
        {
            while (queue.Count > 0)
            {
                var request = queue[0];
                if (RippleBudget.Remaining < RippleReach.Estimate(world, request.Point, request.Radius)) return;
                queue.RemoveAt(0);
                int waited = Time.frameCount - request.Frame;
                if (waited > MaxQueuedFrames) MaxQueuedFrames = waited;
                if (waited > 2) DelayedOverTwoFrames++;
                Create(request);
            }
        }

        // ── Ring creation and drawing ───────────────────────────────

        private void Create(Request request)
        {
            if (!CanUse()) return;
            if (request.Own)
            {
                OwnRingsCreated++;
                MaxOwnWaitFrames = Mathf.Max(MaxOwnWaitFrames, Time.frameCount - request.Frame);
            }
            bool sound = request.Kind == Kind.Sound;
            if (sound)
            {
                int cap = owner.HasEchoBody ? RippleVisualSettings.EchoSoundRingCap : RippleVisualSettings.SoundRingCap;
                while (true)
                {
                    int count = 0, oldest = -1;
                    for (int i = 0; i < rings.Count; i++)
                        if (rings[i].Sound) { count++; if (oldest < 0) oldest = i; }
                    if (count < cap || oldest < 0) break;
                    Remove(oldest);
                }
            }
            else
            {
                for (int i = rings.Count - 1; i >= 0; i--)
                    if (!rings[i].Sound && rings[i].Source == request.Source) Remove(i);
                if (request.Source != owner.PlayerId)
                {
                    int remote = 0, oldest = -1;
                    for (int i = 0; i < rings.Count; i++)
                        if (!rings[i].Sound && rings[i].Source != owner.PlayerId) { remote++; if (oldest < 0) oldest = i; }
                    if (remote >= RippleVisualSettings.RemoteFloorRingCap) Remove(oldest);
                }
            }
            if (!EnsureMaterial()) return;
            var ring = Acquire();
            try
            {
                int before = world.VisualQueryCount;
                RippleReach.Build(world, request.Point, request.Radius, ring.Shape);
                RippleBudget.Spend(world.VisualQueryCount - before);
                bool floor = !sound;
                RippleMesh.Write(ring.Mesh, ring.Shape, floor
                    ? RippleVisualSettings.FloorCoreWidth + RippleVisualSettings.FloorTrailWidth
                    : RippleVisualSettings.CoreWidth + RippleVisualSettings.TrailWidth);
                RippleMesh.ApplyShape(ring.Properties, ring.Shape, request.DoubleEdge);
                // Walls light up only for ring-display rings, never for an Echo listener (§3.2-1).
                ring.HasWall = sound && !owner.IsEcho && RippleMesh.WriteWall(ring.WallMesh);
                if (ring.HasWall)
                {
                    RippleMesh.ApplyShape(ring.WallProperties, ring.Shape, false);
                    ring.WallProperties.SetFloat(RippleMesh.Mode, 1f);
                }
                ring.WallObject.SetActive(ring.HasWall);
                ring.Object.name = Name(request.Label, request.Source);
                ring.Object.transform.position = request.Point;
                ring.Started = Time.time; ring.Lifetime = request.Lifetime; ring.StoppedAt = -1;
                ring.Source = request.Source; ring.Seeker = request.DoubleEdge; ring.Contact = request.Kind == Kind.Contact;
                ring.Radius = request.Radius; ring.Sound = sound; ring.Expand = sound; ring.Self = request.Self;
                ring.Tint = request.Tint;
                Draw(ring, 0, 0, ring.Started);
                ring.Object.SetActive(true);
                rings.Add(ring);
            }
            catch
            {
                if (ring.Object != null) ring.Object.SetActive(false);
                pool.Add(ring);
                throw;
            }
        }

        private static string Name(Label label, int source) => label switch
        {
            Label.Landing => "Landing Floor Pulse " + source,
            Label.Sound => "Sound Pulse",
            Label.Water => "Water Warning Pulse",
            Label.DecoyStep => "Decoy Floor Pulse",
            Label.DecoyLanding => "Decoy Landing Pulse",
            _ => "Movement Floor Pulse " + source
        };

        private void Draw(Ring ring, float age, float t, float now)
        {
            float fadeIn = Mathf.Clamp01(age / Mathf.Max(.001f, Mathf.Min(RippleVisualSettings.FadeInSeconds, ring.Lifetime * .25f)));
            float width = ring.Self ? RippleVisualSettings.SelfWidth : 1f;
            float alpha, radius, core, trail;
            if (ring.Expand)
            {
                radius = Mathf.Lerp(RippleVisualSettings.StartRadius, ring.Radius, 1f - Mathf.Pow(1f - t, RippleVisualSettings.ExpandPower));
                alpha = RippleVisualSettings.MaxAlpha * fadeIn * Mathf.Pow(1f - t, RippleVisualSettings.FadePower);
                float thin = Mathf.Lerp(1f, RippleVisualSettings.ThinAtEnd, t);
                core = RippleVisualSettings.CoreWidth * width * thin;
                trail = RippleVisualSettings.TrailWidth * width * thin;
            }
            else
            {
                radius = ring.Radius;
                float stop = ring.StoppedAt < 0 ? 1 : Mathf.Clamp01(1 - (now - ring.StoppedAt) / RippleVisualSettings.StopFadeSeconds);
                alpha = RippleVisualSettings.FloorAlpha * fadeIn * (1f - t) * stop;
                core = RippleVisualSettings.FloorCoreWidth * width;
                trail = RippleVisualSettings.FloorTrailWidth * width;
            }
            if (ring.HasWall)
            {
                float wall = alpha * RippleVisualSettings.WallRevealBrightness * (ring.Self ? RippleVisualSettings.SelfWallRevealBrightness : 1f);
                RippleMesh.ApplyFrame(ring.WallProperties, ring.Tint, wall, radius, core, trail);
                ring.WallRenderer.SetPropertyBlock(ring.WallProperties);
            }
            if (ring.Self) alpha *= RippleVisualSettings.SelfAlphaClamped;
            RippleMesh.ApplyFrame(ring.Properties, ring.Tint, alpha, radius, core, trail);
            ring.Renderer.SetPropertyBlock(ring.Properties);
        }

        private void StartAfterglow(Ring ring)
        {
            if (material == null || ring.Object == null) return;
            while (afterglows.Count >= RippleVisualSettings.AfterglowCap) RemoveAfterglow(0);
            Afterglow glow = null;
            while (afterglowPool.Count > 0 && glow == null)
            {
                var pooled = afterglowPool[afterglowPool.Count - 1];
                afterglowPool.RemoveAt(afterglowPool.Count - 1);
                if (pooled.Object != null) glow = pooled;
                else if (pooled.Mesh != null) Destroy(pooled.Mesh);
            }
            if (glow == null)
            {
                var go = new GameObject("Ripple Afterglow");
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                var mesh = new Mesh { name = "Ripple Afterglow" };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                glow = new Afterglow { Object = go, Renderer = renderer, Mesh = mesh };
            }
            // Same shape, no new raycasts: only the wall-stopped ends are drawn in _Mode 2.
            RippleMesh.Write(glow.Mesh, ring.Shape, RippleVisualSettings.CoreWidth);
            RippleMesh.ApplyShape(glow.Properties, ring.Shape, false);
            glow.Properties.SetFloat(RippleMesh.Mode, 2f);
            glow.Object.transform.position = ring.Object.transform.position;
            glow.Started = Time.time;
            afterglows.Add(glow);
            DrawAfterglow(glow, 0f);
            glow.Object.SetActive(true);
        }

        private void DrawAfterglows(float now)
        {
            for (int i = afterglows.Count - 1; i >= 0; i--)
            {
                var glow = afterglows[i];
                float age = now - glow.Started;
                if (glow.Object == null || age >= RippleVisualSettings.AfterglowSeconds) { RemoveAfterglow(i); continue; }
                DrawAfterglow(glow, age);
            }
        }

        // 15% -> 0% linear over 12 s (부록 A [잔상]); a short fade-in hides the hand-over from the ring.
        private static void DrawAfterglow(Afterglow glow, float age)
        {
            float alpha = RippleVisualSettings.AfterglowAlpha * Mathf.Clamp01(age / RippleVisualSettings.AfterglowFadeIn) *
                (1f - age / RippleVisualSettings.AfterglowSeconds);
            RippleMesh.ApplyFrame(glow.Properties, RippleVisualSettings.EnvironmentColor, alpha, 1e4f,
                RippleVisualSettings.CoreWidth, 0f);
            glow.Renderer.SetPropertyBlock(glow.Properties);
        }

        private void RemoveAfterglow(int i)
        {
            var glow = afterglows[i];
            afterglows.RemoveAt(i);
            if (glow.Object == null) { if (glow.Mesh != null) Destroy(glow.Mesh); return; }
            glow.Object.SetActive(false);
            afterglowPool.Add(glow);
        }

        private bool EnsureMaterial()
        {
            if (material != null) return true;
            var shader = RippleMesh.LoadShader();
            if (shader == null) shader = ringShader != null ? ringShader : Shader.Find("Marco/WalkingFootRing");
            if (shader == null) return false;
            material = new Material(shader) { name = "Ripple Ring", renderQueue = (int)RenderQueue.Transparent };
            return true;
        }

        private Ring Acquire()
        {
            while (pool.Count > 0)
            {
                var pooled = pool[pool.Count - 1];
                pool.RemoveAt(pool.Count - 1);
                if (pooled.Object != null) return pooled;
                if (pooled.Mesh != null) Destroy(pooled.Mesh);
                if (pooled.WallMesh != null) Destroy(pooled.WallMesh);
            }
            // Root object, no collider or rigidbody: rings never take part in physics or judgement.
            var go = new GameObject("Ripple Ring");
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var mesh = new Mesh { name = "Ripple Ring" };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var wall = new GameObject("Ripple Wall Reveal");
            wall.transform.SetParent(go.transform, false);
            var wallRenderer = wall.AddComponent<MeshRenderer>();
            wallRenderer.sharedMaterial = material;
            wallRenderer.shadowCastingMode = ShadowCastingMode.Off;
            wallRenderer.receiveShadows = false;
            wallRenderer.lightProbeUsage = LightProbeUsage.Off;
            wallRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var wallMesh = new Mesh { name = "Ripple Wall Reveal" };
            wall.AddComponent<MeshFilter>().sharedMesh = wallMesh;
            wall.SetActive(false);
            go.SetActive(false);
            return new Ring { Object = go, Renderer = renderer, Mesh = mesh, WallObject = wall, WallRenderer = wallRenderer, WallMesh = wallMesh };
        }

        // ── Ownership rules (D1) and colour (D5) ───────────────────────────────

        // A ring is the local player's own when it starts at the local player's position. Valve rings never are.
        private bool IsSelf(Vector3 point)
        {
            if (owner == null || owner.Status != PlayerStatus.Alive || owner.IsEcho) return false;
            Vector3 position = owner.transform.position;
            float dx = point.x - position.x, dz = point.z - position.z;
            if (dx * dx + dz * dz > RippleVisualSettings.SelfDistance * RippleVisualSettings.SelfDistance) return false;
            float feet = ownerBody != null ? ownerBody.bounds.min.y : position.y - 1f;
            if (point.y < feet - .5f || point.y > feet + 2.5f) return false;
            return !NearValve(point);
        }

        // Server-path rings carry no source role: colour them by the nearest living, non-Echo player within
        // 1m; valves and anything else are environment rings.
        private static void Classify(Vector3 point, out Color tint, out bool doubleEdge)
        {
            tint = RippleVisualSettings.EnvironmentColor;
            doubleEdge = false;
            if (NearValve(point)) return;
            NetworkPlayer best = null;
            float bestDistance = RippleVisualSettings.CharacterColorDistance * RippleVisualSettings.CharacterColorDistance;
            var players = NetworkPlayer.All;
            for (int i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (player == null || player.Status != PlayerStatus.Alive) continue;
                Vector3 position = player.transform.position;
                if (Mathf.Abs(point.y - position.y) > 2f) continue;
                float dx = point.x - position.x, dz = point.z - position.z;
                float distance = dx * dx + dz * dz;
                if (distance <= bestDistance) { bestDistance = distance; best = player; }
            }
            if (best == null) return;
            doubleEdge = best.Role == PlayerRole.Seeker;
            tint = doubleEdge ? RippleVisualSettings.Seeker : RippleVisualSettings.Runner;
        }

        private static bool NearValve(Vector3 point)
        {
            float limit = RippleVisualSettings.ValveExclusionDistance * RippleVisualSettings.ValveExclusionDistance;
            foreach (var valve in ValveView.All)
                if (valve != null && (valve.transform.position - point).sqrMagnitude <= limit) return true;
            return false;
        }

        // ── Checks and cleanup ───────────────────────────────

        // Used by MovementPulseChecks: the fully expanded floor ring around origin, as a new mesh.
        private Mesh BuildMesh(Vector3 origin, float radius, bool doubleEdge)
        {
            var shape = new RippleShape();
            RippleReach.Build(world, origin, radius, shape);
            var mesh = new Mesh { name = "Ripple Ring" };
            RippleMesh.Write(mesh, shape, RippleVisualSettings.FloorCoreWidth + RippleVisualSettings.FloorTrailWidth);
            return mesh;
        }

        private void Remove(int i)
        {
            var ring = rings[i];
            rings.RemoveAt(i);
            if (ring.Object == null)
            {
                if (ring.Mesh != null) Destroy(ring.Mesh);
                if (ring.WallMesh != null) Destroy(ring.WallMesh);
                return;
            }
            ring.Object.SetActive(false);
            pool.Add(ring);
        }

        private void Clear()
        {
            queue.Clear();
            for (int i = rings.Count - 1; i >= 0; i--) Remove(i);
            for (int i = afterglows.Count - 1; i >= 0; i--) RemoveAfterglow(i);
        }

        private void OnDisable()
        {
            try { Clear(); }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.OnDisable", exception, this); }
        }

        private void OnDestroy()
        {
            try
            {
                Clear();
                foreach (var ring in pool)
                {
                    if (ring.Object != null) Destroy(ring.Object);
                    if (ring.Mesh != null) Destroy(ring.Mesh);
                    if (ring.WallMesh != null) Destroy(ring.WallMesh);
                }
                pool.Clear();
                foreach (var glow in afterglowPool)
                {
                    if (glow.Object != null) Destroy(glow.Object);
                    if (glow.Mesh != null) Destroy(glow.Mesh);
                }
                afterglowPool.Clear();
                if (material != null) Destroy(material);
            }
            catch (Exception exception) { RippleSafety.Report("WalkingFootRings.OnDestroy", exception, this); }
        }
    }
}
