using UnityEditor;
using UnityEngine;

namespace Marco.Network.Editor
{
    public sealed class RuinedUiImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/UI/Ruined/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            bool ring = assetPath.EndsWith("healing_ring_fill_ruined_v3.png");
            importer.isReadable = ring || assetPath.EndsWith("lobby_labels_ruined_v3.png") ||
                assetPath.EndsWith("lobby_support_labels_ruined_v3.png");
            importer.maxTextureSize = ring ? 512 : 4096;
        }
    }
}
