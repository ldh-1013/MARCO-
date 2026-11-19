using System.Collections.Generic;
using UnityEngine;

namespace Marco.Network.Character
{
    /// <summary>Creates MARCO world-lit material copies for runtime-spawned visuals.</summary>
    public sealed class RuntimeMarcoSurfaceMaterials : MonoBehaviour
    {
        public const string ShaderName = "MARCO/WorldSpaceTexturedSurface";

        private readonly List<Material> ownedMaterials = new();

        public static void ApplyTo(GameObject root)
        {
            if (root == null) return;
            var shader = Shader.Find(ShaderName);
            if (shader == null) return;

            var replacements = new Dictionary<Material, Material>();
            var created = new List<Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = materials[i];
                    if (source == null || source.shader == shader) continue;
                    if (!replacements.TryGetValue(source, out var replacement))
                    {
                        replacement = CreateMarcoMaterial(source, shader);
                        replacements.Add(source, replacement);
                        created.Add(replacement);
                    }
                    materials[i] = replacement;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }

            if (created.Count == 0) return;
            var owner = root.GetComponent<RuntimeMarcoSurfaceMaterials>();
            if (owner == null) owner = root.AddComponent<RuntimeMarcoSurfaceMaterials>();
            owner.ownedMaterials.AddRange(created);
        }

        private static Material CreateMarcoMaterial(Material source, Shader shader)
        {
            var material = new Material(shader) { name = source.name + " (MARCO world light)" };
            string textureProperty = source.HasProperty("_BaseMap") ? "_BaseMap"
                : source.HasProperty("_MainTex") ? "_MainTex" : null;
            if (textureProperty != null)
            {
                material.SetTexture("_BaseMap", source.GetTexture(textureProperty));
                material.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
            }

            string colorProperty = source.HasProperty("_BaseColor") ? "_BaseColor"
                : source.HasProperty("_Color") ? "_Color" : null;
            if (colorProperty != null) material.SetColor("_BaseColor", source.GetColor(colorProperty));
            return material;
        }

        private void OnDestroy()
        {
            foreach (var material in ownedMaterials)
                if (material != null) Destroy(material);
            ownedMaterials.Clear();
        }
    }
}
