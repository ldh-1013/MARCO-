using UnityEngine;

namespace Marco.Prototype
{
    [DefaultExecutionOrder(20)]
    [DisallowMultipleComponent]
    public sealed class PlayerPoseReplicator : MonoBehaviour
    {
        public int PlayerId { get; private set; }
        public bool IsLocal { get; private set; }
        public uint ReceivedSequence => latest.Sequence;
        public uint PublishedSequence => sequence;
        private RoomSessionPrototype room;
        private CharacterMotionPresenter motion;
        private PrototypeFirstPersonController movement;
        private HunterTagPrototype hunter;
        private SurvivorInteractionPrototype interaction;
        private Camera cameraView;
        private Vector3 previousPosition;
        private float sendAt, receivedAt;
        private uint sequence, attacks, interactions, appliedAttack, appliedInteraction;
        private bool succeeded, hasPose;
        private PlayerPoseState latest;

        public void Configure(int playerId, bool local) { PlayerId = playerId; IsLocal = local; }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void InheritDevelopmentStream(PlayerPoseReplicator previous)
        {
            if (previous == null || previous.PlayerId != PlayerId || previous.IsLocal != IsLocal) return;
            // Keep counters monotonic across replacement. Otherwise the host would
            // reject sequence=1 and actions would replay or stop after role changes.
            sequence = previous.sequence; attacks = previous.attacks; interactions = previous.interactions;
            appliedAttack = previous.appliedAttack; appliedInteraction = previous.appliedInteraction;
            latest = previous.latest; hasPose = previous.hasPose; receivedAt = previous.receivedAt;
            succeeded = previous.succeeded;
        }
#endif
        private void Start()
        {
            room = RoomSessionPrototype.Instance; motion = GetComponent<CharacterMotionPresenter>();
            movement = GetComponent<PrototypeFirstPersonController>(); cameraView = GetComponentInChildren<Camera>(true);
            hunter = GetComponent<HunterTagPrototype>(); interaction = GetComponent<SurvivorInteractionPrototype>();
            previousPosition = transform.position;
            if (room == null) { enabled = false; return; }
            room.PlayerLeft += OnPlayerLeft;
            if (IsLocal)
            {
                if (hunter != null) hunter.SwingPerformed += OnAttack;
                if (interaction != null) interaction.InteractionPerformed += OnInteraction;
            }
            else
            {
                room.PoseReceived += Receive;
                motion?.SetRemotePose(0, 0, false);
                if (room.TryGetPose(PlayerId, out var pose)) Receive(pose);
            }
        }
        private void OnAttack(float duration) { attacks++; }
        private void OnInteraction(bool value) { interactions++; succeeded = value; }
        private void OnPlayerLeft(int id) { if (!IsLocal && id == PlayerId) Destroy(gameObject); }
        private void Receive(PlayerPoseState pose)
        {
            if (IsLocal || pose.PlayerId != PlayerId || pose.Sequence <= latest.Sequence) return;
            latest = pose; receivedAt = Time.unscaledTime;
            if (!hasPose)
            { transform.position = new Vector3(pose.X, pose.Y, pose.Z); transform.rotation = Quaternion.Euler(0, pose.Yaw, 0); }
            hasPose = true;
            if (motion == null) motion = GetComponent<CharacterMotionPresenter>();
            if (pose.Attack > appliedAttack) { motion?.PlayAttack(0); appliedAttack = pose.Attack; }
            if (pose.Interaction > appliedInteraction) { motion?.PlayInteraction(pose.InteractionSucceeded); appliedInteraction = pose.Interaction; }
        }
        private void Update()
        {
            if (!IsLocal)
            {
                if (!hasPose) return;
                float blend = 1 - Mathf.Exp(-Time.unscaledDeltaTime * 15);
                transform.position = Vector3.Lerp(transform.position, new Vector3(latest.X, latest.Y, latest.Z), blend);
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0, latest.Yaw, 0), blend);
                // Expire locomotion when updates stop; do not keep running in place indefinitely.
                bool fresh = Time.unscaledTime - receivedAt < .3f;
                motion?.SetRemotePose(Mathf.Lerp(motion.LookPitch, latest.Pitch, blend), fresh ? latest.Speed : 0, fresh && latest.Running);
                return;
            }
            float speed = Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up).magnitude / Mathf.Max(.0001f, Time.deltaTime);
            previousPosition = transform.position;
            if (Time.unscaledTime < sendAt) return;
            sendAt = Time.unscaledTime + .05f;
            room.PublishPose(new PlayerPoseState {
                PlayerId = PlayerId, Sequence = ++sequence,
                X = transform.position.x, Y = transform.position.y, Z = transform.position.z,
                Yaw = Mathf.DeltaAngle(0, transform.eulerAngles.y),
                Pitch = cameraView == null ? 0 : Mathf.Clamp(Mathf.DeltaAngle(0, cameraView.transform.localEulerAngles.x), -70, 85),
                Speed = Mathf.Clamp(speed, 0, 30), Running = movement != null && movement.enabled && movement.IsRunning,
                Attack = attacks, Interaction = interactions, InteractionSucceeded = succeeded });
        }
        private void OnDestroy()
        {
            if (room != null) { room.PoseReceived -= Receive; room.PlayerLeft -= OnPlayerLeft; }
            if (hunter != null) hunter.SwingPerformed -= OnAttack;
            if (interaction != null) interaction.InteractionPerformed -= OnInteraction;
        }
    }
}
