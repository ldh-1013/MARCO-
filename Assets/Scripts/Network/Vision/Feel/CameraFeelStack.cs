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
    // PlayerMovement.IsSprinting) and never touches input, movement or anything sent.
    [DefaultExecutionOrder(1100)]
    public sealed class CameraFeelStack : MonoBehaviour
    {
        public static CameraFeelStack Instance { get; private set; }
        // Last frame's summed (capped) offsets — for checks and the capture CSV.
        public static Vector3 CurrentRotation { get; private set; }
        public static Vector3 CurrentPosition { get; private set; }
        public static float CurrentFov { get; private set; }
        public static float CurrentFlashAlpha { get; private set; }
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

        private const float Never = -1000f;
        private NetworkPlayer player;
        private PlayerInteractor interactor;
        private Camera view;
        private float hitAt = Never, echoAt = Never, missAt = Never, hurtAt = Never, nudgeAt = Never;
        private float hurtSide, hurtSeed, seenInjury = -1f;
        private Vector3 bobBaseline; private bool bobBaselineSet;
        private readonly DiveRefraction refraction = new();
        private readonly Collider[] probeHits = new Collider[16];
        private readonly System.Collections.Generic.Dictionary<Collider, bool> structureOf = new();
        private bool wasInside; private float wallPassAt = Never; private Material wallNoise; private bool wallNoiseFailed;
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha"), FrameId = Shader.PropertyToID("_Frame"), AspectId = Shader.PropertyToID("_Aspect");
        private Renderer[] ownBody = new Renderer[0]; private MaterialPropertyBlock rimBlock; private bool rimOn;
        private static readonly int FeelRimId = Shader.PropertyToID("_FeelRim");
        private bool sprintOn, sneakOn;
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
            ownBody = new Renderer[0]; bobBaselineSet = false;
            wasInside = false; wallPassAt = Never; structureOf.Clear();
            Restore();
            interactor = null; view = null; player = null;
            hitAt = echoAt = missAt = hurtAt = nudgeAt = Never;
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
            bool sprint = inRound && alive && movement != null && movement.IsSprinting;
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
                + NudgeRotation(now - nudgeAt);
            Vector3 position = new Vector3(0f, 0f, -FeelRules.HitKickBack(tHit) * hitScale);
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
            // [V30] the animated head bob counts toward the 2 cm cap: our offset gets only what is left (bob untouched)
            if (!bobBaselineSet || (basePosition - bobBaseline).sqrMagnitude > .25f) { bobBaseline = basePosition; bobBaselineSet = true; }
            bobBaseline = FeelRules.Baseline(bobBaseline, basePosition, Time.deltaTime);
            position = FeelRules.CapPositionWithBob(position, basePosition - bobBaseline);
            CurrentPosition = position;
            Vector3 positionInParent = baseRotation * position;
            t.localRotation = writtenRotation = baseRotation * rotation;
            t.localPosition = writtenPosition = basePosition + positionInParent;
            view.fieldOfView = writtenFov = Mathf.Clamp(baseFov + fov, 1f, 179f);
            lastRotation = rotation; lastPosition = positionInParent; lastFov = fov;
            wrote = true;
        }

        // 4.2 술래 화면 적색 #EF6A4C 전체 플래시 — above the darkness (100) and the edge stain (50), behind the HUD (0).
        private void TickWallPass(bool echoView, float now)
        {
            bool inside = false;
            if (echoView)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                int count = Physics.OverlapSphereNonAlloc(view.transform.position, WallPassRules.ProbeRadius, probeHits, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count && !inside; i++)
                {
                    var hit = probeHits[i];
                    if (hit == null) continue;
                    if (!structureOf.TryGetValue(hit, out bool structure))
                    {
                        var part = Marco.Network.Map.MapParts.Read(hit);
                        structureOf[hit] = structure = part != null && part.IsSoundStructure;
                    }
                    inside = structure;
                }
                LastProbeMicroseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1e6 / System.Diagnostics.Stopwatch.Frequency;
                if (WallPassRules.Entered(wasInside, inside)) { wallPassAt = now; WallPasses++; Debug.Log("[MARCO][Feel] wall pass"); }
            }
            wasInside = inside;
            InsideStructure = inside;
            CurrentWallNoiseAlpha = echoView ? WallPassRules.Alpha(now - wallPassAt) : 0f;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            DrawWallNoise();
            if (CurrentFlashAlpha <= .001f) return;
            var saved = GUI.color; int depth = GUI.depth;
            GUI.depth = 40;
            var c = FeelRules.SeekerFlashColor; c.a = CurrentFlashAlpha;
            GUI.color = c;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = saved; GUI.depth = depth;
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

        private void OnDestroy() { Unbind(); if (wallNoise != null) Destroy(wallNoise); if (Instance == this) Instance = null; }
    }
}
