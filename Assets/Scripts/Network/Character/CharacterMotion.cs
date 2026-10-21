using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Marco.Network.Character
{
    // Idle/walk/run/attack blend on the model's own skeleton, plus gaze tilt of chest/neck/head.
    // Ported from Prototype CharacterMotionPresenter. Speed comes from the player's movement,
    // which NetworkTransform already replicates, so remote players need no extra packets.
    // Pitch comes from the camera pivot; remote pivots are not synced, so they look level.
    [DefaultExecutionOrder(50)]
    [RequireComponent(typeof(CharacterBody))]
    public sealed class CharacterMotion : MonoBehaviour
    {
        // PlayerMovement has no sprint yet; the run clip blends in above this speed.
        [SerializeField] private float runSpeed = 6f;

        private CharacterBody body;
        private CharacterAsset asset;
        private Transform view;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[4];
        private readonly AnimationClip[] sources = new AnimationClip[4];
        private readonly Dictionary<Transform, Quaternion> unmodifiedRotations = new();
        private readonly Dictionary<Transform, Vector3> unmodifiedPositions = new();
        private Vector3 previousPosition;
        private float clock, walkClock, walkWeight, runWeight;
        private float attackStart, attackDuration, attackSourceDuration;
        private bool attacking;

        public float LookPitch { get; private set; }
        public float Speed { get; private set; }
        public bool IsAttacking => attacking;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            view = GetComponentInChildren<Camera>(true).transform;
            body.Rebuilt += BuildGraph;
        }

        private void BuildGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            unmodifiedRotations.Clear(); unmodifiedPositions.Clear();
            attacking = false;
            asset = body.Definition;
            var animator = body.BodyAnimator;
            if (asset == null) return;
            if (animator == null || asset.idleClip == null || asset.walkClip == null)
            { Debug.LogError($"Character asset {asset.name} requires an Animator and idle/walk clips.", this); return; }
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            sources[0] = asset.idleClip; sources[1] = asset.walkClip;
            sources[2] = asset.runClip != null ? asset.runClip : asset.walkClip;
            sources[3] = asset.attackClip;
            graph = PlayableGraph.Create("Character motion"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, sources.Length);
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == null) continue;
                clips[i] = AnimationClipPlayable.Create(graph, sources[i]); clips[i].SetSpeed(0);
                graph.Connect(clips[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Character skeleton", animator).SetSourcePlayable(mixer);
            mixer.SetInputWeight(0, 1); graph.Play(); graph.Evaluate(0);
            previousPosition = transform.position;
        }

        // Plays one swing; the wall-clock length is shorter than the imported clip.
        public void PlayAttack()
        {
            if (asset == null) return;
            attackStart = Time.time;
            attackDuration = Mathf.Max(0.01f, asset.attackDuration);
            attackSourceDuration = Mathf.Max(0.01f, asset.attackClip == null ? asset.attackDuration
                : Mathf.Min(asset.attackSourceSpan, asset.attackClip.length));
            attacking = true;
        }

        private void LateUpdate()
        {
            if (!graph.IsValid() || !body.BodyRoot.gameObject.activeInHierarchy) return;
            // Remove last frame's additive pose even when an imported clip omits a bone track.
            foreach (var pair in unmodifiedRotations) if (pair.Key != null) pair.Key.localRotation = pair.Value;
            foreach (var pair in unmodifiedPositions) if (pair.Key != null) pair.Key.localPosition = pair.Value;
            unmodifiedRotations.Clear(); unmodifiedPositions.Clear();

            float dt = Time.deltaTime;
            Speed = Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up).magnitude / Mathf.Max(dt, 0.0001f);
            previousPosition = transform.position;
            LookPitch = Mathf.Clamp(Mathf.DeltaAngle(0, (Quaternion.Inverse(transform.rotation) * view.rotation).eulerAngles.x), -70, 85);
            bool moving = Speed > 0.05f;
            walkWeight = Mathf.MoveTowards(walkWeight, moving ? 1 : 0, dt * 10);
            runWeight = Mathf.MoveTowards(runWeight, moving && Speed > runSpeed ? 1 : 0, dt * 8);
            clock += dt; if (moving) walkClock += dt;
            clips[0].SetTime(Mathf.Repeat(clock, Mathf.Max(.01f, sources[0].length)));
            clips[1].SetTime(Mathf.Repeat(walkClock, Mathf.Max(.01f, Mathf.Min(asset.walkCycleDuration, sources[1].length))));
            clips[2].SetTime(Mathf.Repeat(walkClock * (asset.runClip == null ? 1.6f : 1), Mathf.Max(.01f, sources[2].length)));

            float elapsed = Time.time - attackStart;
            if (attacking && elapsed >= attackDuration) attacking = false;
            float sample = attacking ? attackSourceDuration * elapsed / attackDuration : 0;
            float attackWeight = attacking && sources[3] != null ? 1 : 0;
            if (sources[3] != null) clips[3].SetTime(sample);
            mixer.SetInputWeight(0, (1 - attackWeight) * (1 - walkWeight));
            mixer.SetInputWeight(1, (1 - attackWeight) * walkWeight * (1 - runWeight));
            mixer.SetInputWeight(2, (1 - attackWeight) * walkWeight * runWeight);
            mixer.SetInputWeight(3, attackWeight);
            graph.Evaluate(0);

            // Humanoid torso follows gaze; Generic rigs may have independent wrist roots.
            bool humanoid = body.BodyAnimator.isHuman;
            Tilt(body.ResolveBone(HumanBodyBones.Chest), humanoid ? LookPitch * .25f : 0);
            Tilt(body.ResolveBone(HumanBodyBones.Neck), LookPitch * (humanoid ? .3f : .4f));
            Tilt(body.ResolveBone(HumanBodyBones.Head), LookPitch * (humanoid ? .45f : .6f));
            if (attacking && sources[3] == null)
                RaiseArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand, Mathf.Sin(elapsed / attackDuration * Mathf.PI));
        }

        private void Remember(Transform bone)
        {
            if (!unmodifiedRotations.ContainsKey(bone)) unmodifiedRotations.Add(bone, bone.localRotation);
            if (!unmodifiedPositions.ContainsKey(bone)) unmodifiedPositions.Add(bone, bone.localPosition);
        }

        private void Tilt(Transform bone, float angle)
        {
            if (bone == null || Mathf.Abs(angle) < .001f) return;
            Remember(bone);
            bone.rotation = Quaternion.AngleAxis(angle, transform.right) * bone.rotation;
        }

        private void RaiseArm(HumanBodyBones upperName, HumanBodyBones handName, float amount)
        {
            var upper = body.ResolveBone(upperName); var hand = body.ResolveBone(handName);
            if (upper == null || hand == null) return;
            Remember(upper);
            var turn = Quaternion.AngleAxis(-55 * amount, transform.right);
            Vector3 wrist = hand.position; Quaternion wristRotation = hand.rotation;
            upper.rotation = turn * upper.rotation;
            if (!hand.IsChildOf(upper))
            { Remember(hand); hand.position = upper.position + turn * (wrist - upper.position); hand.rotation = turn * wristRotation; }
        }

        private void OnDestroy()
        {
            body.Rebuilt -= BuildGraph;
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
