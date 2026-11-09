using System;
using System.Reflection;
using Marco.Prototype;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run in a separate batch project: -executeMethod SurvivorInjuryVerification.Run -quit
public static class SurvivorInjuryVerification
{
    private static int checks;
    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var actor = new GameObject("Survivor injury verification");
        var target = actor.AddComponent<PrototypeTarget>();
        var hud = actor.AddComponent<SurvivorHealthHud>();
        Invoke(hud, "Awake");
        Check(hud.IsVisible && !hud.IsInjuryVisible && hud.InjuryOpacity == 0, "Full health has no injury overlay");
        target.Tag();
        Check(target.Health.Lives == 1 && hud.IsInjuryVisible, "Hit enables the injured screen edges at 1.0");
        Check(hud.InjuryTint.r > hud.InjuryTint.g * 5, "1.0 health uses a red tint");
        float hitOpacity = hud.InjuryOpacity;
        Check(hitOpacity > 0.6f && hitOpacity <= 1, "Fresh hit briefly emphasizes the injury");
        typeof(PrototypeTarget).GetField("<LastHitTime>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, -1f);
        float woundedOpacity = hud.InjuryOpacity;
        Check(woundedOpacity > 0.45f && woundedOpacity < hitOpacity, "Red edges persist after the flash ends");
        target.Health.Bandage();
        Check(target.Health.Lives == 1.5f && hud.IsInjuryVisible, "Bandage retains an injury indicator at 1.5");
        Check(hud.InjuryTint.g > 0.3f && hud.InjuryTint.r > hud.InjuryTint.g, "1.5 health uses orange edges");
        Check(hud.InjuryOpacity > 0 && hud.InjuryOpacity < woundedOpacity, "Recovering edges are softer than wounded edges");
        hud.enabled = false;
        Check(!hud.IsInjuryVisible && hud.InjuryOpacity == 0, "Remote/disabled survivor HUD cannot show injury effects");
        hud.enabled = true;
        Check(hud.IsInjuryVisible, "Local HUD restoration reflects the current injury");
        target.Treat();
        Check(!hud.IsInjuryVisible && hud.InjuryOpacity == 0, "Treatment clears the edges");
        target.Tag();
        target.Tag();
        Check(!hud.IsVisible && !hud.IsInjuryVisible && hud.InjuryOpacity == 0, "Echo state clears all injury effects");
        target.ResetHealth();
        Check(!hud.IsInjuryVisible, "Health reset leaves no stale injury effect");

        Invoke(hud, "CreateInjuryVignette");
        var texture = (Texture2D)typeof(SurvivorHealthHud).GetField("injuryVignette", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud);
        Check(texture.GetPixel(128, 128).a == 0 && texture.GetPixel(64, 64).a == 0, "Central aiming area stays fully clear");
        Check(texture.GetPixel(0, 128).a > texture.GetPixel(24, 128).a && texture.GetPixel(24, 128).a > texture.GetPixel(52, 128).a, "Edge fades smoothly towards the centre");
        Check(texture.GetPixel(0, 0).a > texture.GetPixel(0, 128).a, "Corners are stronger than edge midpoints");
        Check(Mathf.Abs(texture.GetPixel(0, 128).a - texture.GetPixel(255, 128).a) < 0.001f
            && Mathf.Abs(texture.GetPixel(128, 0).a - texture.GetPixel(128, 255).a) < 0.001f, "All four sides are symmetric");
        // Edit-mode objects have not entered Play, so invoke the lifecycle callback explicitly.
        Invoke(hud, "OnDestroy");
        Check(texture == null, "HUD destruction callback releases its runtime texture");
        UnityEngine.Object.DestroyImmediate(actor);
        Debug.Log("PASS: SurvivorInjuryVerification " + checks + " checks");
    }

    private static void Invoke(object instance, string method)
    {
        instance.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Survivor injury verification failed: " + message);
        checks++;
        Debug.Log("PASS: " + message);
    }
}
