using System;
using System.Reflection;
using Marco.Network.Round;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    public static class AudioFloorAudit
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Maps/IndoorPool_v1_2.unity", OpenSceneMode.Single);
            var type = typeof(NetworkRound).Assembly.GetType("Marco.Network.Audio.SoundWorld", true);
            var world = Activator.CreateInstance(type);
            int tile = 0, concrete = 0, stairs = 0;
            foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
                foreach (var part in collider.GetComponentsInParent<MonoBehaviour>(true))
                {
                    if (part == null || part.GetType().Name != "PoolV12Part") continue;
                    var partType = part.GetType();
                    string category = (string)partType.GetField("category").GetValue(part);
                    string zone = (string)partType.GetField("zone").GetValue(part);
                    if (category != "Floor" && category != "Stair" && !(category == "Obstacle" && zone == "ST")) break;
                    string surface = (string)type.GetMethod("Surface").Invoke(world, new object[] { collider, collider.bounds.center });
                    bool metal = category == "Stair" || zone == "SC" || zone == "ST2" || zone == "L2";
                    bool soft = zone == "LOB" || zone == "OF1" || zone == "ML" || zone == "WL" || zone == "CL" || zone == "ST";
                    if (metal && surface != "Concrete" || soft && surface != "Tile" || string.IsNullOrEmpty(surface))
                        throw new Exception($"Unmapped floor {part.name}: {category}/{zone} -> {surface}");
                    if (surface == "Tile") tile++; else concrete++;
                    if (category == "Stair") stairs++;
                    break;
                }
            if (tile == 0 || concrete == 0 || stairs == 0) throw new Exception("Floor coverage incomplete");
            string[] names = { "Concrete_0", "Concrete_1", "Concrete_2", "Concrete_3", "Tile_0", "Tile_1", "Tile_2",
                "Moan_0", "Moan_1", "Hurt", "Treat", "Valve", "Bandage", "Reflow", "EndDrone", "Heartbeat", "SeekerDrone", "SurfaceSwim" };
            foreach (var name in names)
            {
                var clip = Resources.Load<AudioClip>("MarcoSound/" + name);
                if (clip == null || clip.length < .1f) throw new Exception("Missing clip " + name);
            }
            Debug.Log($"[AudioFloorAudit] PASS: {tile} tile/substitute, {concrete} concrete/substitute colliders including {stairs} stair colliders; 18 clips loaded. No scene saved.");
        }
    }
}
