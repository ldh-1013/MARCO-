using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Marco.Network.Character;
using Marco.Network.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Marco.Network.Editor
{
    public sealed class LobbyCctvSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Lobby/Lobby_CCTV.unity";
        private const string MaterialFolder = "Assets/Scenes/Lobby/Materials";
        private const int StageLayer = 31;
        private GameObject stage;
        private Camera cameraFeed;
        private Shader surface;
        private int materialIndex;
        private readonly Dictionary<Material, Material> mapMaterials = new();

        [MenuItem("MARCO/Lobby/Create CCTV scene %#F6")]
        public static void Create()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode before creating the lobby scene."); return; }
            if (File.Exists(ScenePath)) { Debug.Log("Lobby CCTV scene already exists; keeping your edits."); return; }
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
            Scene source = default, destination = default;
            Scene previous = SceneManager.GetActiveScene();
            try
            {
                source = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Maps/IndoorPool_v1_2.unity");
                destination = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var builder = new LobbyCctvSceneBuilder { surface = Resources.Load<Shader>("LobbyCctvSurface") };
                if (builder.surface == null) throw new Exception("CCTV shader missing.");
                builder.Build(source, destination);
                if (!EditorSceneManager.SaveScene(destination, ScenePath)) throw new Exception("Cannot save CCTV scene.");
                var scenes = EditorBuildSettings.scenes.ToList();
                if (!scenes.Any(s => s.path == ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
                AssetDatabase.SaveAssets();
                Debug.Log("[LobbyCCTV] Created editable scene: " + ScenePath);
            }
            finally
            {
                if (source.IsValid()) EditorSceneManager.ClosePreviewScene(source);
                if (destination.IsValid()) EditorSceneManager.CloseScene(destination, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private void Build(Scene map, Scene destination)
        {
            if (surface == null) return;
            if (!map.isLoaded) return;
            stage = new GameObject("Lobby CCTV display stage");
            SceneManager.MoveGameObjectToScene(stage, destination);
            // Copy only visible meshes; no map behaviours, physics, lights or network objects.
            var hall = new Bounds(new Vector3(24, 2, 46), new Vector3(52, 20, 34));
            foreach (GameObject root in map.GetRootGameObjects())
            foreach (MeshRenderer original in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!original.enabled || !hall.Intersects(original.bounds) || original.GetComponent<TextMesh>() != null) continue;
                if (!original.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
                var copy = new GameObject(original.name + " [CCTV]");
                copy.layer = StageLayer;
                copy.transform.SetParent(stage.transform, false);
                copy.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
                copy.transform.localScale = original.transform.lossyScale;
                copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = copy.AddComponent<MeshRenderer>();
                var sourceMaterials = original.sharedMaterials;
                var replacements = new Material[sourceMaterials.Length];
                for (int i = 0; i < replacements.Length; i++)
                {
                    Material source = sourceMaterials[i];
                    if (source != null && mapMaterials.TryGetValue(source, out var cached)) replacements[i] = cached;
                    else
                    {
                        replacements[i] = MakeMaterial(source, false);
                        if (source != null) mapMaterials[source] = replacements[i];
                    }
                }
                renderer.sharedMaterials = replacements;
            }
            BuildActor();
            var cameraObject = new GameObject("Pool surveillance camera");
            cameraObject.transform.SetParent(stage.transform, false);
            cameraObject.transform.position = new Vector3(45, 5, 39);
            cameraObject.transform.LookAt(new Vector3(35, 1, 47));
            cameraFeed = cameraObject.AddComponent<Camera>();
            cameraFeed.clearFlags = CameraClearFlags.SolidColor;
            cameraFeed.backgroundColor = new Color(.018f, .025f, .024f);
            cameraFeed.cullingMask = 1 << StageLayer;
            cameraFeed.nearClipPlane = .1f;
            cameraFeed.farClipPlane = 85;
            cameraFeed.fieldOfView = 52;
            cameraFeed.allowHDR = false;
            cameraFeed.allowMSAA = false;
            cameraFeed.enabled = false;
            var data = stage.AddComponent<LobbyCctvStage>();
            data.feedCamera = cameraFeed;
            data.actorRoot = stage.transform.Find("CCTV subject").gameObject;
            data.idleClip = AssetDatabase.LoadAssetAtPath<CharacterAsset>("Assets/Prefabs/Network/Characters/UnityChan.asset").idleClip;
        }

        private Material MakeMaterial(Material source, bool subject)
        {
            var material = new Material(surface) { name = "CCTV display material" };
            string textureProperty = source != null && source.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
            if (source != null && source.HasProperty(textureProperty))
            {
                material.SetTexture("_BaseMap", source.GetTexture(textureProperty));
                material.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
            }
            Color tint = source != null && source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor")
                : source != null && source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            tint.a = 1;
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Brightness", subject ? .12f : .65f);
            material.SetFloat("_Subject", subject ? 1 : 0);
            bool water = source != null && (source.name.ToLowerInvariant().Contains("water") || source.shader.name.Contains("Water"));
            material.SetFloat("_Water", water ? 1 : 0);
            AssetDatabase.CreateAsset(material, MaterialFolder + "/CCTV_" + materialIndex++ + ".mat");
            return material;
        }

        private void BuildActor()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CharacterAsset>("Assets/Prefabs/Network/Characters/UnityChan.asset");
            if (asset == null || asset.visualPrefab == null) throw new Exception("CCTV character asset is missing.");
            var anchor = new GameObject("CCTV subject");
            anchor.SetActive(false);
            anchor.transform.SetParent(stage.transform, false);
            anchor.transform.position = new Vector3(42, 0, 45);
            anchor.transform.rotation = Quaternion.Euler(0, 155, 0);
            anchor.transform.localScale = asset.modelScale;
            var model = UnityEngine.Object.Instantiate(asset.visualPrefab, anchor.transform, false);
            foreach (var script in model.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in model.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
            foreach (var otherCamera in model.GetComponentsInChildren<Camera>(true)) otherCamera.enabled = false;
            foreach (var listener in model.GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var audio in model.GetComponentsInChildren<AudioSource>(true)) { audio.playOnAwake = false; audio.enabled = false; }
            foreach (Transform child in anchor.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = StageLayer;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var sources = renderer.sharedMaterials;
                var replacements = new Material[sources.Length];
                for (int i = 0; i < sources.Length; i++) replacements[i] = MakeMaterial(sources[i], true);
                renderer.sharedMaterials = replacements;
            }
            anchor.SetActive(true);
            var animator = model.GetComponentInChildren<Animator>();
            if (animator != null) { animator.applyRootMotion = false; animator.runtimeAnimatorController = null; }
        }

    }
}


