using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Explicit batch entry point for validating this standalone preview after an Editor upgrade.
public static class PoolPreviewUpgradeValidation
{
    const string Output = "Verification/Unity6000.5.4f1";

    public static void Run()
    {
        if (Application.unityVersion != "6000.5.4f1")
            throw new InvalidOperationException("Run this check with Unity 6000.5.4f1.");
        Directory.CreateDirectory(Output);
        PoolV11Builder.ValidateSavedMap();
        File.Copy("PoolV11Reports/ReloadValidation.txt", Output + "/V11Geometry.txt", true);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        PoolV12Builder.ValidateSavedMap();
        File.Copy("PoolV12Reports/ReloadValidation.txt", Output + "/V12Geometry.txt", true);
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        var view = UnityEngine.Object.FindFirstObjectByType<PoolV12Viewer>();
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        foreach (var material in renderer.sharedMaterials.Where(m => m != null))
            if (material.shader == null || !material.shader.isSupported || material.shader.name == "Hidden/InternalErrorShader")
                throw new InvalidOperationException("Unsupported shader on " + renderer.name);
        Capture(view, 0, "Overview");
        Capture(view, 1, "FirstFloor");
        Capture(view, 2, "SecondFloor");
        view.visibleFloor = 0; view.ApplyView();
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        var paths = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") &&
            new[] { ".unity", ".prefab", ".mat", ".asset" }.Contains(Path.GetExtension(p))).ToArray();
        AssetDatabase.ForceReserializeAssets(paths);
        AssetDatabase.SaveAssets();
        File.WriteAllText(Output + "/EditorValidation.txt",
            "PASS: Unity " + Application.unityVersion + "\nPASS: both saved map versions passed geometry and collision validation.\n" +
            "PASS: renderer materials have supported shaders.\nPASS: rendered ALL/1F/2F without blank or error-pink frames.\n" +
            "PASS: scenes, prefabs and materials reserialized by target Editor.\n" +
            "Test pipe FBX is absent; its optional replacement sample test is skipped. No gameplay or networking validation.\n");
        Debug.Log("POOL_EDITOR_UPGRADE_PASS");
    }

    static void Capture(PoolV12Viewer view, int floor, string name)
    {
        view.visibleFloor = floor; view.ApplyView();
        var target = new RenderTexture(1800, 1400, 24);
        var previous = RenderTexture.active;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderPipeline.SubmitRenderRequest(view.viewCamera,
                new UniversalRenderPipeline.SingleCameraRequest { destination = target });
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
            var pixels = texture.GetPixels32();
            int pink = pixels.Count(c => c.r > 200 && c.b > 200 && c.g < 60);
            int varied = pixels.Where((c, i) => i % 100 == 0).Select(c => (c.r / 16, c.g / 16, c.b / 16)).Distinct().Count();
            if (pink > pixels.Length / 100 || varied < 8)
                throw new InvalidOperationException("Blank or shader-error render: " + name);
            File.WriteAllBytes(Output + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
