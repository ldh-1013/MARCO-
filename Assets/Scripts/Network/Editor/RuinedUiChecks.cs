using System;
using UnityEditor;
using UnityEngine;

namespace Marco.Network.Editor
{
    public static class RuinedUiChecks
    {
        public static void Run()
        {
            try
            {
                string[] names = { "health_stamina_worn", "stamina_yellow_ruined", "lobby_labels_ruined_v3",
                    "lobby_support_labels_ruined_v3", "aim_reticle_ruined_v3", "healing_ring_fill_ruined_v3",
                    "healing_ring_track_ruined_v3", RuinedUi.PaperAsset, "item_icons_worn", "slot_boxes_worn" };
                foreach (string name in names)
                {
                    string path = "Assets/Resources/" + RuinedUi.Root + name + ".png";
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    var texture = RuinedUi.Texture(name);
                    Check(texture != null, "Resource loads: " + name);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    Check(importer.alphaIsTransparency && !importer.mipmapEnabled &&
                        importer.textureCompression == TextureImporterCompression.Uncompressed, "UI import preserves alpha: " + name);
                }
                foreach (string name in new[] { "lobby_labels_ruined_v3", "lobby_support_labels_ruined_v3" })
                {
                    var original = RuinedUi.Texture(name).GetPixels32();
                    var light = RuinedUi.LightTexture(name).GetPixels32();
                    Check(original.Length == light.Length, "Light lettering keeps source dimensions");
                    int transparent = 0, brightInk = 0;
                    for (int i = 0; i < original.Length; i++)
                    {
                        if (original[i].a != light[i].a) throw new Exception("Lettering alpha changed: " + name);
                        if (light[i].a == 0) transparent++;
                        if (light[i].a > 128 && light[i].r > 180) brightInk++;
                    }
                    Check(transparent > light.Length / 2 && brightInk > 1000,
                        "Light lettering preserves transparent background and visible ink: " + name);
                }
                Check(Count(RuinedUi.ProgressTexture(0)) == 0, "Healing 0% is empty");
                int quarter = Count(RuinedUi.ProgressTexture(.25f));
                var pixels = RuinedUi.ProgressTexture(.25f).GetPixels32();
                int wrongQuadrant = 0;
                for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                    if (pixels[y * 256 + x].a > 32 && (x < 128 || y < 128)) wrongQuadrant++;
                Check(quarter > 100 && wrongQuadrant == 0, "Healing advances clockwise from twelve o'clock");
                int half = Count(RuinedUi.ProgressTexture(.5f));
                int full = Count(RuinedUi.ProgressTexture(1));
                Check(quarter < half && half < full && half > full * .4f && half < full * .6f,
                    "Healing 25/50/100% masks expose proportional artwork");
                Check(RuinedUi.ProgressTexture(1).GetPixel(128, 128).a == 0, "Ring center stays transparent");
                Check(Count(RuinedUi.ProgressTexture(-1)) == 0 && Count(RuinedUi.ProgressTexture(2)) == full,
                    "Healing progress clamps safely");
                Check(ReferenceEquals(RuinedUi.ProgressTexture(.1f), RuinedUi.ProgressTexture(.9f)),
                    "Progress reuses its texture instead of allocating every frame");
                Debug.Log("[RuinedUiChecks] PASS: imports, resources, radial direction, progress and texture reuse");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static int Count(Texture2D texture)
        {
            int count = 0;
            foreach (var pixel in texture.GetPixels32()) if (pixel.a > 32) count++;
            return count;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Debug.Log("[RuinedUiChecks] PASS: " + message);
        }
    }
}
