using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Marco.Prototype
{
    public sealed class HunterTagPrototype : MonoBehaviour
    {
        [SerializeField] private float range = 1.2f;
        [SerializeField] private float halfAngle = 45f;
        [SerializeField] private float hitCooldown = 2.5f;
        [SerializeField] private float missCooldown = 1.5f;
        [SerializeField] private float swingDuration = 0.4f;
        [SerializeField] private Transform swingVisual = null;
        [SerializeField] private bool automaticAttack = false;
        [SerializeField] private float attackInterval = 3f;
        [SerializeField] private bool showHud = true;
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private AnimationClip walkClip;
        [SerializeField] private AnimationClip attackClip;
        [SerializeField, Min(0.01f)] private float attackMotionDuration = 0.45f;
        [SerializeField, Min(0.01f)] private float firstPunchDuration = 0.8f;
        [SerializeField, Min(0.01f)] private float walkCycleDuration = 1.2f;
        private Animator attackAnimator;
        private PlayableGraph animationGraph;
        private AnimationMixerPlayable animationMixer;
        private AnimationClipPlayable attackPlayable;
        private AnimationClipPlayable idlePlayable;
        private AnimationClipPlayable walkPlayable;
        private readonly SingleAttackPlayback attackPlayback = new SingleAttackPlayback();
        private Vector3 previousPosition;
        private float idleTime;
        private float walkTime;
        private float walkWeight;
        private float nextAutomaticAttack;
        private float nextTagTime;
        private float cooldownDuration;
        private Texture2D reticleTexture;
        private float swingStart = -100;
        private Quaternion restingRotation;
        private string result = "좌클릭: 스윙";
        private bool localInput = true;
        public event System.Action<float> SwingPerformed;
        public float CooldownRemaining => Mathf.Max(0, nextTagTime - Time.time);
        public float CooldownFraction => Mathf.Clamp01(CooldownRemaining / Mathf.Max(0.01f, cooldownDuration));
        public void SetLocalPlayer(bool value) { localInput = value; showHud = value; }
        private void Start()
        {
            var asset = GetComponent<CharacterVisualPrototype>()?.Definition;
            if (asset != null)
            {
                idleClip = asset.idleClip; walkClip = asset.walkClip; attackClip = asset.attackClip;
                attackMotionDuration = asset.attackDuration; firstPunchDuration = asset.attackSourceSpan;
                walkCycleDuration = asset.walkCycleDuration;
            }
            if (swingVisual != null) restingRotation = swingVisual.localRotation;
            // Registered player assets use the shared presenter; keep legacy dummy animation only.
            if (GetComponent<CharacterMotionPresenter>() != null)
            { nextAutomaticAttack = Time.time + attackInterval; return; }
            attackAnimator = GetComponentInChildren<Animator>();
            if (attackAnimator != null)
            {
                // Never leave the demo controller running, even if a clip reference is missing.
                attackAnimator.runtimeAnimatorController = null;
                attackAnimator.applyRootMotion = false;
                attackAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            if (attackAnimator != null && idleClip != null && walkClip != null && attackClip != null)
            {
                // Imported demo controllers cycle through attacks without input.
                attackAnimator.runtimeAnimatorController = null;
                attackAnimator.applyRootMotion = false;
                animationGraph = PlayableGraph.Create("Hunter input-driven animation");
                animationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                animationMixer = AnimationMixerPlayable.Create(animationGraph, 3);
                idlePlayable = AnimationClipPlayable.Create(animationGraph, idleClip);
                idlePlayable.SetTime(0);
                idlePlayable.SetSpeed(0);
                walkPlayable = AnimationClipPlayable.Create(animationGraph, walkClip);
                walkPlayable.SetTime(0);
                walkPlayable.SetSpeed(0);
                attackPlayable = AnimationClipPlayable.Create(animationGraph, attackClip);
                attackPlayable.SetTime(0);
                attackPlayable.SetSpeed(0);
                animationGraph.Connect(idlePlayable, 0, animationMixer, 0);
                animationGraph.Connect(walkPlayable, 0, animationMixer, 1);
                animationGraph.Connect(attackPlayable, 0, animationMixer, 2);
                animationMixer.SetInputWeight(0, 1);
                animationMixer.SetInputWeight(1, 0);
                animationMixer.SetInputWeight(2, 0);
                AnimationPlayableOutput.Create(animationGraph, "Hunter", attackAnimator).SetSourcePlayable(animationMixer);
                animationGraph.Play();
            }
            else Debug.LogWarning("[HunterAnimation] Animator/Idle/Walk/Punch clip missing; demo playback disabled.", this);
            previousPosition = transform.position;
            nextAutomaticAttack = Time.time + attackInterval;
        }
        private void Update()
        {
            if (swingVisual != null)
            {
                float t = Mathf.Clamp01((Time.time - swingStart) / Mathf.Max(0.01f, swingDuration));
                swingVisual.localRotation = restingRotation * Quaternion.Euler(0, 0, Mathf.Sin(t * Mathf.PI) * -65f);
            }
            bool attack = automaticAttack ? Time.time >= nextAutomaticAttack : localInput && Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked;
            if (attack) TrySwing();
        }

        public bool TrySwing()
        {
            if ((!localInput && !automaticAttack) || Time.time < nextTagTime) return false;
            nextAutomaticAttack = Time.time + Mathf.Max(0.1f, attackInterval);
            swingStart = Time.time;
            SwingPerformed?.Invoke(swingDuration);
            BeginAttackAnimation();
            PrototypeTarget closest = FindTagTarget();
            bool hitTarget = closest != null && closest.Tag();
            cooldownDuration = hitTarget ? hitCooldown : missCooldown;
            nextTagTime = Time.time + cooldownDuration;
            result = hitTarget ? "잡았다! " + closest.name : "헛스윙";
            Debug.Log("[Swing] " + name + " / " + (automaticAttack ? "dummy timer" : "left click") + " / " + result, this);
            return true;
        }

        // Match the reticle and attack to the same body-surface range, cone and occlusion rules.
        // Use the controller centre: scene actors may have a feet pivot or a centred pivot.
        private Vector3 AttackOrigin => GetComponent<CharacterController>() is CharacterController body && body.enabled
            ? body.bounds.center : transform.position;

        public PrototypeTarget FindTagTarget()
        {
            // Remote poses and local movement can change transforms before the physics step.
            Physics.SyncTransforms();
            Vector3 origin = AttackOrigin;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            PrototypeTarget closest = null;
            float closestDistance = float.MaxValue;
            foreach (var hit in Physics.OverlapSphere(origin, range, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                var target = hit.GetComponentInParent<PrototypeTarget>();
                if (target == null || !target.CanBeTagged || hit.transform.IsChildOf(transform)) continue;
                Vector3 point = hit.ClosestPoint(origin);
                Vector3 offset = point - origin;
                float distance = offset.magnitude;
                if (distance > range || Vector3.Angle(forward, Vector3.ProjectOnPlane(offset, Vector3.up)) > halfAngle) continue;
                bool blocked = false;
                if (distance > 0.001f)
                {
                    foreach (var blocker in Physics.RaycastAll(origin, offset / distance, distance,
                        Physics.AllLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (blocker.transform.IsChildOf(transform) || blocker.collider.GetComponentInParent<PrototypeTarget>() == target) continue;
                        blocked = true;
                        break;
                    }
                }
                if (!blocked && distance < closestDistance) { closest = target; closestDistance = distance; }
            }
            return closest;
        }
        private void BeginAttackAnimation()
        {
            if (!animationGraph.IsValid()) return;
            attackPlayback.Begin(Time.time, attackMotionDuration, Mathf.Min(firstPunchDuration, attackClip.length));
        }
        private void LateUpdate()
        {
            Vector3 displacement = Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up);
            previousPosition = transform.position;
            if (!animationGraph.IsValid()) return;
            float delta = Time.deltaTime;
            bool moving = delta > 0 && displacement.magnitude / delta > 0.05f;
            walkWeight = Mathf.MoveTowards(walkWeight, moving ? 1 : 0, delta / 0.1f);
            idleTime = Mathf.Repeat(idleTime + delta, Mathf.Max(0.01f, idleClip.length));
            if (moving) walkTime = Mathf.Repeat(walkTime + delta, Mathf.Max(0.01f, Mathf.Min(walkCycleDuration, walkClip.length)));
            idlePlayable.SetTime(idleTime);
            walkPlayable.SetTime(walkTime);
            attackPlayable.SetTime(attackPlayback.Sample(Time.time));
            // One short source segment only. Never advance into the remaining repeated punches.
            float attackWeight = attackPlayback.IsPlaying ? 1 : 0;
            animationMixer.SetInputWeight(0, (1 - attackWeight) * (1 - walkWeight));
            animationMixer.SetInputWeight(1, (1 - attackWeight) * walkWeight);
            animationMixer.SetInputWeight(2, attackWeight);
            animationGraph.Evaluate(0);
        }
        [ContextMenu("Log attack animation state")]
        private void LogAttackAnimationState()
        {
            Debug.Log("[HunterAnimation] " + name + " / source=" +
                (automaticAttack ? "dummy timer" : "left click") +
                " / graph=" + animationGraph.IsValid() + " / playingAttack=" + attackPlayback.IsPlaying +
                " / idle=" + (idleClip != null ? idleClip.name : "MISSING") +
                " / walk=" + (walkClip != null ? walkClip.name : "MISSING") +
                " / attack=" + (attackClip != null ? attackClip.name : "MISSING"), this);
        }
        private void OnGUI()
        {
            if (!showHud || !localInput || Event.current.type != EventType.Repaint) return;
            var camera = GetComponentInChildren<Camera>();
            if (camera == null || !camera.isActiveAndEnabled) return;
            var vision = camera.GetComponent<MicrophoneVisionController>();
            // Keep the aiming UI in darkness, but never reveal an unseen target's presence.
            bool visibleTarget = (vision == null || vision.IsCenterVisible) && FindTagTarget() != null;
            int oldDepth = GUI.depth;
            Color oldColor = GUI.color;
            GUI.depth = -2;
            float x = camera.pixelRect.center.x, y = Screen.height - camera.pixelRect.center.y;
            if (reticleTexture == null) CreateReticleTexture();
            GUI.color = new Color(0, 0, 0, 0.75f);
            GUI.DrawTexture(new Rect(x - 27, y - 27, 54, 54), reticleTexture);
            GUI.color = CooldownRemaining > 0 ? new Color(0.6f, 0.65f, 0.7f) : visibleTarget ? new Color(1f, 0.3f, 0.25f) : Color.white;
            GUI.DrawTexture(new Rect(x - 2, y - 2, 4, 4), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 10, y - 1, 4, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + 6, y - 1, 4, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1, y - 10, 2, 4), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1, y + 6, 2, 4), Texture2D.whiteTexture);
            GUI.color = new Color(1, 1, 1, 0.2f);
            GUI.DrawTexture(new Rect(x - 24, y - 24, 48, 48), reticleTexture);
            float fraction = CooldownFraction;
            GUI.color = new Color(1f, 0.72f, 0.25f);
            for (int i = 0; i < 96 * fraction; i++)
            {
                float angle = i / 96f * Mathf.PI * 2;
                GUI.DrawTexture(new Rect(x + Mathf.Sin(angle) * 22 - 1.5f, y - Mathf.Cos(angle) * 22 - 1.5f, 3, 3), Texture2D.whiteTexture);
            }
            GUI.color = oldColor;
            GUI.depth = oldDepth;
        }
        private void CreateReticleTexture()
        {
            const int size = 64;
            reticleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float radius = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                pixels[y * size + x] = new Color(1, 1, 1, 1 - Mathf.SmoothStep(0.8f, 1.8f, Mathf.Abs(radius - 29.3f)));
            }
            reticleTexture.SetPixels(pixels);
            reticleTexture.Apply(false, true);
        }
        private void OnDestroy()
        {
            if (animationGraph.IsValid()) animationGraph.Destroy();
            if (reticleTexture != null) Destroy(reticleTexture);
        }
        private void OnDrawGizmosSelected() { Gizmos.color = Color.white; Gizmos.DrawWireSphere(AttackOrigin, range); }
    }
}
