using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Marco.Prototype
{
    // One original asset skeleton drives both the visible world body and own-view limbs.
    [DefaultExecutionOrder(50)]
    public sealed class CharacterMotionPresenter : MonoBehaviour
    {
        private CharacterVisualPrototype visual;
        private CharacterAssetDefinition asset;
        private PrototypeFirstPersonController movement;
        private Camera view;
        private HunterTagPrototype hunter;
        private SurvivorInteractionPrototype interaction;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[5];
        private readonly AnimationClip[] sources = new AnimationClip[5];
        private readonly Dictionary<Transform, Quaternion> unmodifiedRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> unmodifiedPositions = new Dictionary<Transform, Vector3>();
        private readonly SingleAttackPlayback attack = new SingleAttackPlayback();
        private Vector3 previousPosition;
        private float clock, walkClock, walkWeight, runWeight, interactionStart = -100, interactionDuration;
        private bool remote;
        private float remotePitch, remoteSpeed;
        private bool remoteRunning;
        public float LookPitch { get; private set; }
        public float Speed { get; private set; }
        public bool Running { get; private set; }
        public bool IsReady => graph.IsValid();
        public bool IsAttacking => attack.IsPlaying;
        public bool IsInteracting => Time.time < interactionStart + interactionDuration;

        private void Start()
        {
            visual = GetComponent<CharacterVisualPrototype>(); asset = visual.Definition;
            movement = GetComponent<PrototypeFirstPersonController>(); view = GetComponentInChildren<Camera>(true);
            hunter = GetComponent<HunterTagPrototype>(); interaction = GetComponent<SurvivorInteractionPrototype>();
            if (hunter != null) hunter.SwingPerformed += PlayAttack;
            if (interaction != null) interaction.InteractionPerformed += PlayInteraction;
            var animator = visual.BodyAnimator;
            if (asset == null || animator == null || asset.idleClip == null || asset.walkClip == null)
            { Debug.LogError("Character asset requires an Animator and idle/walk clips.", this); enabled = false; return; }
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            sources[0] = asset.idleClip; sources[1] = asset.walkClip;
            sources[2] = asset.runClip != null ? asset.runClip : asset.walkClip;
            sources[3] = asset.attackClip; sources[4] = asset.interactionClip;
            graph = PlayableGraph.Create("Registered character motion"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, sources.Length);
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] == null) continue;
                clips[i] = AnimationClipPlayable.Create(graph, sources[i]); clips[i].SetSpeed(0);
                graph.Connect(clips[i], 0, mixer, i);
            }
            AnimationPlayableOutput.Create(graph, "Native asset skeleton", animator).SetSourcePlayable(mixer);
            mixer.SetInputWeight(0, 1); graph.Play(); graph.Evaluate(0); previousPosition = transform.position;
        }

        public void SetRemotePose(float pitch, float speed, bool running)
        { remote = true; remotePitch = Mathf.Clamp(pitch, -70, 85); remoteSpeed = Mathf.Max(0, speed); remoteRunning = running; }
        public void PlayAttack(float ignoredDuration)
        {
            var definition = asset != null ? asset : GetComponent<CharacterVisualPrototype>().Definition;
            if (definition == null) return;
            attack.Begin(Time.time, definition.attackDuration, definition.attackClip == null ? definition.attackDuration
                : Mathf.Min(definition.attackSourceSpan, definition.attackClip.length));
        }
        public void PlayInteraction(bool succeeded)
        { interactionStart = Time.time; interactionDuration = succeeded ? 0.65f : 0.4f; }

        private void LateUpdate()
        {
            if (!graph.IsValid()) return;
            // Remove last frame's additive pose even when an imported clip omits a bone track.
            foreach (var pair in unmodifiedRotations) if (pair.Key != null) pair.Key.localRotation = pair.Value;
            foreach (var pair in unmodifiedPositions) if (pair.Key != null) pair.Key.localPosition = pair.Value;
            unmodifiedRotations.Clear(); unmodifiedPositions.Clear();
            float dt = Time.deltaTime;
            Speed = remote ? remoteSpeed : Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up).magnitude / Mathf.Max(dt, 0.0001f);
            previousPosition = transform.position;
            Running = remote ? remoteRunning : movement != null && movement.enabled && movement.IsRunning;
            LookPitch = remote ? remotePitch : view == null ? 0 : Mathf.DeltaAngle(0, view.transform.localEulerAngles.x);
            LookPitch = Mathf.Clamp(LookPitch, -70, 85);
            bool moving = Speed > 0.05f;
            walkWeight = Mathf.MoveTowards(walkWeight, moving ? 1 : 0, dt * 10);
            runWeight = Mathf.MoveTowards(runWeight, moving && Running ? 1 : 0, dt * 8);
            clock += dt; if (moving) walkClock += dt;
            clips[0].SetTime(Mathf.Repeat(clock, Mathf.Max(.01f, sources[0].length)));
            clips[1].SetTime(Mathf.Repeat(walkClock, Mathf.Max(.01f, Mathf.Min(asset.walkCycleDuration, sources[1].length))));
            clips[2].SetTime(Mathf.Repeat(walkClock * (asset.runClip == null ? 1.6f : 1), Mathf.Max(.01f, sources[2].length)));
            double sample = attack.Sample(Time.time);
            float attackWeight = attack.IsPlaying && sources[3] != null ? 1 : 0;
            float interactWeight = IsInteracting && sources[4] != null ? 1 : 0;
            if (sources[3] != null) clips[3].SetTime(sample);
            if (sources[4] != null) clips[4].SetTime(Mathf.Clamp01((Time.time - interactionStart) / interactionDuration) * sources[4].length);
            float baseWeight = (1 - attackWeight) * (1 - interactWeight);
            mixer.SetInputWeight(0, baseWeight * (1 - walkWeight));
            mixer.SetInputWeight(1, baseWeight * walkWeight * (1 - runWeight));
            mixer.SetInputWeight(2, baseWeight * walkWeight * runWeight);
            mixer.SetInputWeight(3, attackWeight); mixer.SetInputWeight(4, interactWeight * (1 - attackWeight));
            graph.Evaluate(0);
            // Humanoid torso follows gaze; Generic rigs may have independent wrist roots.
            bool humanoid = visual.BodyAnimator.isHuman;
            Tilt(visual.ResolveBone(HumanBodyBones.Chest), humanoid ? LookPitch * .25f : 0);
            Tilt(visual.ResolveBone(HumanBodyBones.Neck), LookPitch * (humanoid ? .3f : .4f));
            Tilt(visual.ResolveBone(HumanBodyBones.Head), LookPitch * (humanoid ? .45f : .6f));
            if (IsInteracting && sources[4] == null)
            {
                float reach = Mathf.Sin(Mathf.Clamp01((Time.time - interactionStart) / interactionDuration) * Mathf.PI);
                RaiseArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand, reach);
                RaiseArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand, reach);
            }
            else if (attack.IsPlaying && sources[3] == null)
                RaiseArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand, (float)Mathf.Sin((float)(sample / Mathf.Max(.01f, asset.attackDuration)) * Mathf.PI));
        }
        private void Remember(Transform bone)
        {
            if (!unmodifiedRotations.ContainsKey(bone)) unmodifiedRotations.Add(bone, bone.localRotation);
            if (!unmodifiedPositions.ContainsKey(bone)) unmodifiedPositions.Add(bone, bone.localPosition);
        }
        private void Tilt(Transform bone, float angle)
        { if (bone == null || Mathf.Abs(angle) < .001f) return; Remember(bone); bone.rotation = Quaternion.AngleAxis(angle, transform.right) * bone.rotation; }
        private void RaiseArm(HumanBodyBones upperName, HumanBodyBones handName, float amount)
        {
            var upper = visual.ResolveBone(upperName); var hand = visual.ResolveBone(handName);
            if (upper == null || hand == null) return;
            Remember(upper); var turn = Quaternion.AngleAxis(-55 * amount, transform.right);
            Vector3 wrist = hand.position; Quaternion wristRotation = hand.rotation;
            upper.rotation = turn * upper.rotation;
            if (!hand.IsChildOf(upper))
            { Remember(hand); hand.position = upper.position + turn * (wrist - upper.position); hand.rotation = turn * wristRotation; }
        }
        private void OnDestroy()
        {
            if (hunter != null) hunter.SwingPerformed -= PlayAttack;
            if (interaction != null) interaction.InteractionPerformed -= PlayInteraction;
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
