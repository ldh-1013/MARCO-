using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Batch entry point: -executeMethod PoolWaterRenderCheck.Run (with graphics enabled).
public static class PoolWaterRenderCheck
{
    [MenuItem("Tools/MARCO/Validate Pool Water Rendering")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogWarning("Stop Play mode before running the isolated pool render check."); return; }
        Scene preview = default;
        RenderTexture target = null;
        Material testMaterial = null;
        float minusEnabled = Shader.GetGlobalFloat("_MarcoMinusEnabled");
        int exitCode = 0;
        try
        {
            preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Maps/IndoorPool_PrototypeGame.unity");
            Shader.SetGlobalFloat("_MarcoMinusEnabled", 0);
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Maps/PoolV12/Materials/Water.mat");
            foreach (string name in new[] { "_NormalMap", "_DetailNormalMap", "_FoamMap", "_ReflectionCube" })
                if (material.GetTexture(name) == null) throw new Exception("Missing SUIMONO texture: " + name);
            var surfaces = preview.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MeshRenderer>())
                .Where(r => r.sharedMaterial == material).ToArray();
            if (surfaces.Length != 2) throw new Exception("Expected both pool surfaces, found " + surfaces.Length);
            // Freeze only the preview's base animation so image deltas isolate the interaction.
            testMaterial = new Material(material);
            testMaterial.SetFloat("_FlowSpeed", 0);
            testMaterial.SetFloat("_WaveHeight", 0);
            foreach (var surface in surfaces) surface.sharedMaterial = testMaterial;
            Shader.SetGlobalInteger("_MarcoWaterRippleCount", 0);
            var camera = new GameObject("Water validation camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, preview);
            camera.scene = preview;
            camera.transform.position = new Vector3(13, 2.0f, 47.5f);
            camera.transform.LookAt(new Vector3(30, -0.4f, 47.5f));
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 150;
            camera.fieldOfView = 65;
            var data = camera.GetUniversalAdditionalCameraData();
            target = new RenderTexture(1280, 720, 24);
            camera.targetTexture = target;
            Directory.CreateDirectory("Logs/PoolWater");
            for (int renderer = 0; renderer < 2; renderer++)
            {
                data.SetRenderer(renderer);
                foreach (var surface in surfaces) surface.enabled = false;
                var dry = Capture(camera, target);
                foreach (var surface in surfaces) surface.enabled = true;
                var wet = Capture(camera, target);
                File.WriteAllBytes($"Logs/PoolWater/renderer-{renderer}.png", wet.EncodeToPNG());
                var a = dry.GetPixels32(); var b = wet.GetPixels32();
                int changed = 0;
                for (int i = 0; i < a.Length; i++)
                    if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 20) changed++;
                if (changed < a.Length / 100) throw new Exception("Water not visibly rendered, renderer " + renderer);
                Debug.Log($"[PoolWater] Renderer {renderer}: {changed} pixels changed by water.");
                var pool = new Bounds(new Vector3(27.5f, 0, 47.5f), new Vector3(25, 3.5f, 21));
                Marco.Network.Audio.PoolWaterEffects.Emit(new Vector3(18, .025f, 47.5f), pool, 1.2f, true, preview);
                Shader.SetGlobalFloat("_MarcoWaterRippleClock", Time.time + .35f);
                var reaction = Capture(camera, target);
                var c = reaction.GetPixels32();
                int rippled = 0;
                for (int i = 0; i < b.Length; i++)
                    if (Math.Abs(b[i].r - c[i].r) + Math.Abs(b[i].g - c[i].g) + Math.Abs(b[i].b - c[i].b) > 8) rippled++;
                if (rippled < 100) throw new Exception("Interaction ripple was not visible, renderer " + renderer);
                File.WriteAllBytes($"Logs/PoolWater/reaction-{renderer}.png", reaction.EncodeToPNG());
                Debug.Log($"[PoolWater] Renderer {renderer}: {rippled} pixels changed by entry reaction.");
                var effects = preview.GetRootGameObjects().First(go => go.GetComponent<Marco.Network.Audio.PoolWaterEffects>() != null);
                for (int i = 0; i < 40; i++)
                    Marco.Network.Audio.PoolWaterEffects.Emit(new Vector3(18, .025f, 47.5f), pool, 1, true, preview);
                if (Shader.GetGlobalInteger("_MarcoWaterRippleCount") != 16 || effects.GetComponentInChildren<ParticleSystem>().particleCount > 256)
                    throw new Exception("Water effect budget exceeded.");
                UnityEngine.Object.DestroyImmediate(effects);
                if (Shader.GetGlobalInteger("_MarcoWaterRippleCount") != 0) throw new Exception("Water globals were not cleared.");
                UnityEngine.Object.DestroyImmediate(reaction);
                UnityEngine.Object.DestroyImmediate(dry);
                UnityEngine.Object.DestroyImmediate(wet);
            }
            var errors = ShaderUtil.GetShaderMessages(material.shader).Where(m => m.severity.ToString() == "Error").ToArray();
            if (errors.Length > 0) throw new Exception(string.Join("\n", errors.Select(m => m.message)));
            Debug.Log("[PoolWater] PASS: SUIMONO textures resolved, both pools present, 2D/3D water visible, shader compiled.");
            File.WriteAllText("Logs/PoolWater/result.txt", "PASS: SUIMONO textures, both pools, 2D/3D water and entry ripple rendering, effect limits, cleanup and shader compilation.");
        }
        catch (Exception e)
        {
            exitCode = 1;
            Directory.CreateDirectory("Logs/PoolWater");
            File.WriteAllText("Logs/PoolWater/result.txt", e.ToString());
            Debug.LogException(e);
        }
        finally
        {
            Shader.SetGlobalFloat("_MarcoMinusEnabled", minusEnabled);
            if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (testMaterial != null) UnityEngine.Object.DestroyImmediate(testMaterial);
        }
        if (Application.isBatchMode) EditorApplication.Exit(exitCode);
    }

    static Texture2D Capture(Camera camera, RenderTexture target)
    {
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        RenderTexture.active = previous;
        return image;
    }
}

