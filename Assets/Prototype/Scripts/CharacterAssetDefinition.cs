using System;
using UnityEngine;

namespace Marco.Prototype
{
    [CreateAssetMenu(menuName = "MARCO/Character asset")]
    public sealed class CharacterAssetDefinition : ScriptableObject
    {
        [Serializable] public struct GenericBone { public HumanBodyBones bone; public string name; }
        public GameObject visualPrefab;
        public Vector3 modelOffset = new Vector3(0, -1, 0);
        public Vector3 modelScale = Vector3.one;
        public AnimationClip idleClip, walkClip, runClip, attackClip, interactionClip;
        [Min(0.01f)] public float attackDuration = 0.45f;
        [Min(0.01f)] public float attackSourceSpan = 0.8f;
        [Min(0.01f)] public float walkCycleDuration = 1.2f;
        [Tooltip("Eye offset from the original Head joint, in player-local space. No arm copy or pose correction.")]
        public Vector3 firstPersonEyeOffset = new Vector3(0, 0.1f, 0.08f);
        [Tooltip("Humanoid Avatars map automatically. Only Generic rigs need these once.")]
        public GenericBone[] genericBones = new GenericBone[0];

        public static CharacterAssetDefinition Find(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName) || assetName.Length > 80) return null;
            foreach (char c in assetName)
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-') return null;
            return Resources.Load<CharacterAssetDefinition>("CharacterAssets/" + assetName);
        }

        public Transform Resolve(Transform visual, Animator animator, HumanBodyBones bone)
        {
            if (animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid)
                return animator.GetBoneTransform(bone);
            string boneName = null;
            foreach (var binding in genericBones) if (binding.bone == bone) { boneName = binding.name; break; }
            if (string.IsNullOrEmpty(boneName) || visual == null) return null;
            foreach (var skin in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var joint in skin.bones) if (joint != null && joint.name == boneName) return joint;
            return null;
        }
    }
}
