using System;
using System.IO;
using System.Linq;
using Marco.Network.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    // Run in an isolated batch editor with graphics enabled; never changes a saved scene.
    public static class LobbyCctvRenderCheck
    {
        [MenuItem("MARCO/Lobby/Verify CCTV rendering %#F7")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode before checking the lobby scene."); return; }
            UnityEngine.SceneManagement.Scene preview = default;
            RenderTexture target = null;
            int exit = 0;
            Directory.CreateDirectory("Logs/LobbyCctv");
            try
            {
                preview = EditorSceneManager.OpenPreviewScene(LobbyCctvSceneBuilder.ScenePath);
                var stage = preview.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LobbyCctvStage>()).First();
                var camera = stage.feedCamera;
                camera.scene = preview;
                target = new RenderTexture(960, 540, 24);
                camera.targetTexture = target;
                // Sample the same idle clip used at runtime, avoiding a bind-pose preview.
                var animator = stage.actorRoot.GetComponentInChildren<Animator>();
                if (animator != null && stage.idleClip != null) stage.idleClip.SampleAnimation(animator.gameObject, 0);
                stage.SetSpeech(0);
                var quiet = Capture(camera, "quiet");
                stage.SetSpeech(1);
                var speaking = Capture(camera, "speaking");
                int changed = 0;
                for (int i = 0; i < quiet.Length; i++)
                    if (Math.Abs(quiet[i].r - speaking[i].r) + Math.Abs(quiet[i].g - speaking[i].g) + Math.Abs(quiet[i].b - speaking[i].b) > 24) changed++;
                if (changed < 100) throw new Exception("Speaking actor is not visible: " + changed + " changed pixels.");
                var shader = Resources.Load<Shader>("LobbyCctvSurface");
                var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").ToArray();
                if (errors.Length > 0) throw new Exception(string.Join("\n", errors.Select(m => m.message)));
                File.WriteAllText("Logs/LobbyCctv/result.txt", "PASS: pool feed rendered, actor visible, " + changed + " voice-reactive pixels, shader compiled.");
            }
            catch (Exception error)
            {
                exit = 1;
                File.WriteAllText("Logs/LobbyCctv/result.txt", error.ToString());
                Debug.LogException(error);
            }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            }
            if (Application.isBatchMode) EditorApplication.Exit(exit);
            else Debug.Log("[LobbyCCTV] " + File.ReadAllText("Logs/LobbyCctv/result.txt"));
        }

        private static Color32[] Capture(Camera camera, string name)
        {
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = camera.targetTexture;
            var image = new Texture2D(camera.targetTexture.width, camera.targetTexture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes("Logs/LobbyCctv/" + name + ".png", image.EncodeToPNG());
            var pixels = image.GetPixels32();
            UnityEngine.Object.DestroyImmediate(image);
            return pixels;
        }
    }
}

