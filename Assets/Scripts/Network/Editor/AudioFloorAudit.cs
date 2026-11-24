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
            var counts = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include))
                foreach (var part in collider.GetComponentsInParent<MonoBehaviour>(true))
                {
                    if (part == null || part.GetType().Name != "PoolV12Part") continue;
                    var partType = part.GetType();
                    string category = (string)partType.GetField("category").GetValue(part);
                    string zone = (string)partType.GetField("zone").GetValue(part);
                    if (category != "Floor" && category != "Stair" && !(category == "Obstacle" && zone == "ST")) break;
                    string surface = (string)type.GetMethod("Surface").Invoke(world, new object[] { collider, collider.bounds.center });
                    string expected = category == "Stair" ? "MetalStairs" : category == "Obstacle" && zone == "ST" ? "Metal" : zone switch
                    {
                        "SC" or "ST2" or "L2" => "Metal",
                        "ML" or "WL" => "Tile",
                        "ST" => "Concrete",
                        "LOB" or "OF1" or "CL" => "Carpet",
                        "AID" => collider.bounds.center.z >= 24f ? "Carpet" : "Tile",
                        "SZ" => collider.bounds.center.z >= 18f ? "Concrete" : "Carpet",
                        _ => surface
                    };
                    if (surface != expected || string.IsNullOrEmpty(surface))
                        throw new Exception($"Unmapped floor {part.name}: {category}/{zone} -> {surface}, expected {expected}");
                    counts.TryGetValue(surface, out int total);
                    counts[surface] = total + 1;
                    break;
                }
            foreach (var material in new[] { "Tile", "Concrete", "Carpet", "Metal", "MetalStairs" })
                if (!counts.ContainsKey(material)) throw new Exception("No floor mapped to " + material);
            string[] names = { "Concrete_0", "Concrete_1", "Concrete_2", "Concrete_3", "Tile_0", "Tile_1", "Tile_2",
                "Moan_0", "Moan_1", "Hurt", "Treat", "Valve", "Bandage", "Reflow", "EndDrone", "Heartbeat", "SeekerDrone", "SeekerDroneLP", "SurfaceSwim",
                "Carpet", "Mat", "Metal", "MetalStairs", "Breath_Seeker", "Breath_Runner", "ValveJam",
                "ValveOpened", "DiveAmbience", "ValveUnderwater", "Scream", "SeekerAttack", "WoodBench", "SeekerSwing" };
            foreach (var name in names)
            {
                var clip = Resources.Load<AudioClip>("MarcoSound/" + name);
                if (clip == null || clip.length < .1f) throw new Exception("Missing clip " + name);
            }
            int showerSamples = 0;
            foreach (var room in new[] { (minX: 9f, maxX: 21f), (minX: 33f, maxX: 43f) })
                for (float x = room.minX + .5f; x < room.maxX; x += 1f)
                    for (float z = 24.5f; z < 31f; z += 1f)
                        foreach (var hit in Physics.RaycastAll(new Vector3(x, 2f, z), Vector3.down, 3f))
                        {
                            if (hit.normal.y < .5f) continue;
                            bool isFloor = false;
                            foreach (var part in hit.collider.GetComponentsInParent<MonoBehaviour>())
                                if (part != null && part.GetType().Name == "PoolV12Part" &&
                                    (string)part.GetType().GetField("category").GetValue(part) == "Floor") isFloor = true;
                            if (!isFloor) continue;
                            string actual = (string)type.GetMethod("Surface").Invoke(world, new object[] { hit.collider, hit.point });
                            if (actual != "Tile") throw new Exception($"Shower floor {hit.point} uses {actual}");
                            showerSamples++;
                        }
            if (showerSamples == 0) throw new Exception("Shower sample empty");
            Debug.Log("[AudioFloorAudit] Shower floor samples all Tile: " + showerSamples);
            foreach (var name in new[] { "Carpet", "Mat", "Metal", "MetalStairs", "Breath_Seeker", "Breath_Runner" })
            {
                var original = Resources.Load<AudioClip>("MarcoSound/" + name);
                var loopType = typeof(NetworkRound).Assembly.GetType("Marco.Network.Audio.LoopClip", true);
                float duration = name.StartsWith("Breath_") ? .6f : .34f;
                var cue = (AudioClip)loopType.GetMethod("TrimCue").Invoke(null, new object[] { original, duration });
                if (cue == original || Mathf.Abs(cue.length - duration) > .01f)
                    throw new Exception("Cue trim failed for " + name);
                UnityEngine.Object.DestroyImmediate(cue);
            }
            foreach (var pair in counts) Debug.Log($"[AudioFloorAudit] {pair.Key}: {pair.Value} colliders");
            Debug.Log($"[AudioFloorAudit] PASS: 6 floor types, {names.Length} resources and 6 trimmed cues.");
            EditorSceneManager.OpenScene("Assets/Scenes/Network/NetworkPool_v1_2.unity", OpenSceneMode.Single);
        }
    }
}
