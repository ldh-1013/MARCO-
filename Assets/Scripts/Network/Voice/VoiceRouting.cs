using System;
using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Voice
{
    // Microphone loudness grade (기획서 5.1): whisper −50~−30, talk −30~−15, shout −15 dBFS and up.
    public enum VoiceGrade : byte { Whisper, Talk, Shout }

    // 기획서 13.2: three in-round channels with nothing crossing between them. Before the round and
    // once the result is decided everyone talks together (6.3 결과 화면 전원 합류).
    public enum VoiceChannel : byte { Everyone, Game, Echo, Escaped }

    // Server-side rule for "does this listener hear that speaker". Values are tuned on the
    // player prefab's PlayerVoice; the server uses its own copy.
    [Serializable]
    public sealed class VoiceRouting
    {
        [SerializeField] private float whisperRadius = 4f;
        [SerializeField] private float talkRadius = 9f;
        [SerializeField] private float shoutRadius = 22f;
        // 기획서 5.7: the Seeker hears 1.2× farther.
        [SerializeField] private float seekerHearingMultiplier = 1.2f;
        // 기획서 5.6: each wall between them halves the radius.
        [SerializeField, Range(0, 1)] private float wallAttenuation = 0.5f;
        // What counts as a wall. Everything until the map gets a SoundBlocking layer
        // (walls and locker rows only); until then furniture blocks voice too.
        [SerializeField] private LayerMask occluders = ~0;

        private readonly RaycastHit[] hits = new RaycastHit[16];

        public static VoiceChannel ChannelOf(NetworkPlayer player)
        {
            var round = NetworkRound.Instance;
            if (round == null || round.Mode == RoundMode.NotStarted || round.Result != RoundResult.InProgress)
                return VoiceChannel.Everyone;
            return player.Status switch
            {
                PlayerStatus.Alive => VoiceChannel.Game,
                PlayerStatus.Tagged => VoiceChannel.Echo,
                _ => VoiceChannel.Escaped, // escaped, or joined mid-round and spectating
            };
        }

        // radius > 0: proximity (play in 3D up to that distance); 0: channel-wide (play in 2D).
        public bool TryGetHearing(NetworkPlayer speaker, NetworkPlayer listener, VoiceGrade grade, out float radius)
        {
            radius = 0f;
            var channel = ChannelOf(speaker);
            if (channel != ChannelOf(listener)) return false;
            if (channel != VoiceChannel.Game) return true;

            radius = grade switch
            {
                VoiceGrade.Whisper => whisperRadius,
                VoiceGrade.Talk => talkRadius,
                _ => shoutRadius,
            };
            if (listener.Role == PlayerRole.Seeker) radius *= seekerHearingMultiplier;
            Vector3 from = speaker.transform.position, to = listener.transform.position;
            float distance = Vector3.Distance(from, to);
            if (distance > radius) return false;
            radius *= Mathf.Pow(wallAttenuation, CountWalls(from, to, distance));
            return distance <= radius;
        }

        // Players' own CharacterControllers are not walls.
        private int CountWalls(Vector3 from, Vector3 to, float distance)
        {
            if (distance <= 0.001f) return 0;
            int count = Physics.RaycastNonAlloc(from, (to - from) / distance, hits, distance, occluders, QueryTriggerInteraction.Ignore);
            int walls = 0;
            for (int i = 0; i < count; i++)
                if (hits[i].collider.GetComponentInParent<NetworkPlayer>() == null) walls++;
            return walls;
        }
    }
}
