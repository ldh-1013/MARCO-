using Marco.Network.Character;
using UnityEngine;

namespace Marco.Network.Round.ResultPodium
{
    // 표시 전용. Models and poses for the result podium, loaded from Resources at runtime.
    // Regenerate with MARCO > Result > Create podium assets (only fills empty fields).
    public sealed class ResultPodiumAssets : ScriptableObject
    {
        public const string ResourcePath = "ResultPodium/ResultPodiumAssets";

        [Header("Same CharacterAssets as the players' CharacterBody")]
        public CharacterAsset runner;
        public CharacterAsset seeker;

        [Header("Runner (unity-chan!)")]
        public AnimationClip runnerWin;
        public AnimationClip runnerLose;

        [Header("Echo (GhostCharacter_Free)")]
        public GameObject echoPrefab;
        public AnimationClip echoWin;
        public AnimationClip echoLose;
        [Tooltip("Echo model height on the podium, in metres.")]
        public float echoHeight = 1.25f;

        public static ResultPodiumAssets Load() => Resources.Load<ResultPodiumAssets>(ResourcePath);
    }
}
