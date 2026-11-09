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

        [Header("Runner and Echo (unity-chan!; an Echo is the same model, see-through)")]
        public AnimationClip runnerWin;
        public AnimationClip runnerLose;

        public static ResultPodiumAssets Load() => Resources.Load<ResultPodiumAssets>(ResourcePath);
    }
}
