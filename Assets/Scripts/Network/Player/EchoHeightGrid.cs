using UnityEngine;

namespace Marco.Network.Player
{
    // [11-25 V65] 기획서 3.2 · 맵설계 ⑫ — where an Echo body may fly, per position (x, z): above the lowest floor and under the
    // highest slab. The values are MEASURED in the map scene (IndoorPool_v1_2), not designed: EchoHeightGridBaker casts a ray
    // straight down at every 0.5 m cell centre and keeps
    //   floor = the lowest hit whose face points up (a pool's floor in the pools, the 1층 floor elsewhere, a shaft's own floor),
    //   top   = the highest slab (BoxCollider) hit's top − that slab's own bounds height (= its underside).
    // So the gap between a 1층 ceiling and the 2층 floor above it lies inside [floor, top] (flying between floors is allowed);
    // only the outermost slab and the lowest floor bound the flight. A cell with no hit keeps the old building box
    // (EchoBodyRules.FlightMinY / FlightMaxY). Cell (ix, iz) covers x ∈ [originX + ix·cell, +cell), z likewise; index ix·countZ + iz.
    public sealed class EchoHeightGrid : ScriptableObject
    {
        public const string ResourcePath = "EchoHeightGrid";

        public float originX, originZ, cellSize;
        public int countX, countZ;
        public float[] floor, top;
        // Where the numbers came from (checked by EchoHeightGridTests).
        public string sourceScene, measuredAt, gitSha;
        public int noHitCells;

        public bool IsValid =>
            cellSize > 0f && countX > 0 && countZ > 0 && floor != null && top != null &&
            floor.Length == countX * countZ && top.Length == countX * countZ;
    }

    // The one lookup (static, no allocation after the first call). Used by EchoBodyRules.ClampFlight (owner, server, knock point)
    // and EchoInertia (the drawn camera). False = no grid or (x, z) outside it: the caller keeps the building box.
    public static class EchoBoundsGrid
    {
        private static EchoHeightGrid grid;
        private static bool loaded;

        public static EchoHeightGrid Grid
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    try
                    {
                        var g = Resources.Load<EchoHeightGrid>(EchoHeightGrid.ResourcePath);
                        grid = g != null && g.IsValid ? g : null;
                    }
                    catch (System.Exception) { grid = null; }
                }
                return grid;
            }
        }

        public static bool TryGetVerticalRange(float x, float z, out float floor, out float top)
        {
            floor = EchoBodyRules.FlightMinY; top = EchoBodyRules.FlightMaxY;
            var g = Grid;
            if (g == null) return false;
            float fx = (x - g.originX) / g.cellSize, fz = (z - g.originZ) / g.cellSize;
            if (!(fx >= 0f && fz >= 0f && fx <= g.countX && fz <= g.countZ)) return false;   // NaN and outside: false
            int ix = Mathf.Min((int)fx, g.countX - 1), iz = Mathf.Min((int)fz, g.countZ - 1);   // the far edge belongs to the last cell
            int i = ix * g.countZ + iz;
            floor = g.floor[i]; top = g.top[i];
            if (!(floor <= top)) { floor = EchoBodyRules.FlightMinY; top = EchoBodyRules.FlightMaxY; return false; }
            return true;
        }
    }
}
