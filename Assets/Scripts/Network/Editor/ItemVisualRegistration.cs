using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Marco.Network.Editor
{
    // Presentation only: bake model proportions into Resources prefabs for player builds.
    public static class ItemVisualRegistration
    {
        private const string Folder = "Assets/Resources/ItemVisuals";

        [MenuItem("MARCO/Items/Register duck and recorder models")]
        public static void Register()
        {
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            // The supplied colours/textures were assigned on the map instances, not on the FBX.
            // Read them in an isolated preview scene so the user's open scene is untouched.
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Maps/IndoorPool_v1_2.unity");
            try
            {
                var instances = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .Select(t => t.gameObject).ToArray();
                Build("Duck", Find("Assets/Object/오리.fbx"), .27f, true);
                Build("Recorder", Find("Assets/Object/녹음기.fbx"), .24f, false);
                GameObject Find(string path) => instances.FirstOrDefault(instance =>
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance) == path &&
                    PrefabUtility.IsAnyPrefabInstanceRoot(instance)) ?? throw new InvalidOperationException("Missing map instance: " + path);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            AssetDatabase.SaveAssets();
            Verify();
            Debug.Log("[ItemVisualRegistration] PASS: duck and recorder registered for runtime loading.");
        }

        [MenuItem("MARCO/Items/Verify duck and recorder models")]
        public static void Verify()
        {
            foreach (string name in new[] { "Duck", "Recorder" })
            {
                var prefab = Resources.Load<GameObject>("ItemVisuals/" + name);
                if (prefab == null || prefab.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException("Missing visual or unexpected physics collider: " + name);
                var preview = new PreviewRenderUtility();
                try
                {
                    var instance = UnityEngine.Object.Instantiate(prefab);
                    preview.AddSingleGO(instance);
                    var renderers = instance.GetComponentsInChildren<Renderer>(true);
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers)
                    {
                        bounds.Encapsulate(renderer.bounds);
                        foreach (var material in renderer.sharedMaterials)
                            if (material == null || !material.shader.isSupported)
                                throw new InvalidOperationException("Invalid material: " + name);
                    }
                    float expectedHeight = name == "Duck" ? .27f : .24f;
                    if (Mathf.Abs(bounds.size.y - expectedHeight) > .001f)
                        throw new InvalidOperationException("Incorrect visual height: " + name);
                    preview.camera.transform.position = bounds.center + new Vector3(.45f, .24f, -.65f);
                    preview.camera.transform.LookAt(bounds.center);
                    preview.camera.nearClipPlane = .01f;
                    preview.camera.farClipPlane = 5f;
                    preview.camera.orthographic = true;
                    preview.camera.orthographicSize = .21f;
                    preview.camera.backgroundColor = new Color(.12f, .14f, .18f);
                    preview.camera.clearFlags = CameraClearFlags.SolidColor;
                    preview.BeginStaticPreview(new Rect(0, 0, 512, 512));
                    preview.Render(true);
                    var image = preview.EndStaticPreview();
                    Directory.CreateDirectory("Logs/ItemVisuals");
                    File.WriteAllBytes("Logs/ItemVisuals/" + name + ".png", image.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(image);
                    Debug.Log($"[ItemVisualVerification] PASS {name}: height={bounds.size.y:F3}m, renderers={renderers.Length}, no colliders, runtime resource resolved.");
                }
                finally { preview.Cleanup(); }
            }
        }

        private static void Build(string name, GameObject source, float height, bool bottomPivot)
        {
            string path = source.name;
            var root = new GameObject(name);
            try
            {
                var model = UnityEngine.Object.Instantiate(source);
                model.transform.SetParent(root.transform, false);
                model.transform.localPosition = Vector3.zero;
                // Pickup yaw in DecoyConfig expects the duck's beak along local -Z.
                model.transform.localRotation = name == "Duck" ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
                model.transform.localScale = Vector3.one;
                foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.DestroyImmediate(collider);
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("No mesh: " + path);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                if (bounds.size.y < .0001f) throw new InvalidOperationException("Invalid model bounds: " + path);
                float scale = height / bounds.size.y;
                model.transform.localScale *= scale;
                model.transform.localPosition = -bounds.center * scale +
                    Vector3.up * (bottomPivot ? height * .5f + .01f : height * .5f - .09f);
                foreach (var renderer in renderers)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    // Preserve each supplied material's texture and tint. Unlit pickups remain readable
                    // in the dark, like the previous duck, without lighting nearby terrain.
                    renderer.sharedMaterials = renderer.sharedMaterials.Select((material, index) =>
                    {
                        if (material == null) throw new InvalidOperationException("Missing material: " + path);
                        string materialPath = Folder + "/" + name + "_" + material.name.Replace('/', '_') + ".mat";
                        var baked = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        if (baked == null)
                        {
                            baked = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                            AssetDatabase.CreateAsset(baked, materialPath);
                        }
                        baked.SetTexture("_BaseMap", material.HasProperty("_BaseMap")
                            ? material.GetTexture("_BaseMap") : material.mainTexture);
                        baked.SetColor("_BaseColor", material.HasProperty("_BaseColor")
                            ? material.GetColor("_BaseColor") : material.color);
                        EditorUtility.SetDirty(baked);
                        Debug.Log($"[ItemVisualRegistration] {name}: {renderer.name}, material={material.name}, texture={baked.GetTexture("_BaseMap")}");
                        return baked;
                    }).ToArray();
                }
                PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + name + ".prefab");
                Debug.Log($"[ItemVisualRegistration] {name}: source bounds={bounds}, scale={scale}");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
