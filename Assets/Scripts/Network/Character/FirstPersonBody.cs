using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Character
{
    // Owner only: the owner's camera sees its own model with the head cut out, and the camera
    // rides the animated head. Other cameras see the full mesh.
    // Ported from Prototype FirstPersonBodyView; NetworkPlayer enables it for the owner.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CharacterBody))]
    public sealed class FirstPersonBody : MonoBehaviour
    {
        private sealed class SkinVisibility { public SkinnedMeshRenderer Renderer; public Mesh Original, OwnView; }
        private sealed class RigidHeadVisibility { public MeshRenderer Renderer; public bool Enabled; }

        private CharacterBody body;
        private Camera playerCamera;
        private Transform head, leftHand, rightHand;
        private Vector3 eyeOffset, cameraHome;
        private bool maskApplied;
        private readonly List<SkinVisibility> skins = new();
        private readonly List<RigidHeadVisibility> rigidHeadParts = new();

        private bool IsReady => head != null && skins.Count > 0;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            playerCamera = GetComponentInChildren<Camera>(true);
            cameraHome = playerCamera.transform.localPosition;
            body.Rebuilt += Rebind;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeforeSrpCamera;
            RenderPipelineManager.endCameraRendering += AfterSrpCamera;
            Camera.onPreCull += BeforeCamera;
            Camera.onPostRender += AfterCamera;
            Rebind();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeSrpCamera;
            RenderPipelineManager.endCameraRendering -= AfterSrpCamera;
            Camera.onPreCull -= BeforeCamera;
            Camera.onPostRender -= AfterCamera;
            Unbind();
        }

        private void Rebind()
        {
            Unbind();
            if (!isActiveAndEnabled || body.BodyRoot == null) return;
            head = body.ResolveBone(HumanBodyBones.Head);
            leftHand = body.ResolveBone(HumanBodyBones.LeftHand);
            rightHand = body.ResolveBone(HumanBodyBones.RightHand);
            if (head == null || leftHand == null || rightHand == null)
            { Debug.LogError("First-person body: register the asset's head and hand bones.", this); head = null; return; }
            eyeOffset = body.Definition.firstPersonEyeOffset;
            var neck = body.ResolveBone(HumanBodyBones.Neck);
            foreach (var part in body.BodyRoot.GetComponentsInChildren<MeshRenderer>(true))
                if (part.transform.IsChildOf(head) || (neck != null && part.transform.IsChildOf(neck)))
                    rigidHeadParts.Add(new RigidHeadVisibility { Renderer = part, Enabled = part.enabled });
            foreach (var source in body.BodyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh original = source.sharedMesh;
                if (original == null) continue;
                if (!original.isReadable) { Debug.LogError("First-person body: enable mesh Read/Write on " + original.name, this); Unbind(); return; }
                skins.Add(new SkinVisibility { Renderer = source, Original = original, OwnView = MaskHead(source, neck) });
                source.updateWhenOffscreen = true;
            }
        }

        private void Unbind()
        {
            RestoreMeshes();
            foreach (var skin in skins) if (skin.OwnView != skin.Original) Destroy(skin.OwnView);
            skins.Clear();
            rigidHeadParts.Clear();
            head = leftHand = rightHand = null;
            if (playerCamera != null) playerCamera.transform.localPosition = cameraHome;
        }

        // Hide triangles weighted to the head/neck (but not raised arms) in a copy of the mesh.
        private Mesh MaskHead(SkinnedMeshRenderer source, Transform neck)
        {
            Mesh original = source.sharedMesh; Transform[] bones = source.bones;
            var hiddenBones = new bool[bones.Length];
            var armBones = new bool[bones.Length];
            var leftUpper = body.ResolveBone(HumanBodyBones.LeftUpperArm);
            var rightUpper = body.ResolveBone(HumanBodyBones.RightUpperArm);
            for (int i = 0; i < bones.Length; i++)
            {
                hiddenBones[i] = bones[i] != null && (bones[i].IsChildOf(head) || (neck != null && bones[i].IsChildOf(neck)));
                armBones[i] = bones[i] != null && ((leftUpper != null && bones[i].IsChildOf(leftUpper))
                    || (rightUpper != null && bones[i].IsChildOf(rightUpper)) || bones[i].IsChildOf(leftHand) || bones[i].IsChildOf(rightHand));
            }
            var hiddenVertices = new bool[original.vertexCount];
            var weights = original.boneWeights;
            // Classify by skeleton weights, never by a height cut through an arbitrary pose.
            for (int i = 0; i < original.vertexCount; i++)
            {
                var w = i < weights.Length ? weights[i] : default;
                float headWeight = Weight(hiddenBones, w.boneIndex0, w.weight0) + Weight(hiddenBones, w.boneIndex1, w.weight1)
                    + Weight(hiddenBones, w.boneIndex2, w.weight2) + Weight(hiddenBones, w.boneIndex3, w.weight3);
                float armWeight = Weight(armBones, w.boneIndex0, w.weight0) + Weight(armBones, w.boneIndex1, w.weight1)
                    + Weight(armBones, w.boneIndex2, w.weight2) + Weight(armBones, w.boneIndex3, w.weight3);
                hiddenVertices[i] = headWeight >= .5f && armWeight < .2f;
            }
            // Unweighted facial blend-shape parts above the neck are hidden whole.
            Vector3 neckPosition = neck == null ? head.position : neck.position;
            if (weights.Length == 0 && Vector3.Dot(source.bounds.center - neckPosition, transform.up) > -.025f)
                for (int i = 0; i < hiddenVertices.Length; i++) hiddenVertices[i] = true;
            static float Weight(bool[] group, int index, float amount) => index >= 0 && index < group.Length && group[index] ? amount : 0;

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
            if (removed == 0) return original;
            var masked = Instantiate(original); masked.name = original.name + " (own-camera head visibility)";
            for (int sub = 0; sub < groups.Length; sub++) masked.SetTriangles(groups[sub], sub, false);
            return masked;
        }

        // Follow the animated head POSITION only; the pivot already applies pitch.
        private void LateUpdate()
        {
            if (!IsReady) return;
            playerCamera.transform.position = body.BodyRoot.gameObject.activeInHierarchy
                ? head.position + transform.TransformVector(eyeOffset)
                : playerCamera.transform.parent.TransformPoint(cameraHome);
        }

        private void BeforeSrpCamera(ScriptableRenderContext context, Camera camera) => BeforeCamera(camera);
        private void AfterSrpCamera(ScriptableRenderContext context, Camera camera) => AfterCamera(camera);

        private void BeforeCamera(Camera camera)
        {
            if (camera != playerCamera || !IsReady || maskApplied) return;
            maskApplied = true;
            foreach (var skin in skins) if (skin.Renderer != null) skin.Renderer.sharedMesh = skin.OwnView;
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

        private void OnDestroy() => body.Rebuilt -= Rebind;
    }
}
