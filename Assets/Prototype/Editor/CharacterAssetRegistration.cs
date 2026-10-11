using System;
using System.IO;
using System.Linq;
using Marco.Prototype;
using UnityEditor;
using UnityEngine;

public static class CharacterAssetRegistration
{
    [MenuItem("MARCO/Characters/Register selected prefab")]
    public static void Register()
    {
        var prefab = Selection.activeObject as GameObject;
        if (prefab == null || !EditorUtility.IsPersistent(prefab))
            throw new InvalidOperationException("Select a character prefab/model in the Project window, not a scene object.");
        var animator = prefab.GetComponentInChildren<Animator>(true);
        if (animator == null) throw new InvalidOperationException("Character needs an Animator and an imported rig.");
        foreach (string path in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(s => s.sharedMesh != null).Select(s => AssetDatabase.GetAssetPath(s.sharedMesh)).Distinct())
            if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
            { importer.isReadable = true; importer.SaveAndReimport(); }
        var definition = ScriptableObject.CreateInstance<CharacterAssetDefinition>();
        definition.visualPrefab = prefab;
        var clips = (animator.runtimeAnimatorController == null ? new AnimationClip[0] : animator.runtimeAnimatorController.animationClips)
            .Concat(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(prefab)).OfType<AnimationClip>()).Distinct().ToArray();
        definition.idleClip = Pick("idle", "wait"); definition.walkClip = Pick("walk");
        definition.runClip = Pick("run", "sprint"); definition.attackClip = Pick("punch", "attack");
        AnimationClip Pick(params string[] terms) => clips.FirstOrDefault(c => terms.Any(t => c.name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0));
        Directory.CreateDirectory("Assets/Resources/CharacterAssets"); AssetDatabase.Refresh();
        string name = new string(prefab.name.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == ' ').ToArray());
        if (string.IsNullOrWhiteSpace(name)) name = "Character";
        string assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Resources/CharacterAssets/" + name + ".asset");
        AssetDatabase.CreateAsset(definition, assetPath); AssetDatabase.SaveAssets(); Selection.activeObject = definition;
        Debug.Log("Registered " + definition.name + ". Check idle/walk/run/action clips and proportions. " +
            (animator.isHuman ? "Humanoid bones map automatically." : "Generic rig: fill the semantic bone mappings once."));
    }
}
