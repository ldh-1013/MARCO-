using System.Collections.Generic;
using UnityEngine;

namespace Marco.Prototype
{
    // True first person: render the ORIGINAL body, with no added limbs or skeleton.
    [DefaultExecutionOrder(100)]
    public sealed class FirstPersonBodyView : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;
        private CharacterVisualPrototype visual;
        private CharacterMotionPresenter motion;
        private PrototypeTarget target;
        private Transform head, leftHand, rightHand;
        private Vector3 eyeOffset;
        private bool maskApplied;
        private readonly List<SkinVisibility> skins = new List<SkinVisibility>();
        private readonly List<RigidHeadVisibility> rigidHeadParts = new List<RigidHeadVisibility>();
        private sealed class RigidHeadVisibility { public MeshRenderer Renderer; public bool Enabled; }
        private sealed class SkinVisibility
        {
            public SkinnedMeshRenderer Renderer;
            public Mesh Original, OwnView;
        }
        public bool IsReady => head != null && leftHand != null && rightHand != null && skins.Count > 0;
        public bool IsVisible => IsReady && visual.BodyRoot.gameObject.activeInHierarchy && (target == null || !target.Health.IsEcho);
        public bool IsActing => motion != null && (motion.IsAttacking || motion.IsInteracting);
        public Transform RightHand => rightHand;
        public Transform LeftHand => leftHand;
        public Transform CharacterVisual => visual == null ? null : visual.BodyRoot;
        public int SourceRendererCount => skins.Count;
        public int HeadTrianglesRemoved { get; private set; }
        public bool IsMaskApplied => maskApplied;
        public int RigidHeadRendererCount => rigidHeadParts.Count;

        private void Start()
        {
            visual = GetComponent<CharacterVisualPrototype>();
            motion = GetComponent<CharacterMotionPresenter>(); target = GetComponent<PrototypeTarget>();
            if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
            if (visual == null || visual.Definition == null || visual.BodyRoot == null || playerCamera == null)
            { Fail("Assign a registered character and the owner's camera."); return; }
            head = visual.ResolveBone(HumanBodyBones.Head);
            leftHand = visual.ResolveBone(HumanBodyBones.LeftHand); rightHand = visual.ResolveBone(HumanBodyBones.RightHand);
            if (head == null || leftHand == null || rightHand == null)
            { Fail("Register the asset's head and hand bones."); return; }
            eyeOffset = visual.Definition.firstPersonEyeOffset;
            var neck = visual.ResolveBone(HumanBodyBones.Neck);
            foreach (var part in visual.BodyRoot.GetComponentsInChildren<MeshRenderer>(true))
                if (part.transform.IsChildOf(head) || (neck != null && part.transform.IsChildOf(neck)))
                    rigidHeadParts.Add(new RigidHeadVisibility { Renderer = part, Enabled = part.enabled });
            foreach (var source in visual.BodyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh original = source.sharedMesh;
                if (original == null) continue;
                if (!original.isReadable) { Fail("Enable mesh Read/Write on " + original.name); return; }
                Mesh ownView = MaskHead(source, neck);
                skins.Add(new SkinVisibility { Renderer = source, Original = original, OwnView = ownView });
                source.updateWhenOffscreen = true;
            }
            if (!IsReady) { Fail("The original asset has no skinned body."); return; }
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
            AlignEyes();
        }

