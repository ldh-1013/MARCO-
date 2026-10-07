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
        private float swingStart = -100;
        private Quaternion restingRotation;
        private string result = "좌클릭: 스윙";
        private void Start()
        {
            if (swingVisual != null) restingRotation = swingVisual.localRotation;
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
            bool attack = automaticAttack ? Time.time >= nextAutomaticAttack : Input.GetMouseButtonDown(0) && Cursor.lockState == CursorLockMode.Locked;
            if (!attack || Time.time < nextTagTime) return;
            nextAutomaticAttack = Time.time + Mathf.Max(0.1f, attackInterval);
            swingStart = Time.time;
            BeginAttackAnimation();
            PrototypeTarget closest = null; float closestDistance = float.MaxValue;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            foreach (var hit in Physics.OverlapSphere(transform.position, range))
            {
                var target = hit.GetComponentInParent<PrototypeTarget>();
                if (target == null || !target.CanBeTagged || target.transform.root == transform.root) continue;
                Vector3 offset = target.transform.position - transform.position;
                float distance = offset.magnitude;
                if (distance > range || Vector3.Angle(forward, Vector3.ProjectOnPlane(offset, Vector3.up)) > halfAngle) continue;
                if (Physics.Linecast(transform.position, target.transform.position, out var blocker) && blocker.collider.GetComponentInParent<PrototypeTarget>() != target) continue;
                if (distance < closestDistance) { closest = target; closestDistance = distance; }
            }
            bool hitTarget = closest != null && closest.Tag();
            nextTagTime = Time.time + (hitTarget ? hitCooldown : missCooldown);
            result = hitTarget ? "잡았다! " + closest.name : "헛스윙";
            Debug.Log("[Swing] " + name + " / " + (automaticAttack ? "dummy timer" : "left click") + " / " + result, this);
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
            if (!showHud) return;
            var camera = GetComponentInChildren<Camera>();
            if (camera == null) return;
            var vision = camera.GetComponent<MicrophoneVisionController>();
            if (vision == null || !vision.IsCenterVisible) return;
            GUI.depth = -2;
            float x = Screen.width * 0.5f, y = Screen.height * 0.5f;
            if (Physics.Raycast(camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0)), out var aimed, range + 1f))
            {
                var target = aimed.collider.GetComponentInParent<PrototypeTarget>();
                if (target != null && target.CanBeTagged && Vector3.Distance(transform.position, target.transform.position) <= range)
                {
                    Color old = GUI.color; GUI.color = Color.red;
                    GUI.DrawTexture(new Rect(x - 3, y - 3, 6, 6), Texture2D.whiteTexture); GUI.color = old;
                }
            }
            float remaining = Mathf.Max(0, nextTagTime - Time.time);
            float fraction = remaining / Mathf.Max(0.01f, result.StartsWith("잡았다") ? hitCooldown : missCooldown);
            for (int i = 0; i < 32 * fraction; i++)
            {
                float angle = i / 32f * Mathf.PI * 2;
                GUI.DrawTexture(new Rect(x + Mathf.Sin(angle) * 18 - 1, y - Mathf.Cos(angle) * 18 - 1, 3, 3), Texture2D.whiteTexture);
            }
            GUI.Label(new Rect(x - 130, y + 30, 280, 25), result);
            GUI.Label(new Rect(x - 130, y + 55, 280, 25), "쿨다운 " + Mathf.Max(0, nextTagTime - Time.time).ToString("F1") + "초");
        }
        private void OnDestroy() { if (animationGraph.IsValid()) animationGraph.Destroy(); }
        private void OnDrawGizmosSelected() { Gizmos.color = Color.white; Gizmos.DrawWireSphere(transform.position, range); }
    }
}
