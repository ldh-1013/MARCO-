using System;
using System.Linq;
using System.Reflection;
using Marco.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CombinedSceneVerification
{
    [MenuItem("MARCO/Verify combined scene")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play mode before verifying scenes.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save modified scenes before verifying.");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (PrototypePlayerRole role in Enum.GetValues(typeof(PrototypePlayerRole)))
            {
                var scene = EditorSceneManager.OpenScene("Assets/Prototype/Scenes/Prototype_Game.unity");
                var roots = scene.GetRootGameObjects();
                var hud = roots.SelectMany(r => r.GetComponentsInChildren<PrototypeSceneBootstrap>(true)).Single();
                var hunter = roots.Single(r => r.name == "Hunter Player");
                var survivor = roots.Single(r => r.name == "Survivor Player");
                Check(roots.SelectMany(r => r.GetComponentsInChildren<PrototypeTarget>(true)).Count() == 1,
                    "Only the survivor player has health; no training dummy remains");
                MatchLaunchContext.Begin(role);
                typeof(PrototypeSceneBootstrap).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
                Check(hud.LocalRole == role, "Launch role is applied");
                Check(hunter.GetComponent<PrototypeFirstPersonController>().enabled == (role == PrototypePlayerRole.Hunter), "Hunter input ownership");
                Check(survivor.GetComponent<PrototypeFirstPersonController>().enabled == (role == PrototypePlayerRole.Survivor), "Survivor input ownership");
                Check(survivor.GetComponent<SurvivorHealthHud>().enabled == (role == PrototypePlayerRole.Survivor), "Health HUD is visible to its owner only");
                Check(hunter.GetComponent<FirstPersonBodyView>().enabled == (role == PrototypePlayerRole.Hunter), "Hunter first-person body ownership");
                Check(survivor.GetComponent<FirstPersonBodyView>().enabled == (role == PrototypePlayerRole.Survivor), "Survivor first-person body ownership");
                Check(roots.SelectMany(r => r.GetComponentsInChildren<Camera>()).Count(c => c.isActiveAndEnabled) == 1, "Exactly one active camera");
                Check(roots.SelectMany(r => r.GetComponentsInChildren<AudioListener>()).Count(c => c.isActiveAndEnabled) == 1, "Exactly one audio listener");
                Check(roots.SelectMany(r => r.GetComponentsInChildren<MicrophoneVisionController>()).Count(c => c.isActiveAndEnabled) == 1, "Exactly one microphone/vision overlay");
                Check(survivor.GetComponent<PrototypeTarget>() != null && survivor.GetComponent<SurvivorGhostPrototype>() != null, "Survivor health and ghost retained");
                Check(hunter.GetComponent<HunterTagPrototype>() != null, "Hunter attack retained");
                foreach (var root in roots)
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, "No missing scripts: " + transform.name);
            }
            Check(EditorBuildSettings.scenes.Any(s => s.enabled && s.path.EndsWith("/Prototype_Game.unity")), "Game is registered for build");
            Debug.Log("PASS: combined scene references, both role inputs, camera/audio/microphone ownership");
        }
        finally
        {
            MatchLaunchContext.Clear();
            if (setup.Any(s => s.isActive && s.isLoaded && !string.IsNullOrEmpty(s.path)))
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Combined scene verification failed: " + message);
    }
}
