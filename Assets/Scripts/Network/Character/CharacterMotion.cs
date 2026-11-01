using System.Collections.Generic;
using Marco.Network.Player;
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
        // The Runner's sprint speed blends the run clip in above this threshold.
        [SerializeField] private float runSpeed = 5f;

        private CharacterBody body;
        private CharacterAsset asset;
        private NetworkPlayer player;
        private Transform view;
        private PlayableGraph graph;
        private AnimationMixerPlayable mixer;
        private readonly AnimationClipPlayable[] clips = new AnimationClipPlayable[5];
        private readonly AnimationClip[] sources = new AnimationClip[5];
        private readonly Dictionary<Transform, Quaternion> unmodifiedRotations = new();
        private readonly Dictionary<Transform, Vector3> unmodifiedPositions = new();
        private Vector3 previousPosition;
        private float clock, walkClock, walkWeight, runWeight, crouchWeight, treatmentPoseWeight;
        private float attackStart, attackDuration, attackSourceDuration, interactionStart, interactionDuration;
        private bool attacking, interacting;
        private float swimWeight, swimClock, swimTravel;
        private PlayerMovement movement;

        public float LookPitch { get; private set; }
        public float Speed { get; private set; }
        public bool IsAttacking => attacking;
        // Two alternating footfalls per visible locomotion cycle; audio reads this same clock.
        public bool HasFootstepCycle => graph.IsValid() && asset != null && sources[1] != null;
        // Movement is sampled from replicated positions; leave a small tolerance so 5.0 m/s
        // walking noise never crosses into the sprint sound tier.
        public bool FootstepRunning => Speed > runSpeed + 0.05f;
        public int FootstepIndex
        {
            get
            {
                if (!HasFootstepCycle) return 0;
                float period = FootstepRunning ? Mathf.Max(.01f, sources[2].length) :
                    Mathf.Max(.01f, Mathf.Min(asset.walkCycleDuration, sources[1].length));
                float rate = FootstepRunning && asset.runClip == null ? 1.6f : 1f;
                return Mathf.FloorToInt(walkClock * rate / period * 2f);
            }
        }

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            player = GetComponent<NetworkPlayer>();
            movement = GetComponent<PlayerMovement>();
            view = GetComponentInChildren<Camera>(true).transform;
            body.Rebuilt += BuildGraph;
        }

        private void BuildGraph()
        {
            if (graph.IsValid()) graph.Destroy();
            unmodifiedRotations.Clear(); unmodifiedPositions.Clear();
            attacking = interacting = false;
            swimWeight = swimTravel = 0f;
            treatmentPoseWeight = 0f;
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
            sources[4] = asset.interactionClip;
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

        // Healing and bandaging loop the asset's interaction clip when present; otherwise the
        // original skeleton performs a repeating two-arm reach and gentle forward lean.
        public void PlayTreatment(float duration)
        {
            if (asset == null) return;
            interactionStart = Time.time;
            interactionDuration = Mathf.Max(0.1f, duration);
            interacting = true;
        }

        public void StopTreatment() => interacting = false;

        private void LateUpdate()
        {
            if (!graph.IsValid() || !body.BodyRoot.gameObject.activeInHierarchy) return;
            // Swimming rotates the entire model around the head. Restore the model anchor
            // before sampling the next frame so the lean never accumulates.
            body.BodyRoot.localPosition = asset.modelOffset;
            body.BodyRoot.localRotation = Quaternion.identity;
            // Remove last frame's additive pose even when an imported clip omits a bone track.
            foreach (var pair in unmodifiedRotations) if (pair.Key != null) pair.Key.localRotation = pair.Value;
            foreach (var pair in unmodifiedPositions) if (pair.Key != null) pair.Key.localPosition = pair.Value;
            unmodifiedRotations.Clear(); unmodifiedPositions.Clear();

            float dt = Time.deltaTime;
            swimWeight = Mathf.MoveTowards(swimWeight, movement != null && movement.IsSwimming ? 1f : 0f, dt * 4f);
            swimClock += dt;
            crouchWeight = Mathf.MoveTowards(crouchWeight, player != null && player.IsCrouching ? 1f : 0f, dt * 8f);
            treatmentPoseWeight = Mathf.MoveTowards(treatmentPoseWeight, interacting ? 1f : 0f, dt * 7f);
            Speed = Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up).magnitude / Mathf.Max(dt, 0.0001f);
            swimTravel = Mathf.MoveTowards(swimTravel, Mathf.Clamp01(Speed / 2f), dt * 3f);
            previousPosition = transform.position;
            LookPitch = Mathf.Clamp(Mathf.DeltaAngle(0, (Quaternion.Inverse(transform.rotation) * view.rotation).eulerAngles.x), -70, 85);
            bool moving = Speed > 0.05f;
            walkWeight = Mathf.MoveTowards(walkWeight, moving && swimWeight < .01f ? 1 : 0, dt * 10);
            runWeight = Mathf.MoveTowards(runWeight, moving && FootstepRunning ? 1 : 0, dt * 8);
            clock += dt; if (moving) walkClock += dt;
            clips[0].SetTime(Mathf.Repeat(clock, Mathf.Max(.01f, sources[0].length)));
            clips[1].SetTime(Mathf.Repeat(walkClock, Mathf.Max(.01f, Mathf.Min(asset.walkCycleDuration, sources[1].length))));
            clips[2].SetTime(Mathf.Repeat(walkClock * (asset.runClip == null ? 1.6f : 1), Mathf.Max(.01f, sources[2].length)));

            float elapsed = Time.time - attackStart;
            if (attacking && elapsed >= attackDuration) attacking = false;
            float interactionElapsed = Time.time - interactionStart;
            if (interacting && interactionElapsed >= interactionDuration) interacting = false;
            float sample = attacking ? attackSourceDuration * elapsed / attackDuration : 0;
            float attackWeight = attacking && sources[3] != null ? 1 : 0;
            float interactionWeight = interacting && sources[4] != null && attackWeight == 0 ? 1 : 0;
            if (sources[3] != null) clips[3].SetTime(sample);
            if (sources[4] != null && interacting)
                clips[4].SetTime(Mathf.Repeat(interactionElapsed, Mathf.Max(0.01f, sources[4].length)));
            float locomotionWeight = (1 - attackWeight) * (1 - interactionWeight);
            mixer.SetInputWeight(0, locomotionWeight * (1 - walkWeight));
            mixer.SetInputWeight(1, locomotionWeight * walkWeight * (1 - runWeight));
            mixer.SetInputWeight(2, locomotionWeight * walkWeight * runWeight);
            mixer.SetInputWeight(3, attackWeight);
            mixer.SetInputWeight(4, interactionWeight);
            graph.Evaluate(0);

            // Humanoid torso follows gaze; Generic rigs may have independent wrist roots.
            bool humanoid = body.BodyAnimator.isHuman;
            Tilt(body.ResolveBone(HumanBodyBones.Chest), humanoid ? LookPitch * .25f : 0);
            Tilt(body.ResolveBone(HumanBodyBones.Neck), LookPitch * (humanoid ? .3f : .4f));
            Tilt(body.ResolveBone(HumanBodyBones.Head), LookPitch * (humanoid ? .45f : .6f));
            ApplyKneelingPose(crouchWeight);
            if (interacting && sources[4] == null && !attacking)
            {
                float reach = 0.5f + 0.5f * Mathf.Sin(interactionElapsed * (Mathf.PI * 2f / 1.2f));
                Tilt(body.ResolveBone(HumanBodyBones.Chest), 8f * reach);
                RaiseArm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand, reach);
                RaiseArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand, reach);
            }
            else if (attacking && sources[3] == null)
                RaiseArm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand, Mathf.Sin(elapsed / attackDuration * Mathf.PI));
            ApplyTreatmentPose(treatmentPoseWeight * (1f - swimWeight));
            ApplySwimmingPose();
        }

        // Original procedural strokes: upright sculling at rest, alternating crawl on the
        // surface, symmetric pull/glide underwater. World-space axes support both rig types.
        private void ApplySwimmingPose()
        {
            if (swimWeight < .001f) return;
            float travel = swimTravel;
            bool diving = movement != null && movement.IsDiving;
            float phase = swimClock * Mathf.PI * 2f / (diving ? 1.6f : 1.25f);
            var head = body.ResolveBone(HumanBodyBones.Head);
            var root = body.BodyRoot;
            if (root != null)
            {
                Quaternion lean = Quaternion.AngleAxis((diving ? 72f : Mathf.Lerp(8f, 55f, travel)) * swimWeight, transform.right);
                Vector3 anchor = head != null ? head.position : root.position + Vector3.up;
                root.rotation = lean * root.rotation;
                root.position = anchor + lean * (root.position - anchor);
                if (head != null)
                    root.position += Vector3.up * ((transform.position.y + .23f - head.position.y) * swimWeight);
            }
            if (player != null && player.Role == Marco.Core.WinCondition.PlayerRole.Seeker)
            {
                ApplySeekerSwimmingPose(travel, phase);
                return;
            }
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                float cycle = phase + (left || diving ? 0f : Mathf.PI);
                float stroke = Mathf.Sin(cycle);
                float recovery = Mathf.Max(0f, Mathf.Cos(cycle));
                float arm = Mathf.Lerp(-15f + stroke * 8f, -45f + stroke * 40f, travel);
                Tilt(body.ResolveBone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm), arm * swimWeight);
                Tilt(body.ResolveBone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm), (-25f - recovery * 35f) * swimWeight);
                TuckArm(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm,
                    (left ? 1 : -1) * (35f - travel * 15f + recovery * 10f) * swimWeight);
                BendLeg(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg,
                    (Mathf.Sin(phase * 2f + side * Mathf.PI) * 12f - (1f - travel) * 12f) * swimWeight);
                BendLeg(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg,
                    (30f + recovery * 18f) * swimWeight);
                BendLeg(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, 12f * swimWeight);
            }
            Tilt(head, -15f * travel * swimWeight);
        }

        private void ApplySeekerSwimmingPose(float travel, float phase)
        {
            // Use the tilted body's axes, with directions derived from actual joints rather
            // than adding rotations to the imported idle pose's arbitrary bone axes.
            var root = body.BodyRoot;
            Vector3 up = root.up, front = root.forward, right = root.right;
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                float sign = left ? -1f : 1f;
                float cycle = phase + side * Mathf.PI;
                float reach = Mathf.Cos(cycle);
                Vector3 upperArm = Vector3.Lerp(-up * .25f + front * .5f + right * (sign * .55f),
                    up * (.15f + .75f * reach) + front * .55f + right * (sign * .25f), travel);
                Vector3 forearm = Vector3.Lerp(front * .7f - up * .15f + right * (sign * (.25f + .1f * reach)),
                    up * (.35f + .55f * reach) + front * .35f - right * (sign * .12f), travel);
                PoseSwimmingLimb(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm,
                    left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm,
                    left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, upperArm, forearm);

                float kick = Mathf.Sin(phase * 2f + side * Mathf.PI);
                PoseSwimmingLimb(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg,
                    left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg,
                    left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot,
                    -up + front * (.12f + .16f * kick) + right * (sign * .12f),
                    -up - front * (.3f + .12f * kick) + right * (sign * .08f));
            }
            Tilt(body.ResolveBone(HumanBodyBones.Head), -15f * travel * swimWeight);
        }

        private void PoseSwimmingLimb(HumanBodyBones upperId, HumanBodyBones lowerId,
            HumanBodyBones endId, Vector3 upperDirection, Vector3 lowerDirection)
        {
            var upper = body.ResolveBone(upperId);
            var lower = body.ResolveBone(lowerId);
            var end = body.ResolveBone(endId);
            if (upper == null || lower == null || end == null || !lower.IsChildOf(upper)) return;

            // Demon.fbx: Hand.L/R and Foot.L/R are siblings of Pelvis under Demon,
            // not children of Arm2/Leg2. Preserve their animated attachment to the lower
            // segment explicitly so the skinned wrist/ankle cannot stay behind.
            bool detached = !end.IsChildOf(lower);
            Vector3 endpoint = lower.InverseTransformPoint(end.position);
            Quaternion endRotation = Quaternion.Inverse(lower.rotation) * end.rotation;
            Remember(upper); Remember(lower);
            if (detached) Remember(end);
            AimSwimmingSegment(upper, lower.position - upper.position, upperDirection);
            if (detached)
            {
                end.position = lower.TransformPoint(endpoint);
                end.rotation = lower.rotation * endRotation;
            }
            AimSwimmingSegment(lower, end.position - lower.position, lowerDirection);
            if (detached)
            {
                end.position = lower.TransformPoint(endpoint);
                end.rotation = lower.rotation * endRotation;
            }
        }

        private void AimSwimmingSegment(Transform bone, Vector3 current, Vector3 desired)
        {
            if (current.sqrMagnitude < .000001f || desired.sqrMagnitude < .000001f) return;
            Quaternion turn = Quaternion.FromToRotation(current, desired);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, turn, swimWeight) * bone.rotation;
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

        // A one-knee crouch: the left knee folds under the body while the right foot stays planted
        // in front. Moving the pelvis and bending only the legs keeps the torso and head in shape.
        private void ApplyKneelingPose(float amount)
        {
            if (amount < .001f) return;

            var hips = body.ResolveBone(HumanBodyBones.Hips);
            if (hips != null)
            {
                Remember(hips);
                hips.localPosition += Vector3.down * (0.36f * amount);
            }

            BendLeg(HumanBodyBones.LeftUpperLeg, 5f * amount);
            BendLeg(HumanBodyBones.LeftLowerLeg, 100f * amount);
            BendLeg(HumanBodyBones.LeftFoot, -90f * amount);
            BendLeg(HumanBodyBones.RightUpperLeg, -78f * amount);
            BendLeg(HumanBodyBones.RightLowerLeg, 78f * amount);
        }

        // A small forward fold and tucked elbows make the treatment stance read as a crouch.
        // This additive pose also works on rigs that have an imported interaction clip.
        private void ApplyTreatmentPose(float amount)
        {
            if (amount < .001f) return;

            Tilt(body.ResolveBone(HumanBodyBones.Spine), 7f * amount);
            Tilt(body.ResolveBone(HumanBodyBones.Chest), 11f * amount);
            TuckArm(HumanBodyBones.LeftUpperArm, 14f * amount);
            TuckArm(HumanBodyBones.LeftLowerArm, 22f * amount);
            TuckArm(HumanBodyBones.RightUpperArm, -14f * amount);
            TuckArm(HumanBodyBones.RightLowerArm, -22f * amount);
        }

        private void TuckArm(HumanBodyBones name, float angle)
        {
            var bone = body.ResolveBone(name);
            if (bone == null || Mathf.Abs(angle) < .001f) return;
            Remember(bone);
            bone.rotation = Quaternion.AngleAxis(angle, transform.forward) * bone.rotation;
        }

        private void BendLeg(HumanBodyBones name, float angle)
        {
            var bone = body.ResolveBone(name);
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
