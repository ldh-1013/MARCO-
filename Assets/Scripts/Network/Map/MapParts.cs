using UnityEngine;

namespace Marco.Network.Map
{
    // The map metadata on a collider (PoolV12Part — Assembly-CSharp, so read by reflection, the same as before in SoundWorld).
    // One lookup for sound occlusion, clicker sight and (later) tag blocking — 기획서 5.6 · 부록 A [사운드 차폐] [태그 판정].
    public sealed class MapPart
    {
        public string Category, Zone, DesignId;
        public MonoBehaviour Component;
        public bool Occluder, TagBlocker;

        // 5.6 차폐 구조물: a part counted by sound occlusion — flagged soundOccluder, or a Wall / Floor / Ceiling.
        public bool IsSoundStructure => IsStructure(Category, Occluder);
        public bool IsSlab => Category == "Floor" || Category == "Ceiling";

        public static bool IsStructure(string category, bool occluder) =>
            occluder || category == "Wall" || category == "Floor" || category == "Ceiling";
    }

    public static class MapParts
    {
        // The nearest PoolV12Part at or above the collider (a locker row's part sits on the row parent), or null.
        public static MapPart Read(Collider collider)
        {
            if (collider == null) return null;
            foreach (var component in collider.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().Name != "PoolV12Part") continue;
                var type = component.GetType();
                return new MapPart
                {
                    Category = type.GetField("category")?.GetValue(component) as string,
                    Zone = type.GetField("zone")?.GetValue(component) as string,
                    DesignId = type.GetField("designId")?.GetValue(component) as string,
                    Occluder = type.GetField("soundOccluder")?.GetValue(component) is bool occluder && occluder,
                    TagBlocker = type.GetField("tagBlocker")?.GetValue(component) is bool blocker && blocker,
                    Component = component
                };
            }
            return null;
        }
    }
}
