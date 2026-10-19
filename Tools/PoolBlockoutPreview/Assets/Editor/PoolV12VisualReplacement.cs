using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Keeps canonical collision and placement while fitting a future FBX into a selected placeholder.
public sealed class PoolV12VisualReplacement : EditorWindow
{
    GameObject replacement;

    [MenuItem("Tools/Indoor Pool v1.2/Replace Selected Module Visual")]
    static void Open() => GetWindow<PoolV12VisualReplacement>("FBX Visual Replacement");

    void OnSelectionChange() => Repaint();

    void OnGUI()
    {
        var part = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponentInParent<PoolV12Part>();
        EditorGUILayout.LabelField("1. Select a wall, floor or prop in Hierarchy.");
        EditorGUILayout.LabelField("2. Choose a model asset from Project.");
        replacement = (GameObject)EditorGUILayout.ObjectField("FBX / Prefab", replacement, typeof(GameObject), false);
        if (part != null) EditorGUILayout.LabelField("Selected", part.designId + " / " + part.displayName);
        EditorGUILayout.HelpBox("Fits with uniform scale, preserves colliders, and hides old renderers. Ctrl+Z undoes replacement. Stairs, pools and doors need their own assembly adjustment.", MessageType.Info);
        bool supported = part != null && new[] { "Wall", "Floor", "Obstacle", "Guardrail" }.Contains(part.category);
        using (new EditorGUI.DisabledScope(!supported || replacement == null))
            if (GUILayout.Button("Replace Visual (keep collision)")) Replace(part, replacement);
    }

    public static GameObject Replace(PoolV12Part part, GameObject model)
    {
        if (part == null || model == null || !AssetDatabase.Contains(model)) throw new ArgumentException("Select a scene module and a project model asset.");
        var oldRenderers = part.GetComponentsInChildren<MeshRenderer>(true);
        if (oldRenderers.Length == 0) throw new InvalidOperationException("Selected module has no geometry.");
        Bounds target = oldRenderers[0].bounds;
        foreach (var renderer in oldRenderers.Skip(1)) target.Encapsulate(renderer.bounds);
        if (target.size.sqrMagnitude < .000001f) throw new InvalidOperationException("Show the selected module before replacing it.");
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Replace blockout visual");
        var result = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(result, "Add replacement model");
        Undo.SetTransformParent(result.transform, part.transform, "Parent replacement model");
        result.name = "FBX_" + model.name;
        result.transform.localPosition = Vector3.zero; result.transform.localRotation = Quaternion.identity;
        Vector3 parentScale = part.transform.lossyScale;
        result.transform.localScale = new Vector3(1 / parentScale.x, 1 / parentScale.y, 1 / parentScale.z);
        var renderers = result.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) { Undo.RevertAllDownToGroup(group); throw new InvalidOperationException("Model has no renderers."); }
        Bounds CurrentBounds()
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
        Bounds source = CurrentBounds();
        float scale = Mathf.Min(target.size.x / Mathf.Max(.00001f, source.size.x),
            target.size.y / Mathf.Max(.00001f, source.size.y), target.size.z / Mathf.Max(.00001f, source.size.z));
        result.transform.localScale *= scale;
        source = CurrentBounds();
        result.transform.position += new Vector3(target.center.x - source.center.x, target.min.y - source.min.y, target.center.z - source.center.z);
        foreach (var collider in result.GetComponentsInChildren<Collider>(true))
        { Undo.RecordObject(collider, "Preserve blockout collision"); collider.enabled = false; }
        foreach (var renderer in oldRenderers)
        { Undo.RecordObject(renderer, "Hide old visual"); renderer.enabled = false; }
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(part.gameObject.scene);
        Selection.activeGameObject = result;
        return result;
    }
}
