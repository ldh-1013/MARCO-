using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Character
{
    public sealed partial class CharacterMotion
    {
        private float placementWeight, placementProgress;
        private Vector3 placementPoint;
        private GameObject placementProp;
        private Material placementPropMaterial;
        public float PlacementPoseWeight => placementWeight;

        private static readonly HumanBodyBones[] PlacementFingers =
        {
            HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal
        };

        // Additive procedural placement on the registered model. No imported clip or prefab edit.
        // The owner sees these same hands through FirstPersonBody; remote players see the kneeling body.
        private void ApplySoundCanPlacementPose()
        {
            bool active = player != null && player.IsPlacingSoundCan;
            placementWeight = Mathf.MoveTowards(placementWeight, active ? 1 : 0, Time.deltaTime * 6f);
            if (active)
            {
                placementPoint = player.SoundCanPlacementPoint;
                placementProgress = Mathf.MoveTowards(placementProgress, player.SoundCanPlacementProgress, Time.deltaTime * 2f);
            }
            else placementProgress = 1f;
            if (placementWeight < .001f)
            {
                if (placementProp != null) placementProp.SetActive(false);
                placementProgress = 0;
                return;
            }

            var hips = body.ResolveBone(HumanBodyBones.Hips);
            if (hips != null) { Remember(hips); hips.position += Vector3.down * (.13f * placementWeight); }
            Tilt(body.ResolveBone(HumanBodyBones.Spine), 12f * placementWeight);
            Tilt(body.ResolveBone(HumanBodyBones.Chest), 14f * placementWeight);
            // Lower the can, make several small adjustments, then release and draw the hands back.
            float lower = Mathf.SmoothStep(0, 1, Mathf.Clamp01(placementProgress / .32f));
            float release = Mathf.SmoothStep(0, 1, Mathf.Clamp01((placementProgress - .82f) / .18f));
            Vector3 carry = transform.position + transform.forward * .4f - Vector3.up * .2f;
            Vector3 work = placementPoint + Vector3.up * .17f;
            Vector3 centre = Vector3.Lerp(carry, work, lower) + Vector3.up * (release * .18f);
            float fidget = Mathf.Sin(placementProgress * Mathf.PI * 12f) * .018f * lower * (1 - release);
            PosePlacementArm(true, centre - transform.right * .1f + transform.forward * fidget);
            PosePlacementArm(false, centre + transform.right * .1f - transform.forward * fidget);

            for (int i = 0; i < PlacementFingers.Length; i++)
            {
                var finger = body.ResolveBone(PlacementFingers[i]);
                if (finger == null) continue;
                Remember(finger);
                float curl = (18f + 9f * Mathf.Sin(placementProgress * Mathf.PI * 18f + i * .7f)) * placementWeight * (1 - release);
                finger.rotation = Quaternion.AngleAxis(curl, transform.right) * finger.rotation;
            }
            UpdatePlacementProp(active && placementProgress < .9f);
        }

        private void PosePlacementArm(bool left, Vector3 target)
        {
            var upper = body.ResolveBone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            var lower = body.ResolveBone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            var hand = body.ResolveBone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (upper == null || lower == null || hand == null) return;
            float a = Vector3.Distance(upper.position, lower.position), b = Vector3.Distance(lower.position, hand.position);
            Vector3 offset = target - upper.position;
            if (a < .001f || b < .001f || offset.sqrMagnitude < .00001f) return;
            float length = Mathf.Clamp(offset.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            Vector3 direction = offset.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(transform.right * (left ? -1 : 1) + Vector3.up * .2f, direction).normalized;
            if (bend.sqrMagnitude < .001f) bend = transform.forward;
            float cosine = Mathf.Clamp((a * a + length * length - b * b) / (2 * a * length), -1, 1);
            Vector3 upperDirection = direction * cosine + bend * Mathf.Sqrt(1 - cosine * cosine);
            Remember(upper); Remember(lower); Remember(hand);
            bool detached = !hand.IsChildOf(lower);
            Vector3 endLocal = lower.InverseTransformPoint(hand.position);
            Quaternion endRotation = Quaternion.Inverse(lower.rotation) * hand.rotation;
            upper.rotation = Quaternion.Slerp(Quaternion.identity,
                Quaternion.FromToRotation(lower.position - upper.position, upperDirection), placementWeight) * upper.rotation;
            if (detached) { hand.position = lower.TransformPoint(endLocal); hand.rotation = lower.rotation * endRotation; }
            Vector3 lowerDirection = upper.position + direction * length - lower.position;
            if (lowerDirection.sqrMagnitude > .00001f)
                lower.rotation = Quaternion.Slerp(Quaternion.identity,
                    Quaternion.FromToRotation(hand.position - lower.position, lowerDirection), placementWeight) * lower.rotation;
            if (detached) { hand.position = lower.TransformPoint(endLocal); hand.rotation = lower.rotation * endRotation; }
            hand.rotation = Quaternion.Slerp(hand.rotation,
                Quaternion.LookRotation(transform.forward, Vector3.up) * Quaternion.Euler(15, 0, left ? 12 : -12), placementWeight);
        }

        private void UpdatePlacementProp(bool visible)
        {
            if (visible && placementProp == null)
            {
                var shader = Shader.Find("PoolV12/BlockoutSurface") ?? Shader.Find("MARCO/WorldSpaceTexturedSurface");
                if (shader == null) return;
                placementPropMaterial = new Material(shader);
                if (placementPropMaterial.HasProperty("_BaseColor")) placementPropMaterial.SetColor("_BaseColor", new Color(1f, .72f, .3f));
                placementProp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                placementProp.name = "소리통 설치 중 손에 든 장치";
                placementProp.GetComponent<Collider>().enabled = false;
                placementProp.transform.SetParent(transform, false);
                placementProp.transform.localScale = new Vector3(.16f, .18f, .09f);
                var renderer = placementProp.GetComponent<Renderer>();
                renderer.sharedMaterial = placementPropMaterial; renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (placementProp == null) return;
            placementProp.SetActive(visible);
            if (!visible) return;
            var left = body.ResolveBone(HumanBodyBones.LeftHand);
            var right = body.ResolveBone(HumanBodyBones.RightHand);
            if (left != null && right != null)
                placementProp.transform.position = (left.position + right.position) * .5f;
            placementProp.transform.rotation = transform.rotation;
        }

        private void ClearPlacementProp()
        {
            if (placementProp != null) Destroy(placementProp);
            if (placementPropMaterial != null) Destroy(placementPropMaterial);
            placementProp = null; placementPropMaterial = null;
        }
    }
}