        private Mesh MaskHead(SkinnedMeshRenderer source, Transform neck)
        {
            Mesh original = source.sharedMesh; Transform[] bones = source.bones;
            var hiddenBones = new bool[bones.Length];
            var armBones = new bool[bones.Length];
            var leftUpper = visual.ResolveBone(HumanBodyBones.LeftUpperArm);
            var rightUpper = visual.ResolveBone(HumanBodyBones.RightUpperArm);
            for (int i = 0; i < bones.Length; i++)
            {
                hiddenBones[i] = bones[i] != null && (bones[i].IsChildOf(head) || (neck != null && bones[i].IsChildOf(neck)));
                armBones[i] = bones[i] != null && ((leftUpper != null && bones[i].IsChildOf(leftUpper))
                    || (rightUpper != null && bones[i].IsChildOf(rightUpper)) || bones[i].IsChildOf(leftHand) || bones[i].IsChildOf(rightHand));
            }
            var hiddenVertices = new bool[original.vertexCount];
            var weights = original.boneWeights;
            // Weighted bodies must be classified by their skeleton, never by a
            // height cut through an arbitrary animation pose. Imported renderer
            // scales can also differ from baked vertex space (Demon uses ~50x).
            // A spatial cut there erased the torso and left torn shoulder strips.
            for (int i = 0; i < original.vertexCount; i++)
            {
                var w = i < weights.Length ? weights[i] : default;
                float headWeight = Weight(hiddenBones, w.boneIndex0, w.weight0) + Weight(hiddenBones, w.boneIndex1, w.weight1)
                    + Weight(hiddenBones, w.boneIndex2, w.weight2) + Weight(hiddenBones, w.boneIndex3, w.weight3);
                float armWeight = Weight(armBones, w.boneIndex0, w.weight0) + Weight(armBones, w.boneIndex1, w.weight1)
                    + Weight(armBones, w.boneIndex2, w.weight2) + Weight(armBones, w.boneIndex3, w.weight3);
                hiddenVertices[i] = headWeight >= .5f && armWeight < .2f;
            }
            // Separate unweighted facial blend-shape parts have no head weights.
            // Use their world-space renderer bounds, not scaled BakeMesh vertices.
            // This fallback never trims a weighted body or its raised arms/legs.
            Vector3 neckPosition = neck == null ? head.position : neck.position;
            if (weights.Length == 0 && Vector3.Dot(source.bounds.center - neckPosition, transform.up) > -.025f)
                for (int i = 0; i < hiddenVertices.Length; i++) hiddenVertices[i] = true;
            float Weight(bool[] group, int index, float amount) => index >= 0 && index < group.Length && group[index] ? amount : 0;
            var groups = new List<int>[original.subMeshCount]; int removed = 0;
            for (int sub = 0; sub < groups.Length; sub++)
            {
                groups[sub] = new List<int>(); var triangles = original.GetTriangles(sub);
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    if (hiddenVertices[triangles[i]] || hiddenVertices[triangles[i + 1]] || hiddenVertices[triangles[i + 2]])
                    { removed++; continue; }
                    groups[sub].Add(triangles[i]); groups[sub].Add(triangles[i + 1]); groups[sub].Add(triangles[i + 2]);
                }
            }
            HeadTrianglesRemoved += removed;
            if (removed == 0) return original;
            // Visibility-only index buffer on the SAME renderer, not another body part.
            // Imported meshes/materials stay untouched; other cameras see the full mesh.
            var masked = Instantiate(original); masked.name = original.name + " (own-camera head visibility)";
            for (int sub = 0; sub < groups.Length; sub++) masked.SetTriangles(groups[sub], sub, false);
            return masked;
        }
        private void LateUpdate() { if (IsReady) AlignEyes(); }
        private void AlignEyes()
        {
            // Follow the actual animated head POSITION, not its rotation (no double pitch).
            playerCamera.transform.position = head.position + transform.TransformVector(eyeOffset);
        }
        private void BeforeCamera(Camera camera)
        {
            if (camera != playerCamera || !isActiveAndEnabled || !IsVisible || !visual.IsFirstPerson || maskApplied) return;
            maskApplied = true;
            foreach (var skin in skins) if (skin.Renderer != null) skin.Renderer.sharedMesh = skin.OwnView;
            // Rigid head/back-face pieces have no skin weights; keep them off only here.
            foreach (var part in rigidHeadParts)
                if (part.Renderer != null) { part.Enabled = part.Renderer.enabled; part.Renderer.enabled = false; }
        }
        private void AfterCamera(Camera camera) { if (camera == playerCamera) RestoreMeshes(); }
        private void RestoreMeshes()
        {
            if (!maskApplied) return;
            foreach (var skin in skins) if (skin.Renderer != null) skin.Renderer.sharedMesh = skin.Original;
            foreach (var part in rigidHeadParts) if (part.Renderer != null) part.Renderer.enabled = part.Enabled;
            maskApplied = false;
        }
        private void Fail(string message) { Debug.LogError("Original-body first person: " + message, this); enabled = false; }
        private void OnDisable() { RestoreMeshes(); }
        private void OnDestroy()
        {
            Camera.onPreCull -= BeforeCamera; Camera.onPostRender -= AfterCamera;
            RestoreMeshes();
            foreach (var skin in skins) if (skin.OwnView != skin.Original) Destroy(skin.OwnView);
        }
    }
}
