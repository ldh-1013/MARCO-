using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Marco.Network.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Marco.Network.Editor
{
    // [11-25 V65] Measures the Echo flight's per-position vertical range in the map scene and stores it as
    // Assets/Resources/EchoHeightGrid.asset (EchoHeightGrid / EchoBoundsGrid). Nothing here is a design value: every number comes
    // from the scene's colliders. One ray straight down per 0.5 m cell centre over the building outline (EchoBodyRules.Flight*),
    // triggers excluded, every hit kept:
    //   floor = the lowest hit whose face points up;
    //   top   = the highest BoxCollider (slab) hit's y − that collider's bounds height (its underside). A non-box hit above the
    //           highest slab (the roll shutters on the outline) is not a slab and is skipped.
    //   no hit at all (or no slab, or top < floor) = the old building box, counted.
    // Batch: -executeMethod Marco.Network.Editor.EchoHeightGridBaker.Bake   (writes the asset)
    //        -executeMethod Marco.Network.Editor.EchoHeightGridBaker.Verify (measures again, compares with the asset, writes nothing)
    //        optional -echoGridReport <file.csv> for the per-cell table.
    public static class EchoHeightGridBaker
    {
        public const string ScenePath = "Assets/Scenes/Maps/IndoorPool_v1_2.unity";
        public const string AssetPath = "Assets/Resources/EchoHeightGrid.asset";
        public const float CellSize = .5f;

        private sealed class Measured
        {
            public int countX, countZ;
            public float[] floor, top;
            public int noHit, noSlab, inverted, skippedNonSlabTop;
            public readonly Dictionary<string, int> slabThickness = new();
            public readonly List<string> rows = new() { "x,z,hits,floor,top,slabTop,slabThickness,slabName" };
        }

        [MenuItem("MARCO/Echo Height Grid/Bake")]
        public static void Bake()
        {
            var m = Measure();
            var grid = AssetDatabase.LoadAssetAtPath<EchoHeightGrid>(AssetPath);
            bool created = grid == null;
            if (created) grid = ScriptableObject.CreateInstance<EchoHeightGrid>();
            grid.originX = EchoBodyRules.FlightMinX; grid.originZ = EchoBodyRules.FlightMinZ; grid.cellSize = CellSize;
            grid.countX = m.countX; grid.countZ = m.countZ; grid.floor = m.floor; grid.top = m.top;
            grid.sourceScene = ScenePath;
            grid.measuredAt = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            grid.gitSha = GitHead();
            grid.noHitCells = m.noHit;
            if (created) AssetDatabase.CreateAsset(grid, AssetPath); else EditorUtility.SetDirty(grid);
            AssetDatabase.SaveAssets();
            Report(m, "BAKED");
        }

        [MenuItem("MARCO/Echo Height Grid/Verify")]
        public static void Verify()
        {
            var m = Measure();
            var grid = AssetDatabase.LoadAssetAtPath<EchoHeightGrid>(AssetPath);
            if (grid == null || !grid.IsValid) { Debug.LogError("[EchoHeightGridBaker] VERIFY FAIL: no valid asset at " + AssetPath); return; }
            int diff = 0;
            if (grid.countX != m.countX || grid.countZ != m.countZ) diff = -1;
            else
                for (int i = 0; i < m.floor.Length; i++)
                    if (grid.floor[i] != m.floor[i] || grid.top[i] != m.top[i]) diff++;
            Report(m, diff == 0 ? "VERIFY REPRODUCED" : "VERIFY FAIL");
            Debug.Log($"[EchoHeightGridBaker] verify cells={m.floor.Length} differing={diff} asset sha={grid.gitSha} measuredAt={grid.measuredAt}");
        }

        private static Measured Measure()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var scene = EditorSceneManager.GetActiveScene();
            Bounds all = default; bool any = false;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
            {
                if (c == null || c.isTrigger || c.gameObject.scene != scene) continue;
                if (!any) { all = c.bounds; any = true; } else all.Encapsulate(c.bounds);
            }
            float startY = any ? all.max.y + 1f : EchoBodyRules.FlightMaxY + 1f;
            float length = any ? all.size.y + 2f : EchoBodyRules.FlightMaxY - EchoBodyRules.FlightMinY + 2f;

            var m = new Measured
            {
                countX = Mathf.RoundToInt((EchoBodyRules.FlightMaxX - EchoBodyRules.FlightMinX) / CellSize),
                countZ = Mathf.RoundToInt((EchoBodyRules.FlightMaxZ - EchoBodyRules.FlightMinZ) / CellSize)
            };
            m.floor = new float[m.countX * m.countZ]; m.top = new float[m.countX * m.countZ];
            for (int ix = 0; ix < m.countX; ix++)
            for (int iz = 0; iz < m.countZ; iz++)
            {
                int i = ix * m.countZ + iz;
                float x = EchoBodyRules.FlightMinX + (ix + .5f) * CellSize, z = EchoBodyRules.FlightMinZ + (iz + .5f) * CellSize;
                var hits = Physics.RaycastAll(new Vector3(x, startY, z), Vector3.down, length, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => h.collider != null && h.collider.gameObject.scene == scene).ToArray();
                m.floor[i] = EchoBodyRules.FlightMinY; m.top[i] = EchoBodyRules.FlightMaxY;
                if (hits.Length == 0) { m.noHit++; m.rows.Add($"{F(x)},{F(z)},0,{F(m.floor[i])},{F(m.top[i])},,,"); continue; }
                float floor = float.PositiveInfinity;
                foreach (var h in hits) if (h.normal.y > 0f && h.point.y < floor) floor = h.point.y;
                RaycastHit slab = default; bool hasSlab = false;
                float highest = hits.Max(h => h.point.y);
                foreach (var h in hits)
                    if (h.collider is BoxCollider && (!hasSlab || h.point.y > slab.point.y)) { slab = h; hasSlab = true; }
                if (hasSlab && highest > slab.point.y) m.skippedNonSlabTop++;
                if (float.IsPositiveInfinity(floor) || !hasSlab) { m.noSlab++; m.rows.Add($"{F(x)},{F(z)},{hits.Length},{F(m.floor[i])},{F(m.top[i])},,,"); continue; }
                float thickness = slab.collider.bounds.size.y, top = slab.point.y - thickness;
                if (top < floor) { m.inverted++; m.rows.Add($"{F(x)},{F(z)},{hits.Length},{F(m.floor[i])},{F(m.top[i])},{F(slab.point.y)},{F(thickness)},inverted"); continue; }
                m.floor[i] = floor; m.top[i] = top;
                string key = thickness.ToString("F3", CultureInfo.InvariantCulture);
                m.slabThickness[key] = m.slabThickness.TryGetValue(key, out int n) ? n + 1 : 1;
                m.rows.Add($"{F(x)},{F(z)},{hits.Length},{F(floor)},{F(top)},{F(slab.point.y)},{F(thickness)},{slab.collider.name.Replace(',', ' ')}");
            }
            return m;
        }

        private static void Report(Measured m, string verdict)
        {
            string thickness = string.Join(" ", m.slabThickness.OrderByDescending(p => p.Value).Select(p => $"{p.Key}m×{p.Value}"));
            Debug.Log($"[EchoHeightGridBaker] {verdict} cells={m.floor.Length} ({m.countX}x{m.countZ}, {CellSize} m) noHit={m.noHit} noSlab={m.noSlab} " +
                      $"inverted={m.inverted} nonSlabAboveSlab={m.skippedNonSlabTop} slabThickness: {thickness} bytes={m.floor.Length * 2 * sizeof(float)}");
            string path = Arg("-echoGridReport");
            if (!string.IsNullOrEmpty(path)) File.WriteAllLines(path, m.rows, new UTF8Encoding(false));
        }

        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static string GitHead()
        {
            try
            {
                var p = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
                    {
                        WorkingDirectory = Directory.GetCurrentDirectory(), RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                    }
                };
                p.Start(); string sha = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit();
                return sha;
            }
            catch (Exception) { return "unknown"; }
        }
    }
}
