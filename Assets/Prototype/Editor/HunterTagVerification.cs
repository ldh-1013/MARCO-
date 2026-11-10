using System;
using System.Reflection;
using Marco.Prototype;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run in batch mode: -executeMethod HunterTagVerification.Run -quit
public static class HunterTagVerification
{
    private static int checks;

    public static void Run()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use batch mode for the physics verification.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var actor = new GameObject("Hunter");
        actor.transform.position = Vector3.up;
        var controller = actor.AddComponent<CharacterController>();
        controller.height = 2;
        controller.radius = 0.35f;
        var hunter = actor.AddComponent<HunterTagPrototype>();
        var victim = new GameObject("Survivor");
        victim.transform.position = new Vector3(0, 1, 1.5f);
        var capsule = victim.AddComponent<CapsuleCollider>();
        capsule.height = 2;
        capsule.radius = 0.35f;
        var target = victim.AddComponent<PrototypeTarget>();

        Check(hunter.FindTagTarget() == target, "Body surface in range even when pivot is outside 1.2m");
        Check(hunter.TrySwing() && target.Health.Lives == 1, "Swing damages the indicated target once");
        Check(Mathf.Abs(hunter.CooldownRemaining - 2.5f) < 0.05f && hunter.CooldownFraction > 0.98f, "Hit starts the full 2.5s ring");
        Check(!hunter.TrySwing() && target.Health.Lives == 1, "Cooldown rejects a second swing");

        ResetCooldown(hunter);
        victim.transform.position = new Vector3(0, 1, 1.7f);
        Check(hunter.FindTagTarget() == null, "Body outside reach is rejected immediately before a physics step");
        Check(hunter.TrySwing() && target.Health.Lives == 1, "Miss does not damage target");
        Check(Mathf.Abs(hunter.CooldownRemaining - 1.5f) < 0.05f && hunter.CooldownFraction > 0.98f, "Miss starts the full 1.5s ring");

        victim.transform.position = new Vector3(0, 1, -1);
        Check(hunter.FindTagTarget() == null, "Target behind the hunter is rejected");
        victim.transform.position = new Vector3(1, 1, 0);
        Check(hunter.FindTagTarget() == null, "Target beside the hunter is rejected");
        victim.transform.position = new Vector3(0.7f, 1, 0.9f);
        Check(hunter.FindTagTarget() == target, "Front cone accepts an off-centre body");

        victim.transform.position = new Vector3(0, 1, 1.1f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(0, 1, 0.55f);
        wall.transform.localScale = new Vector3(2, 3, 0.1f);
        Check(hunter.FindTagTarget() == null, "Solid wall blocks the attack");
        wall.GetComponent<BoxCollider>().isTrigger = true;
        Check(hunter.FindTagTarget() == target, "Trigger volumes do not block the attack");
        UnityEngine.Object.DestroyImmediate(wall);

        var ownCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ownCollider.transform.SetParent(actor.transform, false);
        ownCollider.transform.localPosition = new Vector3(0, 0, 0.5f);
        ownCollider.transform.localScale = Vector3.one * 0.1f;
        Check(hunter.FindTagTarget() == target, "Hunter child colliders do not obstruct its own attack");
        UnityEngine.Object.DestroyImmediate(ownCollider);

        var sharedRoot = new GameObject("Shared players container");
        actor.transform.SetParent(sharedRoot.transform, true);
        victim.transform.SetParent(sharedRoot.transform, true);
        Check(hunter.FindTagTarget() == target, "Players sharing a scene container remain taggable");

        // Feet pivots: the body centre is a metre above the player transform.
        actor.transform.position = Vector3.zero;
        controller.center = Vector3.up;
        victim.transform.position = new Vector3(0, 0, 1.1f);
        capsule.center = Vector3.up;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0, -0.05f, 0);
        ground.transform.localScale = new Vector3(10, 0.1f, 10);
        Check(hunter.FindTagTarget() == target, "Feet pivots use body height and avoid the floor");
        victim.transform.position = new Vector3(0, 4, 1.1f);
        Check(hunter.FindTagTarget() == null, "Target on another floor is outside the sphere");

        victim.transform.position = new Vector3(0, 0, 1.1f);
        target.Tag();
        Check(hunter.FindTagTarget() == null, "Echo is excluded");
        target.ResetHealth();
        ResetCooldown(hunter);
        hunter.SetLocalPlayer(false);
        Check(!hunter.TrySwing() && target.Health.Lives == 2, "Remote hunter cannot apply local input damage");
        hunter.SetLocalPlayer(true);
        Check(hunter.TrySwing() && target.Health.Lives == 1, "Restoring local ownership enables attack");
        Check(hunter.FindTagTarget() == target, "Wounded survivor remains taggable");
        Debug.Log("PASS: HunterTagVerification " + checks + " checks");
    }

    private static void ResetCooldown(HunterTagPrototype hunter)
    {
        typeof(HunterTagPrototype).GetField("nextTagTime", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hunter, 0f);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Hunter tag verification failed: " + message);
        checks++;
        Debug.Log("PASS: " + message);
    }
}
