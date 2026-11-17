using System;
using Marco.Core.WinCondition;
using Marco.Network.Character;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Vision
{
    // V29 A-1 · A-2: every camera feel effect adds into named channels (rotation, position, FOV, overlay) here, is
    // capped after the sum (FeelRules), and is written once per frame on top of whatever the game set this frame
    // (FirstPersonBody's head follow, the look pitch, the settings FOV). Local presentation only: it reads events and
    // state the client already has (PlayerInteractor.LocalSwingConfirmed, NetworkPlayer.LastInjuryTime,
    // PlayerMovement.IsSprinting / IsSprintExhausted) and never touches input, movement or anything sent.
    [DefaultExecutionOrder(1100)]
    public sealed class CameraFeelStack : MonoBehaviour
    {
        public static CameraFeelStack Instance { get; private set; }
        // Last frame's summed (capped) offsets — for checks and the capture CSV.
        public static Vector3 CurrentRotation { get; private set; }
        public static Vector3 CurrentPosition { get; private set; }
        public static float CurrentFov { get; private set; }
        public static float CurrentFlashAlpha { get; private set; }
        // V32 걷기 bob 위상: the height added this frame to put the walking bob on the own footstep phase (m), its blend
        // weight and the learned profile (lows paired with SFX phase 0 / 0.5) — for checks and the capture CSV.
        public static float CurrentBobFix { get; private set; }
        public static float CurrentBobWeight { get; private set; }
        public static bool BobProfileReady { get; private set; }
        public static float BobLowA { get; private set; }
        public static float BobLowB { get; private set; }
        public static int BobCyclesLearned { get; private set; }
        // How many times each one-shot fired (one event = one effect).
        public static int SeekerWoundHits { get; private set; }
        public static int SeekerEchoHits { get; private set; }
        public static int SeekerMisses { get; private set; }
        public static int RunnerHurts { get; private set; }
        // V31 벽 통과: entries counted, the overlay's opacity now, and the last probe's cost (µs).
        public static int WallPasses { get; private set; }
        public static float CurrentWallNoiseAlpha { get; private set; }
        public static bool InsideStructure { get; private set; }
        public static double LastProbeMicroseconds { get; private set; }
        // V31 B-1 스태미나 소진: the contraction's opacity now, and how many exhaustions started (one per state entry).
        public static float CurrentExhaustionAlpha { get; private set; }
        // VB B-4 8.1 억제 성공 (black edge pulse) and 8.2 부상 후 흔들림 (sway weight) — for checks and the capture CSV.
        public static float CurrentSuppressAlpha { get; private set; }
        public static int Suppressions { get; private set; }
        public static float CurrentSwayWeight { get; private set; }
        public static int SprintExhaustions { get; private set; }

        private const float Never = -1000f;
        private NetworkPlayer player;
        private PlayerInteractor interactor;
        private Camera view;
        private float hitAt = Never, echoAt = Never, missAt = Never, hurtAt = Never, nudgeAt = Never;
        private float hurtSide, hurtSeed, seenInjury = -1f;
        private Vector3 bobBaseline; private bool bobBaselineSet;
        private readonly WalkBobProfile walkBob = new(); private float walkBobWeight, walkGaitSince = Never, lastPitch; private object walkBobKey; private bool walkCrouch;
        private readonly DiveRefraction refraction = new();
        private readonly Collider[] probeHits = new Collider[16];
        private readonly System.Collections.Generic.Dictionary<Collider, bool> structureOf = new();
        private readonly RaycastHit[] crossHits = new RaycastHit[16];
        private Vector3 lastEye; private bool lastEyeSet;
        private bool wasInside; private float wallPassAt = Never; private Material wallNoise; private bool wallNoiseFailed;
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha"), FrameId = Shader.PropertyToID("_Frame"), AspectId = Shader.PropertyToID("_Aspect");
        private Renderer[] ownBody = new Renderer[0]; private MaterialPropertyBlock rimBlock; private bool rimOn;
        private static readonly int FeelRimId = Shader.PropertyToID("_FeelRim");
        private bool sprintOn, sneakOn;
        private float lastBreath = -1f, suppressAt = Never, swayWeight;
        private bool exhaustOn; private float exhaustFrom, exhaustChanged = Never, exhaustSeen = Never; private Material exhaustEdge; private bool exhaustEdgeFailed;
        private static readonly int ColorId = Shader.PropertyToID("_Color"), SeedId = Shader.PropertyToID("_Seed"), DriftId = Shader.PropertyToID("_Drift"),
            CornerDepthId = Shader.PropertyToID("_CornerDepth"), EdgeDepthId = Shader.PropertyToID("_EdgeDepth");
        private float sprintFrom, sprintTo, sprintChanged = Never, sneakFrom, sneakTo, sneakChanged = Never;
        private Quaternion writtenRotation = Quaternion.identity;
        private Vector3 writtenPosition;
        private float writtenFov = float.NaN;
        private Quaternion lastRotation = Quaternion.identity;
        private Vector3 lastPosition;
        private float lastFov;
        private bool wrote, failed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance != null) return;
            var host = new GameObject("Camera feel stack") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(host);
            Instance = host.AddComponent<CameraFeelStack>();
        }

        // 연출.md 7.2 노크 본인 화면 미세 진동 0.1초 (CameraNudge) — now a channel of this stack.
        public void Nudge() => nudgeAt = Time.time;

        // PlayerInteractor.LocalSwingConfirmed (server-confirmed): Wounded = first hit (4.5), Echoed = Echo transition (4.2),
        // Missed = miss (15.3). Public so the capture harness can drive the Seeker's screen without a real target.
        public void OnLocalSwing(SwingResult result)
        {
            float now = Time.time;
            switch (result)
            {
                case SwingResult.Wounded: hitAt = now; SeekerWoundHits++; Debug.Log("[MARCO][Feel] seeker hit (Wounded)"); break;
                case SwingResult.Echoed: hitAt = now; echoAt = now; SeekerEchoHits++; Debug.Log("[MARCO][Feel] seeker hit (Echoed)"); break;
                case SwingResult.Missed: missAt = now; SeekerMisses++; Debug.Log("[MARCO][Feel] seeker miss"); break;
            }
        }

        private void Bind()
        {
            var local = NetworkPlayer.Local;
            if (local == player && (local == null || view != null)) return;
            Unbind();
            player = local;
            if (player == null) return;
            foreach (var camera in player.GetComponentsInChildren<Camera>(true))
                if (camera.name == "PlayerCamera") { view = camera; break; }
            if (view == null) view = player.GetComponentInChildren<Camera>(true);
            interactor = player.GetComponent<PlayerInteractor>();
            if (interactor != null) interactor.LocalSwingConfirmed += OnLocalSwing;
            seenInjury = player.LastInjuryTime;
            wrote = false;
        }

        private void Unbind()
        {
            if (interactor != null) interactor.LocalSwingConfirmed -= OnLocalSwing;
            SetRim(Color.black);
            refraction.Release();
            ownBody = new Renderer[0]; bobBaselineSet = false; walkBob.Reset(); walkBobWeight = 0f; walkBobKey = null; walkGaitSince = Never; CurrentBobFix = 0f;
            wasInside = false; wallPassAt = Never; structureOf.Clear(); lastEyeSet = false;
            Restore();
            interactor = null; view = null; player = null;
            hitAt = echoAt = missAt = hurtAt = nudgeAt = Never;
            exhaustOn = false; exhaustFrom = 0f; exhaustChanged = exhaustSeen = Never; CurrentExhaustionAlpha = 0f;
            lastBreath = -1f; suppressAt = Never; swayWeight = 0f; CurrentSuppressAlpha = CurrentSwayWeight = 0f;
        }

        // Put back exactly what this stack added, so nothing is left behind when it stops.
        private void Restore()
        {
            if (!wrote || view == null) { wrote = false; return; }
            var t = view.transform;
            if (t.localRotation == writtenRotation) t.localRotation = writtenRotation * Quaternion.Inverse(lastRotation);
            if (t.localPosition == writtenPosition) t.localPosition = writtenPosition - lastPosition;
            if (!float.IsNaN(writtenFov) && Mathf.Approximately(view.fieldOfView, writtenFov)) view.fieldOfView = writtenFov - lastFov;
            wrote = false;
        }

        private void LateUpdate()
        {
            if (failed) return;
            try { Tick(); }
            catch (Exception exception) { failed = true; Restore(); Debug.LogException(exception, this); }
        }

        private void Tick()
        {
            Bind();
            if (player == null || view == null) { CurrentRotation = CurrentPosition = Vector3.zero; CurrentFov = 0f; return; }
            float now = Time.time;
            var round = NetworkRound.Instance;
            bool inRound = round != null && round.Mode != RoundMode.NotStarted && round.Result == RoundResult.InProgress;
            bool alive = player.Status == PlayerStatus.Alive && !player.HasEchoBody;

            // 15.3 맞은 도망자: the server's health message (HealthTarget → LastInjuryTime) is the confirmed hit.
            if (player.LastInjuryTime != seenInjury)
            {
                seenInjury = player.LastInjuryTime;
                if (inRound && player.Role == PlayerRole.Runner && player.LastInjuryTime >= 0f)
                {
                    hurtAt = now; hurtSeed = UnityEngine.Random.Range(0f, 64f); hurtSide = AwayFromSeeker();
                    RunnerHurts++;
                    Debug.Log($"[MARCO][Feel] runner hurt (away {hurtSide:+0;-0;0})");
                }
            }

            // 연출 2.2 질주 +5° · 술래 잠행 −3° (0.2 s in, sprint out 0.25 s), ease-out, frame-rate independent.
            var movement = player.GetComponent<PlayerMovement>();
            // 2.2 스태미나 소진 (V31 B-1): the owner's own exhaustion state — the same value its movement uses this frame.
            bool exhausted = inRound && alive && player.Role == PlayerRole.Runner && movement != null && movement.IsSprintExhausted;
            if (exhausted) exhaustSeen = now;
            float exhaustNow = ExhaustionRules.Alpha(exhaustFrom, exhaustOn, now - exhaustChanged);
            if (exhausted != exhaustOn)
            {
                exhaustOn = exhausted; exhaustFrom = exhaustNow; exhaustChanged = now;
                if (exhausted) { SprintExhaustions++; Debug.Log("[MARCO][Feel] sprint exhausted"); }
            }
            CurrentExhaustionAlpha = ExhaustionRules.Alpha(exhaustFrom, exhaustOn, now - exhaustChanged);
            // 8.1 억제 성공 (VB B-4): the only readable trace is the 4.5 s of breath spent at once, out of the water.
            bool runner = inRound && alive && player.Role == PlayerRole.Runner && movement != null;
            if (runner)
            {
                if (lastBreath >= 0f && BreathFeelRules.Suppressed(lastBreath, movement.BreathSeconds, movement.IsDiving))
                { suppressAt = now; Suppressions++; Debug.Log("[MARCO][Feel] suppression"); }
                lastBreath = movement.BreathSeconds;
            }
            else lastBreath = -1f;
            CurrentSuppressAlpha = BreathFeelRules.SuppressAlpha(now - suppressAt);
            // 8.2 부상 후: a breathing sway while the forced-surfacing slow lasts (SwimPenaltyRemaining, already sent).
            swayWeight = BreathFeelRules.SwayFade(swayWeight, runner && movement.SwimPenaltyRemaining > 0f, Time.deltaTime);
            CurrentSwayWeight = swayWeight;
            // the sprint FOV goes back (0.25 s ease-out) when the exhaustion starts and does not rise again while it lasts
            bool sprint = inRound && alive && movement != null &&
                ExhaustionRules.SprintFovAllowed(movement.IsSprinting, exhausted, exhaustSeen == Never ? -1f : now - exhaustSeen);
            bool sneak = inRound && alive && player.Role == PlayerRole.Seeker && movement != null && !movement.IsSwimming &&
                Cursor.lockState == CursorLockMode.Locked && Input.GetKey(KeyCode.LeftControl) && IsMoving();
            float sprintNow = FeelRules.Follow(sprintFrom, sprintTo, now - sprintChanged, sprintTo > sprintFrom ? FeelRules.FovInSeconds : FeelRules.SprintFovOutSeconds);
            if (sprint != sprintOn) { sprintOn = sprint; sprintFrom = sprintNow; sprintTo = sprint ? FeelRules.SprintFov : 0f; sprintChanged = now; sprintNow = sprintFrom; }
            float sneakNow = FeelRules.Follow(sneakFrom, sneakTo, now - sneakChanged, FeelRules.FovInSeconds);
            if (sneak != sneakOn) { sneakOn = sneak; sneakFrom = sneakNow; sneakTo = sneak ? FeelRules.SneakFov : 0f; sneakChanged = now; sneakNow = sneakFrom; }

            // One-shots, with priority (punch-in > hit > miss).
            float tEcho = now - echoAt, tHit = now - hitAt, tMiss = now - missAt;
            bool punchActive = tEcho >= 0f && tEcho < FeelRules.PunchRiseSeconds + FeelRules.PunchReturnSeconds;
            bool hitActive = tHit >= 0f && tHit < FeelRules.HitKickSeconds;
            float hitScale = FeelRules.PriorityScale(punchActive);
            float missScale = FeelRules.PriorityScale(punchActive || hitActive);

            Vector3 rotation = FeelRules.HitKickRotation(tHit) * hitScale
                + FeelRules.MissRotation(tMiss) * missScale
                + FeelRules.HurtRotation(now - hurtAt, hurtSide, hurtSeed)
                + NudgeRotation(now - nudgeAt)
                + BreathFeelRules.SwayRotation(now, swayWeight);
            Vector3 position = new Vector3(0f, 0f, -FeelRules.HitKickBack(tHit) * hitScale) + BreathFeelRules.SwayPosition(now, swayWeight);
            float fov = sprintNow + sneakNow + FeelRules.PunchFovOffset(tEcho);

            rotation = FeelRules.CapRotation(rotation);
            position = FeelRules.CapPosition(position);
            fov = FeelRules.CapFov(fov);
            CurrentRotation = rotation; CurrentPosition = position; CurrentFov = fov;
            CurrentFlashAlpha = FeelRules.ShowsSeekerFlash(player.Role == PlayerRole.Seeker) ? FeelRules.FlashAlpha(tEcho) : 0f;
            Write(Quaternion.Euler(rotation), position, fov);

            // 15.3 술래 손끝 림 — the local Seeker's own body only (MaterialPropertyBlock on its own renderers, never sent).
            SetRim(player.Role == PlayerRole.Seeker && alive ? FeelRules.RimColor(tHit) : Color.black);
            // 2.2 잠수 가장자리 물 굴절 — the player camera only, while diving.
            refraction.Update(view, inRound && alive && movement != null && movement.IsDiving);
            // 2.2 벽 통과 — the local Echo body's camera entering a structure (read-only overlap test, Echo view only).
            TickWallPass(inRound && player.HasEchoBody, now);
        }

        // 7.2 knock: 0.1 s, 0.45°, low-frequency noise (the old CameraNudge values).
        private static Vector3 NudgeRotation(float t)
        {
            const float Seconds = .1f, Degrees = .45f;
            if (t < 0f || t >= Seconds) return Vector3.zero;
            return new Vector3((Mathf.PerlinNoise(t * 90f, 0f) - .5f) * 2f, (Mathf.PerlinNoise(0f, t * 90f) - .5f) * 2f, 0f) * Degrees * FeelRules.FeelIntensity;
        }

        private void SetRim(Color rim)
        {
            bool on = rim.maxColorComponent > .001f;
            if (!on && !rimOn) return;
            if (on && ownBody.Length == 0 && player != null && player.TryGetComponent<CharacterBody>(out var body) && body.BodyRoot != null)
                ownBody = body.BodyRoot.GetComponentsInChildren<Renderer>(true);
            rimBlock ??= new MaterialPropertyBlock();
            foreach (var renderer in ownBody)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(rimBlock);
                rimBlock.SetColor(FeelRimId, on ? rim : Color.black);
                renderer.SetPropertyBlock(rimBlock);
            }
            rimOn = on;
            if (!on) ownBody = new Renderer[0];   // re-read after a model rebuild
        }

        private bool IsMoving() => player.TryGetComponent<CharacterMotion>(out var motion) && motion.Speed > .05f;

        // +1 = turn right (the Seeker was on the left), −1 = turn left, 0 = no living Seeker known.
        private float AwayFromSeeker()
        {
            foreach (var other in NetworkPlayer.All)
            {
                if (other == null || other == player || other.Role != PlayerRole.Seeker || other.Status != PlayerStatus.Alive) continue;
                Vector3 local = view.transform.InverseTransformDirection(other.transform.position - player.transform.position);
                return Mathf.Abs(local.x) < .05f ? 0f : local.x > 0f ? -1f : 1f;
            }
            return 0f;
        }

        // Compose onto this frame's base: if nobody rewrote a channel since our last write, remove our old offset first.
        private void Write(Quaternion rotation, Vector3 position, float fov)
        {
            var t = view.transform;
            Quaternion baseRotation = wrote && t.localRotation == writtenRotation ? writtenRotation * Quaternion.Inverse(lastRotation) : t.localRotation;
            Vector3 basePosition = wrote && t.localPosition == writtenPosition ? writtenPosition - lastPosition : t.localPosition;
            float baseFov = wrote && Mathf.Approximately(view.fieldOfView, writtenFov) ? writtenFov - lastFov : view.fieldOfView;
            // [V32] 2.1 the walking bob on the own footstep phase: a height shift of the base — part of the bob (outside the
            // 2 cm feel cap), written and taken back like our own offset
            Vector3 bobFix = WalkBobFix(t, basePosition);
            Vector3 bobbed = basePosition + bobFix;
            // [V30] the animated head bob counts toward the 2 cm cap: our offset gets only what is left (bob untouched)
            if (!bobBaselineSet || (bobbed - bobBaseline).sqrMagnitude > .25f) { bobBaseline = bobbed; bobBaselineSet = true; }
            bobBaseline = FeelRules.Baseline(bobBaseline, bobbed, Time.deltaTime);
            position = FeelRules.CapPositionWithBob(position, bobbed - bobBaseline);
            CurrentPosition = position;
            Vector3 positionInParent = baseRotation * position + bobFix;
            t.localRotation = writtenRotation = baseRotation * rotation;
            t.localPosition = writtenPosition = basePosition + positionInParent;
            view.fieldOfView = writtenFov = Mathf.Clamp(baseFov + fov, 1f, 179f);
            lastRotation = rotation; lastPosition = positionInParent; lastFov = fov;
            wrote = true;
        }

        // [V32] 2.1 걷기 bob 위상 (WalkBobRules): reads the gait phase (CharacterMotion.GaitPhase / FootstepIndex — the
        // values the own footstep SFX uses) and the live camera height; changes neither. Walk gait only: not sprinting
        // (FootstepRunning), not swimming / diving, not the Echo. Returns the height shift in the camera parent's space.
        private Vector3 WalkBobFix(Transform t, Vector3 basePosition)
        {
            float now = Time.time, dt = Time.deltaTime;
            player.TryGetComponent<CharacterMotion>(out var motion);
            player.TryGetComponent<PlayerMovement>(out var movement);
            player.TryGetComponent<CharacterBody>(out var body);
            bool cycle = motion != null && motion.HasFootstepCycle;
            bool walk = cycle && movement != null && player.Status == PlayerStatus.Alive && !player.HasEchoBody &&
                !movement.IsSwimming && !movement.IsDiving && motion.Speed > .05f && !motion.FootstepRunning;
            // another character (role swap, model rebuild) has its own walk clip: learn again
            object key = body != null ? body.Definition : null;
            if (!ReferenceEquals(key, walkBobKey)) { walkBob.Reset(); walkBobKey = key; }
            if (!walk) walkGaitSince = Never;
            else if (walkGaitSince == Never || movement.IsCrouching != walkCrouch) { walkGaitSince = now; walkCrouch = movement.IsCrouching; }
            float pitch = motion != null ? motion.LookPitch : 0f;
            float pitchRate = dt > 0f ? Mathf.Abs(Mathf.DeltaAngle(lastPitch, pitch)) / dt : 0f;
            lastPitch = pitch;
            if (cycle)
            {
                Vector3 world = t.parent != null ? t.parent.TransformPoint(basePosition) : basePosition;
                bool valid = walk && now - walkGaitSince >= WalkBobRules.SettleSeconds && !motion.IsAttacking &&
                    pitchRate < WalkBobRules.MaxPitchDegreesPerSecond;
                walkBob.Observe(motion.FootstepIndex / 2, motion.GaitPhase, world.y - player.transform.position.y, valid);
            }
            walkBobWeight = WalkBobRules.Fade(walkBobWeight, walk && walkBob.Ready, dt);
            float fix = cycle ? walkBob.Correction(motion.GaitPhase) * WalkBobRules.Shown(walkBobWeight) : 0f;
            CurrentBobFix = fix; CurrentBobWeight = walkBobWeight; BobProfileReady = walkBob.Ready;
            BobLowA = walkBob.LowA; BobLowB = walkBob.LowB; BobCyclesLearned = walkBob.CyclesLearned;
            Vector3 up = Vector3.up * fix;
            return t.parent != null ? t.parent.InverseTransformVector(up) : up;
        }

        // 4.2 술래 화면 적색 #EF6A4C 전체 플래시 — above the darkness (100) and the edge stain (50), behind the HUD (0).
        private void TickWallPass(bool echoView, float now)
        {
            bool inside = false;
            if (echoView)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                Vector3 eye = view.transform.position;
                int count = Physics.OverlapSphereNonAlloc(eye, WallPassRules.ProbeRadius, probeHits, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count && !inside; i++) inside = IsStructure(probeHits[i]);
                bool crossed = false;
                Vector3 step = eye - lastEye;
                // a teleport (> 2 m in a frame) is not a pass
                if (lastEyeSet && step.sqrMagnitude > 1e-6f && step.sqrMagnitude < 4f)
                {
                    int hits = Physics.RaycastNonAlloc(lastEye, step.normalized, crossHits, step.magnitude, ~0, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < hits && !crossed; i++) crossed = IsStructure(crossHits[i].collider);
                }
                lastEye = eye; lastEyeSet = true;
                LastProbeMicroseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
                if (WallPassRules.Entered(wasInside, inside, crossed)) { wallPassAt = now; WallPasses++; Debug.Log("[MARCO][Feel] wall pass"); }
            }
            else lastEyeSet = false;
            wasInside = inside;
            InsideStructure = inside;
            CurrentWallNoiseAlpha = echoView ? WallPassRules.Alpha(now - wallPassAt) : 0f;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            DrawExhaustion();
            DrawWallNoise();
            if (CurrentFlashAlpha <= .001f) return;
            var saved = GUI.color; int depth = GUI.depth;
            GUI.depth = 40;
            var c = FeelRules.SeekerFlashColor; c.a = CurrentFlashAlpha;
            GUI.color = c;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = saved; GUI.depth = depth;
        }

        private bool IsStructure(Collider hit)
        {
            if (hit == null) return false;
            if (!structureOf.TryGetValue(hit, out bool structure))
            {
                var part = Marco.Network.Map.MapParts.Read(hit);
                structureOf[hit] = structure = part != null && part.IsSoundStructure;
            }
            return structure;
        }

        // 2.2 스태미나 소진 수축 — the A-0 edge mask (EdgeStain) in black, still (no drift, no pulse); drawn under the health
        // edge stain (50) so the health state stays readable on top of it.
        private void DrawExhaustion()
        {
            // the suppression pulse (8.1, VB B-4) is the same black edge at the same depth: one draw, the larger opacity
            float alpha = Mathf.Max(CurrentExhaustionAlpha, CurrentSuppressAlpha);
            if (alpha <= .001f || exhaustEdgeFailed) return;
            if (exhaustEdge == null)
            {
                var shader = Resources.Load<Shader>("MarcoPresentation/EdgeStain");
                if (shader == null) { exhaustEdgeFailed = true; return; }
                exhaustEdge = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                exhaustEdge.SetColor(ColorId, ExhaustionRules.Colour);
                exhaustEdge.SetFloat(SeedId, 23.7f);
                exhaustEdge.SetFloat(DriftId, 0f);
                exhaustEdge.SetFloat(CornerDepthId, ExhaustionRules.CornerDepth);
                exhaustEdge.SetFloat(EdgeDepthId, ExhaustionRules.EdgeDepth);
            }
            exhaustEdge.SetFloat(AlphaId, alpha);
            exhaustEdge.SetFloat(AspectId, Screen.width / (float)Mathf.Max(1, Screen.height));
            var saved = GUI.matrix; int depth = GUI.depth;
            GUI.matrix = Matrix4x4.identity; GUI.depth = 52;
            Graphics.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, exhaustEdge);
            GUI.matrix = saved; GUI.depth = depth;
        }

        // 2.2 벽 통과 overlay — above the darkness (100), behind the HUD (0).
        private void DrawWallNoise()
        {
            if (CurrentWallNoiseAlpha <= .001f || wallNoiseFailed) return;
            if (wallNoise == null)
            {
                var shader = Resources.Load<Shader>("MarcoPresentation/WallPassNoise");
                if (shader == null) { wallNoiseFailed = true; return; }
                wallNoise = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            wallNoise.SetFloat(AlphaId, CurrentWallNoiseAlpha);
            wallNoise.SetFloat(FrameId, Time.frameCount % 997);
            wallNoise.SetFloat(AspectId, Screen.width / (float)Mathf.Max(1, Screen.height));
            var saved = GUI.matrix; int depth = GUI.depth;
            GUI.matrix = Matrix4x4.identity; GUI.depth = 42;
            Graphics.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture, wallNoise);
            GUI.matrix = saved; GUI.depth = depth;
        }

        private void OnDestroy() { Unbind(); if (wallNoise != null) Destroy(wallNoise); if (exhaustEdge != null) Destroy(exhaustEdge); if (Instance == this) Instance = null; }
    }
}
