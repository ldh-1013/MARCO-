using System;
using System.Collections.Generic;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Marco.Network.Round.ResultPodium
{
    public enum PodiumBody : byte { Runner, Echo, Seeker }

    // One participant as the result screen shows them. Built from synced role/status only.
    public struct PodiumEntry
    {
        public string Name;
        public PodiumBody Body;
        public bool Won;
        public string Outcome;
        public Color OutcomeColor;
        public bool IsLocal;
        // Matches award winners (PodiumAward.WinnerIds) to the character under which their badges go.
        public int PlayerId;
    }

    // 표시 전용. Builds the result podium far below the map (Y=-20000, apart from the lobby CCTV at
    // -10000) on its own layer and films it into a RenderTexture: a dark stage, the winning side's banner,
    // everyone in one row with win/lose poses, their name/result and award badges under them, and the three
    // award cards below (ResultPodiumStage.Awards). Each client builds it locally from what it already
    // knows (synced role/status and the round's awards); nothing here is networked.
    // Tick drives everything from one clock, so the editor check can sample any moment.
    public sealed partial class ResultPodiumStage : IDisposable
    {
        public const int Layer = 30;
        public static readonly Vector3 Origin = new(0, -20000, 0);
        public static readonly Color RunnersColor = new(0.2f, 0.9f, 1f);   // 기획서 12.5 도망자 = 시안
        public static readonly Color SeekersColor = new(0.95f, 0.2f, 0.2f); // 기획서 12.5 술래 = 레드
        public static readonly Color VoidColor = new(0.72f, 0.74f, 0.8f);

        // Show order (seconds from the moment the round was decided).
        private const float BannerPop = .55f, ActorsStart = .85f, Beat = .34f, DropTime = .32f, InfoPop = .28f;
        private const float RiserHeight = .22f, RiserScale = .62f, LabelZ = -.75f, SeekerHeight = 1.9f;
        private const float BannerY = 3.15f, BannerHeight = .78f, LabelTop = -.28f, LabelBottom = -1.25f;
        private const float FieldOfView = 30f, Pitch = 7f;

        public Camera Camera { get; private set; }
        public RenderTexture Feed { get; private set; }
        public float Time { get; private set; }
        public float VoteRevealTime { get; }
        public bool IsValid => root != null;

        private readonly GameObject root;
        private readonly RoundResult result;
        private readonly Color team;
        private readonly List<Actor> actors = new();
        private readonly List<Material> materials = new();
        private readonly List<Confetto> confetti = new();
        private readonly Transform banner, bannerRibbon, rays;
        private readonly float confettiAt;
        private float cameraDistance, cameraHeight, rowWidth;
        private Mesh quad, cylinder;
        private Material surfaceTemplate;
        private Shader glowShader;
        private static Font font;

        public ResultPodiumStage(RoundResult result, IReadOnlyList<PodiumEntry> entries, ResultPodiumAssets assets,
            int width, int height, IReadOnlyList<PodiumAward> awards = null)
        {
            this.result = result;
            team = result switch { RoundResult.RunnersWin => RunnersColor, RoundResult.SeekersWin => SeekersColor, _ => VoidColor };
            var surface = Resources.Load<Shader>("ResultPodium/ResultPodiumSurface");
            glowShader = Resources.Load<Shader>("ResultPodium/ResultPodiumGlow");
            if (surface == null || glowShader == null) { Debug.LogError("[MARCO][Result] Podium shaders missing in Resources/ResultPodium."); return; }
            surfaceTemplate = new Material(surface) { name = "Result podium surface" };
            materials.Add(surfaceTemplate);
            quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

            root = new GameObject("Result podium (display only)");
            root.transform.position = Origin;
            BuildSet();
            rays = BuildRays();
            (banner, bannerRibbon) = BuildBanner(ResultTitle(result));

            // Winners in the middle (on risers), losers out to the sides; the Demon gets a wider slot for
            // its arms. Reveal from the edges inward so the winners land last.
            var order = new List<PodiumEntry>(entries);
            order.Sort((a, b) => b.Won.CompareTo(a.Won));
            int count = order.Count;
            var slots = RowPositions(order, out rowWidth);
            for (int i = 0; i < count; i++)
            {
                var actor = BuildActor(order[i], assets, slots[i], SlotWidth(order[i].Body));
                if (actor != null) actors.Add(actor);
            }
            actors.Sort((a, b) => Mathf.Abs(b.X).CompareTo(Mathf.Abs(a.X)));
            for (int i = 0; i < actors.Count; i++)
            {
                actors[i].LandTime = ActorsStart + i * Beat + DropTime;
                actors[i].InfoTime = ActorsStart + actors.Count * Beat + DropTime + .15f + i * .07f;
            }
            float infoDone = ActorsStart + actors.Count * Beat + DropTime + .15f + actors.Count * .07f + InfoPop;
            VoteRevealTime = Mathf.Min(infoDone + .2f, 3.6f);
            confettiAt = actors.Count > 0 ? actors[actors.Count - 1].LandTime : ActorsStart;
            if (result != RoundResult.Void) BuildConfetti();

            BuildCamera(width, height);
            SetLayer(root.transform);
            if (awards != null) SetAwards(awards);
            Tick(0f);
        }

        // ---------------------------------------------------------------- set dressing

        private void BuildSet()
        {
            // Backdrop: near-black at the floor fading up into a hint of the winning colour.
            var backdrop = Quad("Backdrop", new Vector3(0, 4, 6), new Vector3(40, 16, 1), Shape(1, team * .16f + new Color(.02f, .02f, .05f), new Color(.012f, .014f, .03f, 1)));
            backdrop.rotation = Quaternion.identity;
            // Floor pool of light behind the row.
            var floor = Quad("Floor glow", new Vector3(0, -.005f, 1.2f), new Vector3(16, 9, 1), Shape(0, new Color(.16f, .19f, .3f, .55f), Color.clear));
            floor.rotation = Quaternion.Euler(90, 0, 0);
        }

        private Transform BuildRays()
        {
            var pivot = new GameObject("Rays").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0, BannerY, 5.5f);
            var material = Shape(4, new Color(team.r, team.g, team.b, result == RoundResult.Void ? .03f : .075f), Color.clear);
            for (int i = 0; i < 12; i++)
            {
                var arm = new GameObject("Ray").transform;
                arm.SetParent(pivot, false);
                arm.localRotation = Quaternion.Euler(0, 0, i * 30f);
                var ray = Quad("Beam", Vector3.zero, new Vector3(1.6f, 14f, 1), material, arm);
                ray.localPosition = new Vector3(0, 7f, 0);
            }
            return pivot;
        }

        private (Transform banner, Transform ribbon) BuildBanner(string title)
        {
            var pivot = new GameObject("Banner").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0, BannerY, -.2f);
            float textWidth = EstimateWidth(title, BannerHeight);
            var ribbon = new GameObject("Ribbon").transform;
            ribbon.SetParent(pivot, false);
            Quad("Ribbon fill", new Vector3(0, 0, .06f), new Vector3(textWidth + 2.4f, BannerHeight * 1.45f, 1),
                Shape(2, new Color(team.r * .22f, team.g * .22f, team.b * .22f, .92f), Color.clear), ribbon);
            var line = Shape(2, new Color(team.r, team.g, team.b, .95f), Color.clear);
            Quad("Ribbon top", new Vector3(0, BannerHeight * .74f, .05f), new Vector3(textWidth + 2.8f, .05f, 1), line, ribbon);
            Quad("Ribbon bottom", new Vector3(0, -BannerHeight * .74f, .05f), new Vector3(textWidth + 2.8f, .05f, 1), line, ribbon);
            Text(title, new Vector3(.035f, -.035f, .03f), BannerHeight, new Color(0, 0, 0, .85f), pivot, true);
            Text(title, Vector3.zero, BannerHeight, Color.Lerp(team, Color.white, .25f), pivot, true);
            return (pivot, ribbon);
        }

        private void BuildConfetti()
        {
            var palette = new[] { team, Color.Lerp(team, Color.white, .6f), new Color(1f, .85f, .25f), Color.white, Color.Lerp(team, Color.black, .3f) };
            var shared = new Material[palette.Length];
            for (int i = 0; i < palette.Length; i++) shared[i] = Shape(2, palette[i], Color.clear);
            var random = new System.Random(1013);
            for (int i = 0; i < 90; i++)
            {
                var piece = Quad("Confetto", Vector3.zero, new Vector3(.07f, .12f, 1), shared[i % shared.Length]);
                var c = new Confetto
                {
                    Piece = piece,
                    Start = new Vector3((float)(random.NextDouble() * 2 - 1), 0, (float)random.NextDouble() * 2.5f - .5f),
                    Delay = (float)random.NextDouble() * 2.2f,
                    Fall = .75f + (float)random.NextDouble() * .6f,
                    Sway = (float)random.NextDouble() * 6.28f,
                    Spin = new Vector3((float)random.NextDouble() * 400 - 200, (float)random.NextDouble() * 400 - 200, (float)random.NextDouble() * 300 - 150),
                };
                piece.gameObject.SetActive(false);
                confetti.Add(c);
            }
        }

        // ---------------------------------------------------------------- actors

        private Actor BuildActor(PodiumEntry entry, ResultPodiumAssets assets, float x, float slotWidth)
        {
            bool decided = result != RoundResult.Void;
            GameObject prefab;
            AnimationClip clip;
            CharacterAsset definition = null;
            switch (entry.Body)
            {
                case PodiumBody.Seeker:
                    definition = assets != null ? assets.seeker : null;
                    prefab = definition != null ? definition.visualPrefab : null;
                    clip = definition != null ? definition.idleClip : null;
                    break;
                // An Echo is the Runner model made see-through, as in game (CharacterBody ghost materials).
                case PodiumBody.Echo:
                default:
                    definition = assets != null ? assets.runner : null;
                    prefab = definition != null ? definition.visualPrefab : null;
                    clip = assets == null || definition == null ? null : !decided ? definition.idleClip : entry.Won ? assets.runnerWin : assets.runnerLose;
                    break;
            }

            var actor = new Actor { Entry = entry, X = x, Width = slotWidth, Definition = definition, Clip = clip, Procedural = entry.Body == PodiumBody.Seeker && decided };
            bool raised = entry.Won && decided;
            float floorY = raised ? RiserHeight : 0f;
            actor.Home = new Vector3(x, floorY, 0);
            actor.Anchor = new GameObject("Actor: " + entry.Name).transform;
            actor.Anchor.SetParent(root.transform, false);
            actor.Anchor.localPosition = actor.Home;
            // Face the camera, turned a little toward the middle of the row.
            actor.Anchor.localRotation = Quaternion.Euler(0, 180f - x * 4.5f, 0);

            // Riser + spot light on the floor.
            var spotColor = raised ? new Color(team.r, team.g, team.b, .75f) : new Color(.55f, .6f, .75f, .32f);
            actor.Spot = Quad("Spot", new Vector3(x, floorY + .012f, 0), new Vector3(1.1f, 1.1f, 1), Shape(0, spotColor, Color.clear));
            actor.Spot.rotation = Quaternion.Euler(90, 0, 0);
            if (raised)
            {
                var riserMaterial = Surface(null, new Color(team.r * .25f + .05f, team.g * .25f + .05f, team.b * .25f + .07f), team * new Color(1, 1, 1, .3f), 0f, .1f);
                actor.Riser = MeshObject("Riser", cylinder, new Vector3(x, RiserHeight / 2, 0), new Vector3(RiserScale, RiserHeight / 2, RiserScale), riserMaterial);
            }

            if (prefab != null)
            {
                // Inactive holder so the imported demo scripts never wake up (same as CharacterBody).
                var holder = new GameObject("Model");
                holder.SetActive(false);
                holder.transform.SetParent(actor.Anchor, false);
                var instance = Object.Instantiate(prefab, holder.transform, false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                actor.Model = holder.transform;
                Prepare(instance, entry, decided);
                holder.SetActive(true);
                actor.Animator = holder.GetComponentInChildren<Animator>(true);
                if (actor.Animator != null && clip != null)
                {
                    actor.Animator.applyRootMotion = false;
                    actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    actor.Animator.runtimeAnimatorController = null;
                    actor.Graph = PlayableGraph.Create("Result podium " + entry.Name);
                    actor.Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    actor.Playable = AnimationClipPlayable.Create(actor.Graph, clip);
                    AnimationPlayableOutput.Create(actor.Graph, "Pose", actor.Animator).SetSourcePlayable(actor.Playable);
                    actor.Graph.Play();
                    actor.Playable.SetTime(0);
                    actor.Graph.Evaluate(0);
                }
                if (actor.Procedural) RememberSeekerRest(actor);
                if (entry.Body == PodiumBody.Seeker) FitHeight(actor, SeekerHeight, 0f);
            }
            else
            {
                Debug.LogWarning($"[MARCO][Result] No podium model for {entry.Body}; showing the label only.");
            }

            BuildInfo(actor);
            return actor;
        }

        private void Prepare(GameObject instance, PodiumEntry entry, bool decided)
        {
            foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
            foreach (var camera in instance.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var listener in instance.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var light in instance.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true)) particles.gameObject.SetActive(false);
            bool loser = decided && !entry.Won;
            Color rim = entry.Body switch
            {
                PodiumBody.Seeker => SeekersColor,
                PodiumBody.Echo => new Color(.75f, .5f, 1f),
                _ => RunnersColor,
            };
            rim.a = loser ? .45f : 1f;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                // The Demon's hand-held ball is hidden in game too (CharacterBody.HideSeekerBall).
                bool ball = false;
                foreach (var m in renderer.sharedMaterials) if (m != null && m.name.Contains("Ball_mat")) ball = true;
                if (ball && entry.Body == PodiumBody.Seeker) { renderer.enabled = false; continue; }
                var sources = renderer.sharedMaterials;
                var replaced = new Material[sources.Length];
                for (int i = 0; i < sources.Length; i++)
                {
                    if (entry.Body == PodiumBody.Echo)
                    {
                        // Flat colour, no source texture — the same as in game (CharacterBody.MakeGhostMaterials).
                        replaced[i] = Surface(null, EchoTint, rim, 0f, .45f);
                        if (sources[i] != null) replaced[i].name = sources[i].name + " (result podium Echo)";
                        MakeTranslucent(replaced[i], EchoAlpha);
                    }
                    else replaced[i] = Surface(sources[i], entry.Body == PodiumBody.Seeker ? new Color(1.55f, 1.5f, 1.5f) : Color.white, rim, loser ? 1f : 0f, 0f);
                }
                renderer.sharedMaterials = replaced;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
            }
        }

        private void FitHeight(Actor actor, float height, float hover)
        {
            if (!TryBounds(actor.Model, out var bounds) || bounds.size.y < .01f) return;
            float scale = height / bounds.size.y;
            actor.Model.localScale *= scale;
            TryBounds(actor.Model, out bounds);
            actor.Model.position += Vector3.up * (actor.Anchor.position.y + hover - bounds.min.y);
            actor.ModelRest = actor.Model.localPosition;
        }

        private static bool TryBounds(Transform model, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any;
        }

        private void BuildInfo(Actor actor)
        {
            var info = new GameObject("Info").transform;
            info.SetParent(root.transform, false);
            info.localPosition = new Vector3(actor.X, 0, LabelZ);
            actor.Info = info;
            var entry = actor.Entry;
            float maxWidth = actor.Width * .9f;
            Color nameColor = entry.IsLocal ? new Color(1f, .86f, .3f) : Color.white;
            float nameHeight = Mathf.Min(.24f, .24f * maxWidth / Mathf.Max(.01f, EstimateWidth(entry.Name, .24f)));
            Text(entry.Name, new Vector3(.012f, LabelTop - .012f, .01f), nameHeight, new Color(0, 0, 0, .8f), info, true);
            Text(entry.Name, new Vector3(0, LabelTop, 0), nameHeight, nameColor, info, true);
            Quad("Outcome plate", new Vector3(0, LabelTop - .36f, .02f), new Vector3(1.0f, .25f, 1),
                Shape(2, new Color(entry.OutcomeColor.r * .3f, entry.OutcomeColor.g * .3f, entry.OutcomeColor.b * .3f, .85f), Color.clear), info);
            Text(entry.Outcome, new Vector3(0, LabelTop - .36f, 0), .17f, entry.OutcomeColor, info, true);
            // Award badges (기획서 8) go under the outcome once the awards are known (BuildBadges).
            info.localScale = Vector3.zero;
        }

        // ---------------------------------------------------------------- per frame

        public void Tick(float deltaTime)
        {
            if (root == null) return;
            Time += Mathf.Max(0f, deltaTime);
            float t = Time;

            // Banner: elastic pop, then a slow sway.
            float b = t / BannerPop;
            float pop = t <= 0 ? 0 : 1 - Mathf.Exp(-6.5f * b) * Mathf.Cos(10f * b);
            banner.localScale = Vector3.one * Mathf.Max(0, pop);
            banner.localRotation = Quaternion.Euler(0, 0, 7f * Mathf.Exp(-3f * t) * Mathf.Sin(15f * t) + 1.2f * Mathf.Sin(t * 1.7f));
            bannerRibbon.localScale = new Vector3(Mathf.Clamp01(b * 1.6f), 1, 1);
            rays.localRotation = Quaternion.Euler(0, 0, t * 9f);
            rays.localScale = Vector3.one * Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .8f));

            foreach (var actor in actors) TickActor(actor, t);
            TickConfetti(t);
            TickAwards(t);

            // Slow push-in.
            float push = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / 3f));
            float distance = cameraDistance * (1 + .1f * push);
            Camera.transform.localPosition = new Vector3(0, cameraHeight + distance * Mathf.Tan(Pitch * Mathf.Deg2Rad), -distance);
        }

        private void TickActor(Actor actor, float t)
        {
            float start = actor.LandTime - DropTime;
            bool visible = t >= start;
            actor.Anchor.gameObject.SetActive(visible);
            actor.Spot.gameObject.SetActive(visible);
            if (actor.Riser != null) actor.Riser.localScale = new Vector3(RiserScale, RiserHeight / 2 * Mathf.Clamp01((t - ActorsStart + .3f) / .25f) + .0001f, RiserScale);

            if (visible)
            {
                float p = Mathf.Clamp01((t - start) / DropTime);
                float since = t - actor.LandTime;
                actor.Anchor.localPosition = actor.Home + Vector3.up * (4.2f * (1 - p) * (1 - p));
                float squash = since < 0 ? 0 : (actor.Entry.Won ? .22f : .15f) * Mathf.Exp(-8f * since) * Mathf.Cos(18f * since);
                actor.Anchor.localScale = new Vector3(1 + squash * .5f, 1 - squash, 1 + squash * .5f);
                actor.Spot.localScale = new Vector3(1.1f, 1.1f, 1) * Mathf.Lerp(.2f, 1f, Mathf.Clamp01(since / .25f + 1));
                Pose(actor, Mathf.Max(0, since));
            }

            float info = (t - actor.InfoTime) / InfoPop;
            float s = info <= 0 ? 0 : info >= 1 ? 1 : 1 + 2.2f * Mathf.Pow(info - 1, 3) + 1.2f * Mathf.Pow(info - 1, 2);
            actor.Info.localScale = Vector3.one * s;
        }

        private void Pose(Actor actor, float since)
        {
            if (!actor.Graph.IsValid()) return;
            if (actor.Procedural) RestoreSeekerRest(actor);
            float length = actor.Clip.length;
            // Idles loop; WIN00/LOSE00 play once and hold their last pose.
            double time = actor.Clip.isLooping || actor.Entry.Body == PodiumBody.Seeker
                ? since % Mathf.Max(.01f, length)
                : Mathf.Min(since, length - 1f / 60f);
            actor.Playable.SetTime(time);
            actor.Graph.Evaluate(0);
            if (actor.Procedural) PoseSeeker(actor, since);
        }

        // The Demon has no win/lose clips: pose it over its idle, like CharacterMotion's swimming pose,
        // aiming limbs along the stage's axes from the real joints.
        private static readonly HumanBodyBones[] SeekerBones =
        {
            HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        };

        private void RememberSeekerRest(Actor actor)
        {
            foreach (var id in SeekerBones)
            {
                var bone = actor.Definition.Resolve(actor.Model, actor.Animator, id);
                if (bone == null) continue;
                actor.Bones[id] = bone;
                actor.Rest.Add((bone, bone.localRotation, bone.localPosition));
            }
        }

        private static void RestoreSeekerRest(Actor actor)
        {
            foreach (var (bone, rotation, position) in actor.Rest)
            {
                bone.localRotation = rotation;
                bone.localPosition = position;
            }
        }

        private void PoseSeeker(Actor actor, float since)
        {
            float w = Mathf.SmoothStep(0, 1, Mathf.Clamp01(since / .3f));
            if (w <= 0) return;
            var frame = actor.Anchor;
            Vector3 up = frame.up, front = frame.forward, right = frame.right;
            Transform Bone(HumanBodyBones id) => actor.Bones.TryGetValue(id, out var bone) ? bone : null;
            if (actor.Entry.Won)
            {
                // Victory: both fists up, pumping on the beat; chest back, chin up.
                float pump = .5f + .5f * Mathf.Sin(since * Mathf.PI * 2f * 1.4f);
                Tilt(Bone(HumanBodyBones.Chest), right, -10f * w);
                Tilt(Bone(HumanBodyBones.Head), right, -14f * w);
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? -1f : 1f;
                    float lift = side == 0 ? pump : 1 - pump;
                    // A wide V rather than straight up, so the claws stay under the banner.
                    Limb(actor, side == 0, w,
                        up * (.35f + .25f * lift) + right * (sign * .8f) + front * .15f,
                        up * .85f + right * (sign * (.4f + .1f * lift)) + front * .1f);
                }
                actor.Model.localPosition = actor.ModelRest + Vector3.up * (.05f * Mathf.Abs(Mathf.Sin(since * Mathf.PI * 1.4f)) * w);
            }
            else
            {
                // Defeat: hunched over, arms hanging limp, head down, swaying slowly.
                float sway = Mathf.Sin(since * 1.3f);
                Tilt(Bone(HumanBodyBones.Spine), right, 20f * w);
                Tilt(Bone(HumanBodyBones.Chest), right, 18f * w);
                Tilt(Bone(HumanBodyBones.Head), right, 32f * w);
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? -1f : 1f;
                    Limb(actor, side == 0, w,
                        -up + front * (.5f + .05f * sway) + right * (sign * .12f),
                        -up + front * .42f + right * (sign * .02f));
                }
                actor.Model.localPosition = actor.ModelRest + Vector3.down * (.06f * w);
                actor.Model.localRotation = Quaternion.Euler(0, 0, 2.5f * sway * w);
            }
        }

        private static void Tilt(Transform bone, Vector3 axis, float angle)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(angle, axis) * bone.rotation;
        }

        private static void Limb(Actor actor, bool left, float weight, Vector3 upperDirection, Vector3 lowerDirection)
        {
            actor.Bones.TryGetValue(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, out var upper);
            actor.Bones.TryGetValue(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, out var lower);
            actor.Bones.TryGetValue(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, out var end);
            if (upper == null || lower == null || end == null || !lower.IsChildOf(upper)) return;
            // Demon.fbx: Hand.L/R are siblings of Pelvis, not children of Arm2 — carry them along explicitly.
            bool detached = !end.IsChildOf(lower);
            Vector3 endpoint = lower.InverseTransformPoint(end.position);
            Quaternion endRotation = Quaternion.Inverse(lower.rotation) * end.rotation;
            Aim(upper, lower.position - upper.position, upperDirection, weight);
            if (detached) { end.position = lower.TransformPoint(endpoint); end.rotation = lower.rotation * endRotation; }
            Aim(lower, end.position - lower.position, lowerDirection, weight);
            if (detached) { end.position = lower.TransformPoint(endpoint); end.rotation = lower.rotation * endRotation; }
        }

        private static void Aim(Transform bone, Vector3 current, Vector3 desired, float weight)
        {
            if (current.sqrMagnitude < 1e-6f || desired.sqrMagnitude < 1e-6f) return;
            bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(current, desired), weight) * bone.rotation;
        }

        private void TickConfetti(float t)
        {
            if (confetti.Count == 0) return;
            float halfWidth = cameraDistance * Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad) * Camera.aspect;
            float top = cameraHeight + cameraDistance * Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad) + .5f;
            foreach (var c in confetti)
            {
                float local = t - confettiAt - c.Delay;
                bool on = local > 0;
                c.Piece.gameObject.SetActive(on);
                if (!on) continue;
                float span = (top + 1.5f) / c.Fall;
                float u = local % span;
                Vector3 p = new(c.Start.x * halfWidth + .25f * Mathf.Sin(local * 1.7f + c.Sway), top - u * c.Fall, c.Start.z);
                c.Piece.localPosition = p;
                c.Piece.localRotation = Quaternion.Euler(c.Spin * local);
            }
        }

        // ---------------------------------------------------------------- camera

        private void BuildCamera(int width, int height)
        {
            var cameraObject = new GameObject("Result podium camera");
            cameraObject.transform.SetParent(root.transform, false);
            Camera = cameraObject.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.012f, .014f, .03f, 1);
            Camera.cullingMask = 1 << Layer;
            Camera.fieldOfView = FieldOfView;
            Camera.nearClipPlane = .3f;
            Camera.farClipPlane = 60f;
            Camera.allowHDR = false;
            Camera.allowMSAA = true;
            Camera.enabled = false;
            Camera.transform.localRotation = Quaternion.Euler(Pitch, 0, 0);
            Resize(width, height);
        }

        public void Resize(int width, int height)
        {
            if (Camera == null) return;
            width = Mathf.Max(64, width);
            height = Mathf.Max(64, height);
            if (Feed != null && Feed.width == width && Feed.height == height) return;
            if (Feed != null) { Camera.targetTexture = null; Feed.Release(); Kill(Feed); }
            Feed = new RenderTexture(width, height, 24) { name = "Result podium feed", antiAliasing = 4, filterMode = FilterMode.Bilinear };
            Feed.Create();
            Camera.targetTexture = Feed;
            Camera.aspect = width / (float)height;

            // Frame banner top to the award cards, leaving the bottom ~18% for the rematch vote bar.
            float tan = Mathf.Tan(FieldOfView * .5f * Mathf.Deg2Rad);
            float contentTop = BannerY + BannerHeight, contentBottom = CardsBottom;
            float bannerWidth = EstimateWidth(ResultTitle(result), BannerHeight) + 2.8f;
            float widest = Mathf.Max(Mathf.Max(rowWidth, bannerWidth), CardsWidth);
            float visibleHeight = Mathf.Max((contentTop - contentBottom) / .76f, widest * 1.1f / Camera.aspect);
            cameraDistance = visibleHeight / (2 * tan);
            cameraHeight = contentTop + visibleHeight * .05f - visibleHeight / 2;
        }

        public void Render()
        {
            if (Camera != null) Camera.Render();
        }

        public GameObject Root => root;

        // Viewport rectangle (0..1, y up) of each visible character, for the editor render check.
        public List<(string label, Rect rect)> ActorScreenRects()
        {
            var rects = new List<(string, Rect)>();
            foreach (var actor in actors)
            {
                if (actor.Model == null || !actor.Anchor.gameObject.activeInHierarchy || !TryBounds(actor.Model, out var bounds)) continue;
                Vector2 min = Vector2.one * float.MaxValue, max = Vector2.one * float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x, (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                    Vector2 p = Camera.WorldToViewportPoint(corner);
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
                rects.Add(($"{actor.Entry.Name} ({actor.Entry.Body}{(actor.Entry.Won ? " win" : "")})", Rect.MinMaxRect(min.x, min.y, max.x, max.y)));
            }
            return rects;
        }

        // ---------------------------------------------------------------- helpers

        private static float SlotWidth(PodiumBody body) => body == PodiumBody.Seeker ? 2.0f : 1.45f;

        // x of each entry (in rank order): rank 0 takes the middle slot, then alternating outward.
        private static List<float> RowPositions(List<PodiumEntry> ranked, out float total)
        {
            int count = ranked.Count;
            var leftToRight = new List<int>();
            for (int i = 0; i < count; i++) leftToRight.Add(i);
            float middle = (count - 1) / 2f;
            leftToRight.Sort((a, b) => Mathf.Abs(a - middle).CompareTo(Mathf.Abs(b - middle)) != 0
                ? Mathf.Abs(a - middle).CompareTo(Mathf.Abs(b - middle)) : a.CompareTo(b));
            var widths = new float[count];
            for (int rank = 0; rank < count; rank++) widths[leftToRight[rank]] = SlotWidth(ranked[rank].Body);
            total = 0;
            foreach (float w in widths) total += w;
            var centers = new float[count];
            float x = -total / 2;
            for (int i = 0; i < count; i++) { centers[i] = x + widths[i] / 2; x += widths[i]; }
            var positions = new List<float>();
            for (int rank = 0; rank < count; rank++) positions.Add(centers[leftToRight[rank]]);
            return positions;
        }

        public static string ResultTitle(RoundResult result) => result switch
        {
            RoundResult.RunnersWin => "도망자 승리",
            RoundResult.SeekersWin => "술래 승리",
            RoundResult.Void => "무효",
            _ => "",
        };

        // Hangul/CJK glyphs are about square; Latin about half as wide.
        private static float EstimateWidth(string text, float height)
        {
            float width = 0;
            foreach (char c in text ?? "") width += c >= 0x1100 ? 1.0f : c == ' ' ? .35f : .58f;
            return width * height;
        }

        private Material Shape(int shape, Color color, Color second)
        {
            var material = new Material(glowShader) { name = "Result podium glow" };
            material.SetFloat("_Shape", shape);
            material.SetColor("_Color", color);
            material.SetColor("_Color2", second);
            materials.Add(material);
            return material;
        }

        private Material Surface(Material source, Color tint, Color rim, float dim, float emission)
        {
            var material = new Material(surfaceTemplate) { name = (source != null ? source.name : "Prop") + " (result podium)" };
            if (source != null)
            {
                foreach (string property in new[] { "_BaseMap", "_MainTex", "_MainTexture" })
                {
                    if (!source.HasProperty(property) || source.GetTexture(property) == null) continue;
                    material.SetTexture("_BaseMap", source.GetTexture(property));
                    material.SetTextureScale("_BaseMap", source.GetTextureScale(property));
                    material.SetTextureOffset("_BaseMap", source.GetTextureOffset(property));
                    break;
                }
                if (source.HasProperty("_BaseColor")) tint *= source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color")) tint *= source.GetColor("_Color");
                tint.a = 1;
            }
            material.SetColor("_BaseColor", tint);
            material.SetColor("_RimColor", rim);
            material.SetFloat("_Dim", dim);
            material.SetFloat("_Emission", emission);
            materials.Add(material);
            return material;
        }

        // Same flat see-through purple as the in-game Echo (NetworkPlayer prefab CharacterBody.ghostTint 0.65, 0.25, 1, 0.3,
        // no texture), a little lighter and more opaque so it still reads against the dark podium backdrop;
        // depth-unsorted, double-sided alpha blend like CharacterBody.MakeGhostMaterials.
        private static readonly Color EchoTint = new(.78f, .45f, 1f);
        private const float EchoAlpha = .5f;

        private static void MakeTranslucent(Material material, float alpha)
        {
            material.SetFloat("_Alpha", alpha);
            material.SetFloat("_Cutoff", .05f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        private Transform Quad(string name, Vector3 position, Vector3 scale, Material material, Transform parent = null) =>
            MeshObject(name, quad, position, scale, material, parent);

        private Transform MeshObject(string name, Mesh mesh, Vector3 position, Vector3 scale, Material material, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : root.transform, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private static Font StageFont => font != null ? font
            : font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Apple SD Gothic Neo", "Noto Sans CJK KR", "Arial" }, 96);

        private void Text(string value, Vector3 position, float height, Color color, Transform parent, bool bold)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var text = go.AddComponent<TextMesh>();
            text.font = StageFont;
            text.fontSize = 96;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.characterSize = height / (96 * .1f);
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            text.text = value;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = StageFont.material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            foreach (Transform child in t) SetLayer(child);
        }

        private static void Kill(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

        public void Dispose()
        {
            foreach (var actor in actors) if (actor.Graph.IsValid()) actor.Graph.Destroy();
            actors.Clear();
            if (Camera != null) Camera.targetTexture = null;
            if (Feed != null) { Feed.Release(); Kill(Feed); Feed = null; }
            Kill(root);
            foreach (var material in materials) Kill(material);
            materials.Clear();
            confetti.Clear();
        }

        private sealed class Actor
        {
            public PodiumEntry Entry;
            public float X, Width, LandTime, InfoTime;
            public Vector3 Home, ModelRest;
            public Transform Anchor, Model, Spot, Riser, Info;
            public Animator Animator;
            public CharacterAsset Definition;
            public AnimationClip Clip;
            public bool Procedural;
            public PlayableGraph Graph;
            public AnimationClipPlayable Playable;
            public readonly List<(Transform bone, Quaternion rotation, Vector3 position)> Rest = new();
            public readonly Dictionary<HumanBodyBones, Transform> Bones = new();
        }

        private sealed class Confetto
        {
            public Transform Piece;
            public Vector3 Start, Spin;
            public float Delay, Fall, Sway;
        }
    }
}
