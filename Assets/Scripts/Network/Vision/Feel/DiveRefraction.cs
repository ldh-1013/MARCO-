using System;
using UnityEngine;

namespace Marco.Network.Vision
{
    // V30 연출.md 2.2 잠수 — 가장자리 물 굴절, owned by CameraFeelStack. While the local player dives it asks URP for the
    // opaque texture on the player camera only (UniversalAdditionalCameraData.requiresColorOption = On, set by reflection —
    // Marco.Network has no URP reference; the pipeline asset is not changed) and shows a quad that fills the view at the
    // near plane with Marco/WaterEdgeRefraction. Off: the quad is hidden and the camera's option is put back as it was.
    internal sealed class DiveRefraction
    {
        private Camera camera;
        private GameObject quad;
        private Material material;
        private Component cameraData;
        private System.Reflection.PropertyInfo colorOption;
        private object savedOption;
        private bool active, failed;

        public void Update(Camera view, bool diving)
        {
            if (failed) return;
            try
            {
                if (view != camera) { Release(); camera = view; }
                if (camera == null) return;
                if (diving != active) { active = diving; Apply(diving); }
                if (active && quad != null) Fit();
            }
            catch (Exception exception) { failed = true; Release(); Debug.LogException(exception); }
        }

        private void Apply(bool on)
        {
            if (cameraData == null)
            {
                cameraData = camera.GetComponent("UniversalAdditionalCameraData");
                colorOption = cameraData?.GetType().GetProperty("requiresColorOption");
            }
            if (colorOption != null)
            {
                if (on) { savedOption = colorOption.GetValue(cameraData); colorOption.SetValue(cameraData, Enum.Parse(colorOption.PropertyType, "On")); }
                else if (savedOption != null) colorOption.SetValue(cameraData, savedOption);
            }
            if (on && quad == null)
            {
                var shader = Resources.Load<Shader>("MarcoPresentation/WaterEdgeRefraction");
                if (shader == null || colorOption == null) { failed = true; return; }
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Dive edge refraction";
                quad.hideFlags = HideFlags.DontSave;
                UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
                quad.layer = camera.gameObject.layer;
                quad.transform.SetParent(camera.transform, false);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            if (quad != null) quad.SetActive(on);
        }

        // Just past the near plane, exactly covering the view.
        private void Fit()
        {
            float distance = camera.nearClipPlane * 1.05f;
            float height = 2f * distance * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            quad.transform.localPosition = new Vector3(0f, 0f, distance);
            quad.transform.localRotation = Quaternion.identity;
            quad.transform.localScale = new Vector3(height * camera.aspect * 1.02f, height * 1.02f, 1f);
            material.SetFloat("_Aspect", camera.aspect);
        }

        public void Release()
        {
            if (active && colorOption != null && savedOption != null && cameraData != null) colorOption.SetValue(cameraData, savedOption);
            active = false;
            if (quad != null) UnityEngine.Object.Destroy(quad);
            if (material != null) UnityEngine.Object.Destroy(material);
            quad = null; material = null; cameraData = null; colorOption = null; savedOption = null; camera = null;
        }
    }
}
