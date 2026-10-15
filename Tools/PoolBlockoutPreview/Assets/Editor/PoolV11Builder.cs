using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class PoolV11Builder
{
    public const string ScenePath = "Assets/Scenes/IndoorPool_v1_1.unity";
    const string Root = "Assets/PoolV11";
    const float Cell = .5f;
    static PoolV11Layout layout;
    static string generated;
    static readonly Dictionary<string, GameObject> modules = new Dictionary<string, GameObject>();
    static readonly Dictionary<string, Transform> roomNodes = new Dictionary<string, Transform>();
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    static Transform environment, buildings, props, collision, gamePoints, spawnPoints, labels, roofs;
    static Transform[] buildingFloors, propFloors, pointFloors, labelFloors;
    static PoolV11Viewer viewer;
    static readonly List<string> checks = new List<string>();

    [MenuItem("Tools/Indoor Pool v1.1/Create Exact Map")]
    public static void CreateMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build(AssetDatabase.GenerateUniqueAssetPath(ScenePath));
    }

    public static void BuildBatch()
    {
        if (File.Exists(ScenePath)) throw new InvalidOperationException("Refusing to overwrite an existing scene. Use Create Exact Map for a new copy.");
        Build(ScenePath);
    }

    static void Build(string scenePath)
    {
        layout = JsonUtility.FromJson<PoolV11Layout>(File.ReadAllText(Root + "/Design/Layout_v1_1.json"));
        modules.Clear(); materials.Clear(); roomNodes.Clear(); checks.Clear();
        generated = AssetDatabase.GenerateUniqueAssetPath(Root + "/Generated");
        Directory.CreateDirectory(generated + "/Prefabs");
        Directory.CreateDirectory(generated + "/Meshes");
        Directory.CreateDirectory(Root + "/Materials");
        AssetDatabase.Refresh();
        int rendererIndex = Find3DRenderer();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        environment = Group("Environment"); buildings = Group("Buildings"); props = Group("Obstacles");
        collision = Group("Collision"); gamePoints = Group("GamePoints"); spawnPoints = Group("SpawnPoints"); labels = Group("Labels");
        roofs = Group("Roofs_Optional", environment);
        buildingFloors = Floors(buildings); propFloors = Floors(props); pointFloors = Floors(gamePoints); labelFloors = Floors(labels);
        CreateMaterials();
        CreateRooms();
        BuildFloor(1); BuildFloor(2);
        BuildWalls(1); BuildWalls(2);
        foreach (var corner in new[] { new Vector3(-.15f, 4.5f, -.15f), new Vector3(64.15f, 3.5f, -.15f), new Vector3(-.15f, 9, 62.15f), new Vector3(64.15f, 5, 62.15f) })
            WorldBox("ExteriorCorner", buildingFloors[0], new Vector3(corner.x, 0, corner.z), new Vector3(.3f, corner.y, .3f), materials["Wall"], "Wall", 1, structural: true);
        foreach (var door in layout.doors) CreateDoor(door);
        foreach (var stair in layout.stairs) CreateStair(stair);
        BuildPools();
        CreateWaterVolumes();
        foreach (var obstacle in layout.obstacles) CreateObstacle(obstacle);
        AddLandmarkDetails();
        foreach (var point in layout.points) CreatePoint(point);
        CreateExitDoors();
        CreateSpawnPoints();
        CreateViewer(rendererIndex);
        roofs.gameObject.SetActive(false);
        Physics.SyncTransforms();
        ValidateScene();
        EditorSceneManager.SaveScene(scene, scenePath);
        AssetDatabase.SaveAssets();
        CaptureViews();
        EditorSceneManager.SaveScene(scene, scenePath);
        WriteReports(scenePath);
        Debug.Log("POOL_V11_BUILD_PASS: " + scenePath);
        if (!Application.isBatchMode && SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(32, 0, 31), Quaternion.Euler(55, 0, 0), 55);
    }

    static Transform Group(string name, Transform parent = null)
    {
        var node = new GameObject(name).transform;
        node.SetParent(parent, false);
        return node;
    }

    static Transform[] Floors(Transform parent) => new[] { Group("Floor_1F", parent), Group("Floor_2F", parent) };
    static float FloorY(int floor) => floor == 2 ? 3.5f : 0;

    static PoolV11Part Describe(GameObject go, string id, string name, string category, int floor, string zone = "")
    {
        var info = go.GetComponent<PoolV11Part>() ?? go.AddComponent<PoolV11Part>();
        info.designId = id; info.displayName = name; info.category = category; info.floor = floor; info.zone = zone;
        info.designPosition = go.transform.position;
        return info;
    }

    static Material Mat(string key, Color color, string shader = "PoolV11/BlockoutSurface", bool cut = false)
    {
        string path = Root + "/Materials/" + key + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var found = Shader.Find(shader);
            if (found == null) throw new InvalidOperationException("Missing shader " + shader);
            material = new Material(found) { name = key };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_UseCut")) material.SetFloat("_UseCut", cut ? 1 : 0);
            AssetDatabase.CreateAsset(material, path);
        }
        materials[key] = material;
        return material;
    }

    static void CreateMaterials()
    {
        Mat("Carpet", new Color(.87f, .85f, .71f)); Mat("Mat", new Color(.68f, .79f, .70f));
        Mat("Tile", new Color(.83f, .91f, .91f)); Mat("Concrete", new Color(.76f, .69f, .49f));
        Mat("Grating", new Color(.54f, .61f, .63f)); Mat("UpperCarpet", new Color(.69f, .65f, .82f));
        Mat("Wall", new Color(.88f, .91f, .89f), cut: true);
        Mat("Equipment", new Color(.35f, .44f, .49f)); Mat("Furniture", new Color(.79f, .73f, .49f));
        Mat("Rail", new Color(.53f, .61f, .65f)); Mat("Valve", new Color(.83f, .17f, .14f));
        Mat("Exit", new Color(.12f, .65f, .35f)); Mat("Drain", new Color(.22f, .24f, .27f));
        Mat("Marker", new Color(.90f, .59f, .18f)); Mat("Water", new Color(.08f, .69f, .86f, .45f), "PoolV11/WaterPreview");
        Mat("Label", Color.black, "Blockout/WorldLabel");
    }

    static Material FloorMaterial(PoolV11Room room)
    {
        if (room.floorMaterial == "카펫") return materials[room.floor == 2 ? "UpperCarpet" : "Carpet"];
        if (room.floorMaterial == "매트") return materials["Mat"];
        if (room.floorMaterial == "그레이팅") return materials["Grating"];
        if (room.floorMaterial == "타일") return materials["Tile"];
        return materials["Concrete"];
    }

    static GameObject Module(string key, Action<Transform> author)
    {
        if (modules.TryGetValue(key, out var cached)) return cached;
        var root = new GameObject(key);
        author(root.transform);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, generated + "/Prefabs/" + key + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
        modules.Add(key, prefab);
        return prefab;
    }

    static GameObject Place(GameObject prefab, string name, Transform parent, Vector3 position, Vector3 scale, Quaternion rotation)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position, rotation); go.transform.localScale = scale;
        return go;
    }

    static GameObject Primitive(string name, Transform parent, Vector3 localPosition, Vector3 size, Material material,
        PrimitiveType type = PrimitiveType.Cube)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = localPosition; go.transform.localScale = size;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    static BoxCollider Solid(Transform parent, Vector3 center, Vector3 size)
    {
        var solid = Group("Collision", parent).gameObject.AddComponent<BoxCollider>();
        solid.center = center; solid.size = size; return solid;
    }

    static GameObject BoxPrefab(string key, Material material)
    {
        return Module(key, root =>
        {
            Primitive("Visual_ReplaceWithFBX", root, Vector3.up * .5f, Vector3.one, material);
            Solid(root, Vector3.up * .5f, Vector3.one);
        });
    }

    static GameObject WorldBox(string id, Transform parent, Vector3 bottomCenter, Vector3 size, Material material, string category,
        int floor, string zone = "", bool structural = false)
    {
        var go = Place(BoxPrefab(category + "_" + material.name, material), id, parent, bottomCenter, size, Quaternion.identity);
        var info = Describe(go, id, id, category, floor, zone);
        info.designSize = size; info.soundOccluder = structural; info.tagBlocker = structural;
        if (material == materials["Wall"]) SetWallBase(go, FloorY(floor));
        return go;
    }

    static void SetWallBase(GameObject go, float height)
    {
        foreach (var renderer in go.GetComponentsInChildren<Renderer>())
        {
            var block = new MaterialPropertyBlock(); block.SetFloat("_WallBase", height); renderer.SetPropertyBlock(block);
        }
    }

    static void CreateRooms()
    {
        foreach (var room in layout.rooms.Concat(layout.voids))
        {
            var node = Group(room.id + "_" + room.name, buildingFloors[room.floor - 1]);
            node.position = room.rects[0].Center(FloorY(room.floor));
            var info = Describe(node.gameObject, room.id, room.name, room.id.StartsWith("PS") ? "ServiceVoid" : "Room", room.floor);
            info.designArea = room.area; info.note = "Source §5; " + JsonUtility.ToJson(room);
            roomNodes.Add(room.id, node);
            if (!room.id.StartsWith("PS"))
            {
                Vector3 labelPosition = room.rects[0].Center(FloorY(room.floor) + .04f);
                float width = Mathf.Min(room.rects[0].Width * .78f, 18), depth = Mathf.Min(room.rects[0].Depth * .5f, 2);
                string caption = room.id + "\n" + EnglishName(room.id);
                if (room.id == "H") { labelPosition = new Vector3(23, .04f, 61); caption = "MAIN POOL HALL"; width = 16; depth = .7f; }
                if (room.id == "ST") { labelPosition = new Vector3(27.5f, 3.54f, 33.2f); caption = "ST / SEATING"; width = 16; depth = .65f; }
                if (room.id == "C") depth = 1.2f;
                AddLabel(caption, labelPosition, labelFloors[room.floor - 1], room.floor, room.id, width, depth);
            }
        }
    }

    static string EnglishName(string id)
    {
        string[] ids = { "LOB", "OF1", "OF2", "OF3", "OF4", "FA", "LG", "ML", "WL", "MS", "WS", "H", "SC", "ST2", "A", "DR", "LD", "C", "CL", "ST", "L2" };
        string[] names = { "LOBBY", "OFFICE", "DUTY ROOM", "STAFF LOUNGE", "WORKSHOP", "FIRST AID", "LIFEGUARD", "LOCKER L", "LOCKER R", "SHOWER L", "SHOWER R", "MAIN POOL HALL", "STAFF ROUTE", "STAIR S2", "MACHINE ROOM", "CHEMICAL STORE", "LOADING BAY", "WATER TANKS", "UPPER LOBBY", "SPECTATOR SEATING", "STAFF LANDING" };
        int index = Array.IndexOf(ids, id); return index >= 0 ? names[index] : id;
    }

    static PoolV11Room[,] Grid(int floor)
    {
        var grid = new PoolV11Room[128, 124];
        foreach (var room in layout.rooms.Concat(layout.voids).Where(r => r.floor == floor))
        foreach (var rect in room.rects)
        for (int x = Mathf.RoundToInt(rect.x0 * 2); x < rect.x1 * 2; x++)
        for (int z = Mathf.RoundToInt(rect.z0 * 2); z < rect.z1 * 2; z++)
        {
            if (grid[x, z] != null && grid[x, z] != room) throw new InvalidOperationException("Overlapping design rooms.");
            grid[x, z] = room;
        }
        return grid;
    }

    static bool PoolCell(float x, float z) => (x >= 15 && x < 40 && z >= 37 && z < 58) || (x >= 3 && x < 11 && z >= 41 && z < 53);

    static void BuildFloor(int floor)
    {
        var grid = Grid(floor); var used = new bool[128, 124];
        bool Available(int x, int z, PoolV11Room room)
        {
            if (x < 0 || z < 0 || x >= 128 || z >= 124 || used[x, z] || grid[x, z] != room) return false;
            float px = (x + .5f) * Cell, pz = (z + .5f) * Cell;
            if (floor == 1 && (PoolCell(px, pz) || room.id.StartsWith("PS"))) return false;
            if (floor == 2 && layout.stairs.Any(s => s.rect.Contains(px, pz))) return false;
            return true;
        }
        for (int x = 0; x < 128; x++)
        for (int z = 0; z < 124; z++)
        {
            var room = grid[x, z];
            if (room == null || !Available(x, z, room)) continue;
            int width = 1, depth = 1;
            while (Available(x + width, z, room)) width++;
            while (z + depth < 124 && Enumerable.Range(x, width).All(px => Available(px, z + depth, room))) depth++;
            for (int px = x; px < x + width; px++) for (int pz = z; pz < z + depth; pz++) used[px, pz] = true;
            Vector3 position = new Vector3((x + width / 2f) * Cell, FloorY(floor) - .5f, (z + depth / 2f) * Cell);
            WorldBox("Floor_" + room.id + "_" + x + "_" + z, roomNodes[room.id], position,
                new Vector3(width * Cell, .5f, depth * Cell), FloorMaterial(room), "Floor", floor, room.id, true);
        }
        foreach (var room in layout.rooms.Where(r => r.floor == floor))
        foreach (var rect in room.rects)
            WorldBox("Roof_" + room.id, roofs, rect.Center(FloorY(floor) + room.ceiling),
                new Vector3(rect.Width, .2f, rect.Depth), materials["Tile"], "Ceiling", floor, room.id, true);
        foreach (var shaft in layout.voids.Where(r => r.floor == floor))
        foreach (var rect in shaft.rects)
            WorldBox(shaft.id + "_SealedServiceSpace", roomNodes[shaft.id], rect.Center(0),
                new Vector3(rect.Width, 3, rect.Depth), materials["Equipment"], "NonWalkable", 1, shaft.id, true);
    }

    static IEnumerable<PoolV11Door> Openings(int floor)
    {
        foreach (var door in layout.doors.Where(d => d.floor == floor)) yield return door;
        if (floor == 1)
        {
            yield return new PoolV11Door { id = "ExitDoor_1", floor = 1, alongX = true, x = 55, z = 62, width = 3 };
            yield return new PoolV11Door { id = "ExitDoor_2", floor = 1, alongX = true, x = 23, z = 0, width = 2 };
        }
        else
        {
            yield return new PoolV11Door { id = "S1_UpperOpening", floor = 2, alongX = true, x = 27, z = 16, width = 3 };
            yield return new PoolV11Door { id = "S2_SlabOpening", floor = 2, alongX = true, x = 54.5f, z = 16, width = 3 };
            yield return new PoolV11Door { id = "S3_UpperOpening", floor = 2, alongX = false, x = 15, z = 32, width = 2 };
        }
    }

    static void BuildWalls(int floor)
    {
        var grid = Grid(floor);
        for (int direction = 0; direction < 2; direction++)
        {
            bool alongX = direction == 0;
            int fixedCount = alongX ? 124 : 128, runCount = alongX ? 128 : 124;
            for (int edge = 0; edge <= fixedCount; edge++)
            {
                int run = 0;
                while (run < runCount)
                {
                    PoolV11Room a = alongX ? (edge > 0 ? grid[run, edge - 1] : null) : (edge > 0 ? grid[edge - 1, run] : null);
                    PoolV11Room b = alongX ? (edge < 124 ? grid[run, edge] : null) : (edge < 128 ? grid[edge, run] : null);
                    if (a == b) { run++; continue; }
                    int end = run + 1;
                    while (end < runCount)
                    {
                        PoolV11Room nextA = alongX ? (edge > 0 ? grid[end, edge - 1] : null) : (edge > 0 ? grid[edge - 1, end] : null);
                        PoolV11Room nextB = alongX ? (edge < 124 ? grid[end, edge] : null) : (edge < 128 ? grid[edge, end] : null);
                        if (nextA != a || nextB != b) break;
                        end++;
                    }
                    var owner = a ?? b;
                    bool outer = floor == 1 && (edge == 0 || edge == fixedCount);
                    bool rail = floor == 2 && (a == null || b == null) && owner.id == "ST";
                    float height = rail ? 1.1f : floor == 2 ? 3 : outer ? Mathf.Max(3.5f, owner.ceiling) : 3.5f;
                    if (floor == 1 && !outer && (a?.id == "H" || b?.id == "H")) height = 9;
                    float thickness = outer ? .3f : .2f;
                    float coordinate = edge * Cell;
                    float offset = outer ? (edge == 0 ? -.15f : .15f) : 0;
                    WallSpan(alongX, coordinate, coordinate + offset, run * Cell, end * Cell, height, thickness,
                        owner, floor, rail);
                    run = end;
                }
            }
        }
    }

    static void WallSpan(bool alongX, float designLine, float physicalLine, float from, float to, float height,
        float thickness, PoolV11Room owner, int floor, bool rail)
    {
        var doors = Openings(floor).Where(d => d.alongX == alongX && Mathf.Abs((alongX ? d.z : d.x) - designLine) < .001f).ToArray();
        var ticks = new SortedSet<float> { from, to };
        if (floor == 1 && alongX && designLine == 31)
            foreach (float x in new[] { 15f, 40f }) if (x > from && x < to) ticks.Add(x);
        foreach (var door in doors)
        {
            float center = alongX ? door.x : door.z;
            if (center - door.width / 2 > from && center - door.width / 2 < to) ticks.Add(center - door.width / 2);
            if (center + door.width / 2 > from && center + door.width / 2 < to) ticks.Add(center + door.width / 2);
        }
        var values = ticks.ToArray();
        for (int i = 0; i < values.Length - 1; i++)
        {
            float mid = (values[i] + values[i + 1]) / 2;
            bool gap = doors.Any(d => Mathf.Abs(mid - (alongX ? d.x : d.z)) < d.width / 2 - .001f);
            if (rail && gap) continue;
            float baseY = FloorY(floor) + (gap ? 2.5f : 0);
            float fullHeight = floor == 1 && alongX && designLine == 31 && mid >= 15 && mid <= 40 ? 3.5f : height;
            float segmentHeight = fullHeight - (gap ? 2.5f : 0);
            if (segmentHeight <= 0) continue;
            Vector3 position = alongX ? new Vector3(mid, baseY, physicalLine) : new Vector3(physicalLine, baseY, mid);
            Vector3 size = alongX ? new Vector3(values[i + 1] - values[i], segmentHeight, thickness) : new Vector3(thickness, segmentHeight, values[i + 1] - values[i]);
            WorldBox((rail ? "Guardrail_" : gap ? "Lintel_" : "Wall_") + owner.id + "_" + (alongX ? "X" : "Z") + designLine + "_" + values[i],
                roomNodes[owner.id], position, size, materials[rail ? "Rail" : "Wall"], rail ? "Guardrail" : "Wall", floor, owner.id, !rail);
        }
    }

    static GameObject DoorPrefab(float width, bool exterior, bool shutter)
    {
        return Module((exterior ? "Exterior_" : "Interior_") + (shutter ? "Shutter" : "Doorframe") + "_" + width, root =>
        {
            float depth = exterior ? .3f : .2f;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 p = new Vector3(side * (width / 2 + .04f), 1.25f, 0), size = new Vector3(.08f, 2.5f, depth + .05f);
                Primitive("Jamb", root, p, size, materials["Rail"]); Solid(root, p, size);
            }
            Primitive("Header", root, new Vector3(0, 2.55f, 0), new Vector3(width + .16f, .1f, depth + .05f), materials["Rail"]);
            if (shutter)
                Primitive("RaisedShutter_NoBlockingLeaf", root, new Vector3(0, 2.85f, 0), new Vector3(width, .5f, .15f), materials["Equipment"]);
        });
    }

    static void CreateDoor(PoolV11Door door)
    {
        Vector3 position = new Vector3(door.x, FloorY(door.floor), door.z);
        var go = Place(DoorPrefab(door.width, false, false), door.id + "_" + door.name, buildingFloors[door.floor - 1], position,
            Vector3.one, Quaternion.Euler(0, door.alongX ? 0 : 90, 0));
        var part = Describe(go, door.id, door.name, "Door", door.floor);
        part.openingWidth = door.width; part.openingHeight = 2.5f; part.note = "Open blockout doorway; no gameplay door logic. Source §7.1.";
        AddLabel(door.id, position + Vector3.up * 2.62f, labelFloors[door.floor - 1], door.floor);
    }

    static Mesh SaveMesh(string name, List<Vector3> vertices, List<int> triangles)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, generated + "/Meshes/" + name + ".asset"); return mesh;
    }

    static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int start = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
    }

    static GameObject MeshPart(string name, Transform parent, Mesh mesh, Material material, bool solid)
    {
        var go = Group(name, parent).gameObject;
        go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = material;
        if (solid) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        return go;
    }

    static void CreateStair(PoolV11Stair stair)
    {
        var prefab = Module("Stair_" + stair.width + "m_Run6_Rise3p5", root =>
        {
            for (int i = 0; i < 24; i++)
            {
                float height = 3.5f * (i + 1) / 24;
                Primitive("Step_" + (i + 1), root, new Vector3(0, height / 2, (i + .5f) * .25f),
                    new Vector3(stair.width, height, .25f), materials["Grating"]);
            }
            var v = new List<Vector3>(); var t = new List<int>();
            float w = stair.width / 2;
            Quad(v, t, new Vector3(-w, 0, 0), new Vector3(-w, 3.5f, 6), new Vector3(w, 3.5f, 6), new Vector3(w, 0, 0));
            Quad(v, t, new Vector3(w, 0, 0), new Vector3(w, 3.5f, 6), new Vector3(w, 0, 6), new Vector3(w, 0, 0));
            Quad(v, t, new Vector3(-w, 0, 0), new Vector3(-w, 0, 6), new Vector3(-w, 3.5f, 6), new Vector3(-w, 0, 0));
            Quad(v, t, new Vector3(-w, 0, 6), new Vector3(w, 0, 6), new Vector3(w, 3.5f, 6), new Vector3(-w, 3.5f, 6));
            var mesh = SaveMesh("StairRamp_" + stair.width, v, t);
            var solid = Group("ContinuousRamp_Collision", root).gameObject.AddComponent<MeshCollider>(); solid.sharedMesh = mesh;
            foreach (float side in new[] { -1f, 1f })
            {
                for (int i = 0; i <= 6; i++)
                    Primitive("RailPost", root, new Vector3(side * (w - .05f), 3.5f * i / 6 + .55f, i), new Vector3(.08f, 1.1f, .08f), materials["Rail"]);
                var rail = Primitive("Handrail", root, new Vector3(side * (w - .05f), 2.85f, 3), new Vector3(.08f, .08f, Mathf.Sqrt(48.25f)), materials["Rail"]);
                rail.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(3.5f, 6) * Mathf.Rad2Deg, 0, 0);
                var railSolid = Group("Handrail_Collision", root);
                railSolid.localPosition = rail.transform.localPosition; railSolid.localRotation = rail.transform.localRotation;
                railSolid.gameObject.AddComponent<BoxCollider>().size = rail.transform.localScale;
            }
        });
        Vector3 origin = stair.alongX ? new Vector3(stair.rect.x0, 0, (stair.rect.z0 + stair.rect.z1) / 2) :
            new Vector3((stair.rect.x0 + stair.rect.x1) / 2, 0, stair.rect.z0);
        var go = Place(prefab, stair.id + "_" + stair.name, buildingFloors[0], origin, Vector3.one, Quaternion.Euler(0, stair.alongX ? 90 : 0, 0));
        var part = Describe(go, stair.id, stair.name, "Stair", 1);
        part.designSize = new Vector3(stair.width, 3.5f, 6); part.note = "Source §7.2; continuous collision slope = atan(3.5/6) = 30.2564 degrees.";
        AddLabel(stair.id, stair.rect.Center(3.9f), labelFloors[0], 1);
    }

    static void BuildPools()
    {
        BuildCompetitionPool(); BuildTeachingPool();
    }

    static void CreateWaterVolumes()
    {
        var parent = Group("WaterVolumes_MetadataOnly", collision);
        void Volume(string id, Vector3 center, Vector3 size, float radius = 0)
        {
            var go = Group(id, parent).gameObject; go.transform.position = center;
            var box = go.AddComponent<BoxCollider>(); box.size = size; box.isTrigger = true;
            var info = Describe(go, id, id, "WaterVolume", 1, "H"); info.designSize = size; info.radius = radius;
            info.note = radius > 0 ? "Circular sump metadata. Trigger is the enclosing box; a future water query must test XZ radius too." : "Placement volume only; no swimming logic.";
        }
        Volume("Competition_Shallow_1p8", new Vector3(24.5f, -.9f, 47.5f), new Vector3(19, 1.8f, 21));
        Volume("Competition_Deep_3p5", new Vector3(37, -1.75f, 47.5f), new Vector3(6, 3.5f, 21));
        Volume("Teaching_Shallow_1p2", new Vector3(7, -.6f, 47), new Vector3(8, 1.2f, 12));
        Volume("Teaching_CircularSump_3p5", new Vector3(7, -2.35f, 47), new Vector3(2.4f, 2.3f, 2.4f), 1.2f);
    }

    static void BasinBox(string name, Transform parent, Vector3 bottomCenter, Vector3 size)
    {
        var visual = Primitive(name, parent, bottomCenter + Vector3.up * size.y / 2, size, materials["Tile"]);
        Solid(parent, bottomCenter + Vector3.up * size.y / 2, size);
    }

    static void BasinWalls(Transform root, float width, float depth, float height)
    {
        BasinBox("Wall_West", root, new Vector3(-width / 2 - .1f, -height, 0), new Vector3(.2f, height, depth));
        BasinBox("Wall_East", root, new Vector3(width / 2 + .1f, -height, 0), new Vector3(.2f, height, depth));
        BasinBox("Wall_South", root, new Vector3(0, -height, -depth / 2 - .1f), new Vector3(width, height, .2f));
        BasinBox("Wall_North", root, new Vector3(0, -height, depth / 2 + .1f), new Vector3(width, height, .2f));
    }

    static void WaterPlane(Transform root, float width, float depth)
    {
        var plane = Primitive("WaterSurface_Plane", root, Vector3.zero, new Vector3(width / 10, 1, depth / 10), materials["Water"], PrimitiveType.Plane);
        plane.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    static void BuildCompetitionPool()
    {
        var prefab = Module("CompetitionPool_25x21_Depth1p8_3p5", root =>
        {
            BasinWalls(root, 25, 21, 3.5f);
            BasinBox("ShallowFloor_19m_Depth1p8", root, new Vector3(-3, -2, 0), new Vector3(19, .2f, 21));
            BasinBox("DeepFloor_East6m_Depth3p5", root, new Vector3(9.5f, -3.7f, 0), new Vector3(6, .2f, 21));
            BasinBox("DepthTransition_Wall", root, new Vector3(6.4f, -3.5f, 0), new Vector3(.2f, 1.7f, 21));
            WaterPlane(root, 25, 21);
            for (int i = 1; i < 8; i++)
                Primitive("LaneRope_" + i, root, new Vector3(0, .025f, -10.5f + .5f + i * 2.5f), new Vector3(25, .05f, .05f), materials["Furniture"]);
        });
        var go = Place(prefab, "CompetitionPool_25x21", environment, new Vector3(27.5f, 0, 47.5f), Vector3.one, Quaternion.identity);
        var part = Describe(go, "CompetitionPool", "경영풀", "Pool", 1, "H"); part.designSize = new Vector3(25, 3.5f, 21);
        part.soundOccluder = true; part.tagBlocker = true; part.note = "Water Y=0; bottom Y=-1.8 west of X34, Y=-3.5 east of X34. Side walls retained.";
        AddLabel("25m / 8 LANES", new Vector3(26, .08f, 47.5f), labelFloors[0], 1, maxWidth: 12, maxDepth: 1);
    }

    static void BuildTeachingPool()
    {
        var prefab = Module("TeachingPool_8x12_CircularSump_R1p2", root =>
        {
            BasinWalls(root, 8, 12, 1.2f);
            var angles = new SortedSet<float>();
            for (int i = 0; i < 96; i++) angles.Add(i * Mathf.PI * 2 / 96);
            foreach (float x in new[] { -4f, 4f }) foreach (float z in new[] { -6f, 6f })
                angles.Add((Mathf.Atan2(z, x) + Mathf.PI * 2) % (Mathf.PI * 2));
            var a = angles.ToArray(); var v = new List<Vector3>(); var t = new List<int>();
            var wallV = new List<Vector3>(); var wallT = new List<int>(); var floorV = new List<Vector3>(); var floorT = new List<int>();
            Vector3 Inner(float angle, float y) => new Vector3(Mathf.Cos(angle) * 1.2f, y, Mathf.Sin(angle) * 1.2f);
            Vector3 Outer(float angle)
            {
                float x = Mathf.Cos(angle), z = Mathf.Sin(angle);
                float radius = Mathf.Min(Mathf.Abs(x) < .00001f ? 100000 : 4 / Mathf.Abs(x), Mathf.Abs(z) < .00001f ? 100000 : 6 / Mathf.Abs(z));
                return new Vector3(x * radius, -1.2f, z * radius);
            }
            for (int i = 0; i < a.Length; i++)
            {
                float first = a[i], second = a[(i + 1) % a.Length];
                Quad(v, t, Inner(first, -1.2f), Inner(second, -1.2f), Outer(second), Outer(first));
                Quad(wallV, wallT, Inner(first, -3.5f), Inner(second, -3.5f), Inner(second, -1.2f), Inner(first, -1.2f));
                int start = floorV.Count; floorV.Add(new Vector3(0, -3.5f, 0)); floorV.Add(Inner(second, -3.5f)); floorV.Add(Inner(first, -3.5f));
                floorT.AddRange(new[] { start, start + 1, start + 2 });
            }
            MeshPart("ShallowFloor_ActualCircularHole", root, SaveMesh("TeachingPool_RingFloor", v, t), materials["Tile"], true);
            MeshPart("Sump_CircularWall", root, SaveMesh("TeachingPool_SumpWall", wallV, wallT), materials["Tile"], true);
            MeshPart("Sump_Bottom_Depth3p5", root, SaveMesh("TeachingPool_SumpBottom", floorV, floorT), materials["Tile"], true);
            WaterPlane(root, 8, 12);
        });
        var go = Place(prefab, "TeachingPool_8x12", environment, new Vector3(7, 0, 47), Vector3.one, Quaternion.identity);
        var part = Describe(go, "TeachingPool", "교습풀", "Pool", 1, "H"); part.designSize = new Vector3(8, 3.5f, 12);
        part.note = "Source §5: water Y=0, shallow Y=-1.2, circular sump R1.2 centered at (7,47), bottom Y=-3.5.";
        AddLabel("TEACHING\nPOOL", new Vector3(7, .08f, 47), labelFloors[0], 1, maxWidth: 5, maxDepth: 2);
    }

    static string Archetype(PoolV11Obstacle obstacle)
    {
        string name = obstacle.name;
        if (obstacle.occludes) return "Locker";
        if (name.Contains("벤치")) return "Bench";
        if (name.Contains("감시 의자")) return "LifeguardChair";
        if (name.Contains("저수조") || name.Contains("탱크")) return "Tank";
        if (name.Contains("여과기")) return "FilterBank";
        if (name.Contains("펌프")) return "PumpBank";
        if (name.Contains("선반")) return "Shelf";
        if (name.Contains("손잡이")) return "Handrail";
        if (name.Contains("팔레트")) return "Pallet";
        if (name.Contains("카운터") || name.Contains("안내데스크")) return "Counter";
        if (name.Contains("책상") || name.Contains("식탁") || name.Contains("작업대")) return "Table";
        if (name.Contains("출발대")) return "StartingBlock";
        if (name.Contains("샤워 부스")) return "ShowerBooths";
        if (name.Contains("로프 릴")) return "RopeReel";
        if (name.Contains("화분")) return "Planter";
        if (name.Contains("침대")) return "Bed";
        if (name.Contains("스탠드") || name.Contains("거치대")) return "EquipmentRack";
        return "Cabinet";
    }

    static GameObject PropPrefab(string type)
    {
        return Module("Prop_" + type, root =>
        {
            var visual = Group("Visual_ReplaceWithFBX", root);
            Material mat = materials[type == "Locker" || type == "Tank" || type == "FilterBank" || type == "Cabinet" ? "Equipment" : "Furniture"];
            void Box(string name, Vector3 p, Vector3 size) => Primitive(name, visual, p, size, mat);
            if (type == "Bench" || type == "Table" || type == "StartingBlock" || type == "Bed")
            {
                Box("Top", new Vector3(0, .9f, 0), new Vector3(1, .2f, 1));
                foreach (float x in new[] { -.4f, .4f }) foreach (float z in new[] { -.4f, .4f })
                    Box("Leg", new Vector3(x, .4f, z), new Vector3(.12f, .8f, .12f));
            }
            else if (type == "Shelf" || type == "EquipmentRack")
            {
                foreach (float y in new[] { .05f, .5f, .95f }) Box("Shelf", new Vector3(0, y, 0), new Vector3(1, .1f, 1));
                foreach (float x in new[] { -.46f, .46f }) Box("Upright", new Vector3(x, .5f, .4f), new Vector3(.08f, 1, .15f));
            }
            else if (type == "Handrail" || type == "LifeguardChair")
            {
                foreach (float x in new[] { -.4f, .4f }) Box("Upright", new Vector3(x, .5f, 0), new Vector3(.15f, 1, .2f));
                Box("Top", new Vector3(0, .92f, 0), new Vector3(1, .16f, 1));
            }
            else if (type == "Tank" || type == "Planter" || type == "RopeReel")
                Primitive("Cylinder", visual, new Vector3(0, .5f, 0), new Vector3(1, .5f, 1), mat, PrimitiveType.Cylinder);
            else if (type == "FilterBank" || type == "PumpBank")
            {
                for (int i = 0; i < 3; i++) Primitive("Machine_" + i, visual, new Vector3(0, .5f, -.33f + i * .33f),
                    new Vector3(.8f, .5f, .28f), mat, PrimitiveType.Cylinder);
                Box("HeaderPipe", new Vector3(-.45f, .75f, 0), new Vector3(.1f, .1f, 1));
            }
            else if (type == "ShowerBooths")
            {
                Box("BackPanel", new Vector3(-.45f, .5f, 0), new Vector3(.1f, 1, 1));
                for (int i = 0; i <= 3; i++) Box("Partition", new Vector3(0, .5f, -.48f + .32f * i), new Vector3(1, 1, .04f));
            }
            else if (type == "Pallet")
            {
                for (int i = 0; i < 5; i++) Box("Slat", new Vector3(-.4f + i * .2f, .9f, 0), new Vector3(.15f, .2f, 1));
                Box("Base", new Vector3(0, .4f, 0), new Vector3(1, .8f, 1));
            }
            else Box("Body", new Vector3(0, .5f, 0), Vector3.one);
            Solid(root, Vector3.up * .5f, Vector3.one);
        });
    }

    static void CreateObstacle(PoolV11Obstacle obstacle)
    {
        if (obstacle.stepped)
        {
            var prefab = Module("SpectatorSeating_21x6p5_6Tiers", root =>
            {
                for (int i = 0; i < 6; i++)
                {
                    float h = 2.4f - i * .4f, depth = 6.5f / 6;
                    var position = new Vector3(0, h / 2, i * depth + depth / 2);
                    var size = new Vector3(21, h, depth);
                    Primitive("Tier_" + (i + 1), root, position, size, materials["UpperCarpet"]); Solid(root, position, size);
                }
            });
            var go = Place(prefab, obstacle.id + "_계단식좌석단", propFloors[1], new Vector3(27.5f, 3.5f, 26), Vector3.one, Quaternion.identity);
            var info = Describe(go, obstacle.id, obstacle.name, "Obstacle", 2, obstacle.zone);
            info.designSize = new Vector3(21, 2.4f, 6.5f); info.note = "Six tiers; no walkable aisles between seats. U-shaped perimeter route remains open.";
            return;
        }
        string type = Archetype(obstacle);
        var result = Place(PropPrefab(type), obstacle.id + "_" + obstacle.name, propFloors[obstacle.floor - 1],
            obstacle.rect.Center(FloorY(obstacle.floor)), new Vector3(obstacle.rect.Width, obstacle.height, obstacle.rect.Depth), Quaternion.identity);
        var part = Describe(result, obstacle.id, obstacle.name, "Obstacle", obstacle.floor, obstacle.zone);
        part.designSize = result.transform.localScale; part.soundOccluder = obstacle.occludes; part.tagBlocker = obstacle.occludes;
        part.note = "Source §9.3. Collider follows the canonical blocking rectangle; visual children can be replaced independently.";
    }

    static void AddLandmarkDetails()
    {
        // §5 mentions these landmarks but §9.3 gives no coordinates. Keep them out of the canonical blocking data.
        var details = Group("Supplementary_Landmarks_ApproximatePlacement", propFloors[0]);
        foreach (float z in new[] { 12f, 16f })
        {
            var go = Place(PropPrefab("Bed"), "FA_Bed_Approximate_" + z, details, new Vector3(1.5f, 0, z), new Vector3(2, .6f, 1), Quaternion.identity);
            foreach (var collider in go.GetComponentsInChildren<Collider>()) collider.enabled = false;
            var part = Describe(go, "FA_Bed_" + z, "응급 침대(임시 위치)", "SupplementaryVisual", 1, "FA");
            part.note = "Landmark only; source gives no exact coordinates. No collision; excluded from route/obstacle validation.";
        }
    }

    static GameObject ValvePrefab()
    {
        return Module("ValveHandle_Reusable", root =>
        {
            var wheel = Group("Visual_ReplaceWithFBX", root);
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI * 2 / 16;
                var segment = Primitive("WheelRim", wheel, new Vector3(Mathf.Cos(angle) * .30f, Mathf.Sin(angle) * .30f, 0),
                    new Vector3(.13f, .055f, .06f), materials["Valve"]);
                segment.transform.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg + 90);
            }
            Primitive("Spoke_X", wheel, Vector3.zero, new Vector3(.58f, .04f, .06f), materials["Valve"]);
            Primitive("Spoke_Y", wheel, Vector3.zero, new Vector3(.04f, .58f, .06f), materials["Valve"]);
            var trigger = Group("InteractionAnchor_NoGameplay", root).gameObject.AddComponent<SphereCollider>(); trigger.radius = .45f; trigger.isTrigger = true;
        });
    }

    static GameObject DrainPrefab()
    {
        return Module("DrainCover_Reusable", root =>
        {
            Primitive("Rim", root, new Vector3(0, .025f, 0), new Vector3(.85f, .025f, .85f), materials["Drain"], PrimitiveType.Cylinder);
            for (int i = 0; i < 5; i++)
                Primitive("GrateBar", root, new Vector3(-.28f + i * .14f, .06f, 0), new Vector3(.04f, .04f, .65f), materials["Rail"]);
            var trigger = Group("InteractionAnchor_NoGameplay", root).gameObject.AddComponent<SphereCollider>(); trigger.radius = .6f; trigger.isTrigger = true;
        });
    }

    static void CreatePoint(PoolV11Point point)
    {
        GameObject go;
        if (point.id.StartsWith("Valve_"))
            go = Place(ValvePrefab(), point.id, pointFloors[point.floor - 1], point.Position, Vector3.one, Quaternion.Euler(0, 90, 0));
        else if (point.id.StartsWith("Drain_"))
            go = Place(DrainPrefab(), point.id, pointFloors[0], point.Position, Vector3.one, Quaternion.identity);
        else if (point.id.StartsWith("Exit_"))
        {
            go = Group(point.id, pointFloors[0]).gameObject; go.transform.position = point.Position;
            var trigger = go.AddComponent<SphereCollider>(); trigger.radius = 2; trigger.isTrigger = true;
            Primitive("ExitMarker", go.transform, new Vector3(0, .015f, 0), new Vector3(1.2f, .03f, 1.2f), materials["Exit"]);
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI * 2 / 24;
                var segment = Primitive("ExitRing_Visual", go.transform,
                    new Vector3(Mathf.Cos(angle) * .7f, 1.15f + Mathf.Sin(angle) * .7f, 0),
                    new Vector3(.2f, .06f, .06f), materials["Exit"]);
                segment.transform.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg + 90);
            }
        }
        else
        {
            go = Group(point.id, pointFloors[point.floor - 1]).gameObject; go.transform.position = point.Position;
            if (point.id != "R" && point.id != "S")
                Primitive("CandidateMarker_NoSpawnLogic", go.transform, new Vector3(0, .08f, 0), new Vector3(.35f, .16f, .35f), materials["Marker"]);
        }
        var info = Describe(go, point.id, point.name, "GamePoint", point.floor, point.zone);
        if (point.id == "R")
        {
            int index = 0;
            foreach (float z in new[] { -.75f, .75f }) foreach (float x in new[] { -3.15f, -1.05f, 1.05f, 3.15f })
                Group("RunnerSlot_" + (++index), go.transform).localPosition = new Vector3(x, 0, z);
        }
        info.radius = point.id.StartsWith("Exit_") ? 2 : point.id.StartsWith("Drain_") ? .6f : point.id.StartsWith("Valve_") ? .45f : 0;
        info.note = "Source §9.1 exact (X,Y,Z)=(document x,document height,document y). Placement anchor only; no gameplay activation.";
        string label = point.id.Replace("Valve_", "VALVE ").Replace("Exit_", "EXIT ").Replace("Drain_", "DRAIN ");
        AddLabel(label, point.Position + Vector3.up * (point.id.StartsWith("Valve_") ? .55f : .15f), pointFloors[point.floor - 1], point.floor,
            maxWidth: point.id.StartsWith("Valve_") || point.id.StartsWith("Exit_") || point.id.StartsWith("Drain_") ? 2.5f : .65f, maxDepth: .5f);
    }

    static void CreateExitDoors()
    {
        foreach (int index in new[] { 1, 2 })
        {
            bool north = index == 1; float width = north ? 3 : 2;
            var position = new Vector3(north ? 55 : 23, 0, north ? 62.15f : -.15f);
            var door = Place(DoorPrefab(width, true, north), "ExitDoor_" + index, buildingFloors[0], position, Vector3.one, Quaternion.identity);
            var info = Describe(door, "ExitDoor_" + index, north ? "하역 셔터" : "정문 양개문", "ExternalDoor", 1);
            info.designPosition = new Vector3(north ? 55 : 23, 0, north ? 62 : 0);
            info.openingWidth = width; info.openingHeight = 2.5f;
            info.note = "Wall center is outside footprint by 0.15m; design opening center Z=" + (north ? 62 : 0);
            if (!north)
            {
                foreach (float side in new[] { -1f, 1f })
                {
                    var leaf = Primitive("DoubleDoorLeaf_Open_NoCollision", door.transform,
                        new Vector3(side * .95f, 1.2f, -.45f), new Vector3(.05f, 2.4f, .9f), materials["Exit"]);
                }
            }
        }
    }

    static void CreateSpawnPoints()
    {
        Vector3[] slots = { new Vector3(27.85f, 0, 5.75f), new Vector3(29.95f, 0, 5.75f), new Vector3(32.05f, 0, 5.75f), new Vector3(55, 3.5f, 18) };
        for (int i = 0; i < 4; i++)
        {
            var slot = Group("SpawnPoint_" + (i + 1), spawnPoints); slot.position = slots[i];
            AddLabel(i == 3 ? "S" : "R" + (i + 1), slots[i] + Vector3.up * .1f,
                labelFloors[i == 3 ? 1 : 0], i == 3 ? 2 : 1, maxWidth: .7f, maxDepth: .4f);
        }
    }

    static void AddLabel(string text, Vector3 position, Transform parent, int floor, string zone = "", float maxWidth = 1.5f, float maxDepth = .55f)
    {
        var go = Group("Label_" + text, parent).gameObject; go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        var mesh = go.AddComponent<TextMesh>(); mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mesh.text = text; mesh.fontSize = 48; mesh.characterSize = text.Length > 20 ? .19f : .24f;
        mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center; mesh.color = new Color(.08f, .12f, .16f);
        go.GetComponent<MeshRenderer>().sharedMaterial = materials["Label"]; go.AddComponent<WorldLabelFontBinding>();
        Bounds bounds = go.GetComponent<MeshRenderer>().bounds;
        if (bounds.size.x > .0001f && bounds.size.z > .0001f)
            go.transform.localScale = Vector3.one * Mathf.Min(1, maxWidth / bounds.size.x, maxDepth / bounds.size.z);
    }

    static int Find3DRenderer()
    {
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Blockout/Rendering/BlockoutRenderer3D.asset");
        if (pipeline == null || renderer == null) throw new InvalidOperationException("Existing project's 3D renderer not found.");
        var serialized = new SerializedObject(pipeline); var list = serialized.FindProperty("m_RendererDataList");
        for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == renderer) return i;
        throw new InvalidOperationException("3D renderer is not registered; preserving project settings.");
    }

    static void CreateViewer(int rendererIndex)
    {
        var cameraNode = Group("MapPreviewCamera", environment);
        var camera = cameraNode.gameObject.AddComponent<Camera>(); camera.tag = "MainCamera";
        camera.transform.position = new Vector3(49, 64, -45); camera.transform.LookAt(new Vector3(32, 0, 29));
        camera.orthographic = false; camera.fieldOfView = 44; camera.nearClipPlane = .05f; camera.farClipPlane = 300;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.78f, .83f, .85f);
        camera.GetUniversalAdditionalCameraData().SetRenderer(rendererIndex); camera.gameObject.AddComponent<AudioListener>();
        viewer = cameraNode.gameObject.AddComponent<PoolV11Viewer>(); viewer.viewCamera = camera;
        viewer.firstBuildings = buildingFloors[0]; viewer.secondBuildings = buildingFloors[1];
        viewer.firstProps = propFloors[0]; viewer.secondProps = propFloors[1];
        viewer.firstLabels = labelFloors[0]; viewer.secondLabels = labelFloors[1];
        viewer.firstPoints = pointFloors[0]; viewer.secondPoints = pointFloors[1]; viewer.roofs = roofs.gameObject;
        viewer.pools = environment.GetComponentsInChildren<PoolV11Part>().Where(p => p.category == "Pool").Select(p => p.gameObject).ToArray();
        viewer.ApplyView();
    }

    static bool CapsuleFree(Vector3 feet)
    {
        return !Physics.CheckCapsule(feet + Vector3.up * .40f, feet + Vector3.up * 1.45f, .35f, ~0, QueryTriggerInteraction.Ignore);
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks.Add("PASS: " + message);
    }

    static void ValidateScene()
    {
        Require(layout.rooms.Length == 21 && layout.rooms.Count(r => r.floor == 1) == 17, "21 rooms: 17 ground floor + 4 upper floor");
        var grid = Grid(1); int empty = 0;
        foreach (var cell in grid) if (cell == null) empty++;
        Require(empty == 0, "64 x 62m footprint is fully assigned to rooms or 3 sealed service spaces");
        Require(Mathf.Abs(layout.rooms.Where(r => r.floor == 1).Sum(r => r.area) + 124 - 3968) < .01f,
            "Ground-floor room area plus service spaces = 3968m2");
        Require(layout.doors.Length == 25 && layout.stairs.Length == 3 && layout.obstacles.Length == 82,
            "All 25 door entries, 3 stairs and 82 expanded obstacle entries imported");
        var parts = UnityEngine.Object.FindObjectsByType<PoolV11Part>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var room in layout.rooms)
            Require(parts.Count(p => p.category == "Room" && p.designId == room.id) == 1, "Room exists exactly once: " + room.id);
        foreach (var room in layout.rooms)
        {
            float expected = room.area - (room.id == "H" ? 621 : room.id == "L2" ? 1.5f : 0);
            float actual = parts.Where(p => p.category == "Floor" && p.zone == room.id).Sum(p =>
            {
                var bounds = p.GetComponentInChildren<BoxCollider>().bounds;
                return bounds.size.x * bounds.size.z;
            });
            Require(Mathf.Abs(expected - actual) < .01f, "Actual floor geometry area: " + room.id + " = " + actual + "m2");
        }
        foreach (var point in layout.points)
        {
            var part = parts.Single(p => p.category == "GamePoint" && p.designId == point.id);
            Require(Vector3.Distance(part.transform.position, point.Position) < .001f, "Exact point coordinate: " + point.id);
        }
        Require(parts.Count(p => p.category == "GamePoint" && p.designId.StartsWith("Valve_")) == 5, "5 reusable valve instances");
        Require(parts.Count(p => p.category == "GamePoint" && p.designId.StartsWith("Drain_")) == 2, "2 reusable drains at Y=-3.5");
        foreach (var door in layout.doors)
        {
            Vector3 center = new Vector3(door.x, FloorY(door.floor) + .05f, door.z);
            Require(CapsuleFree(center), "Door centre clearance for radius 0.35m / height 1.8m: " + door.id);
        }
        foreach (Transform point in spawnPoints)
        {
            Require(point.GetComponents<Component>().Length == 1 && CapsuleFree(point.position + Vector3.up * .05f), "Empty, clear spawn: " + point.name);
            Require(Physics.Raycast(point.position + Vector3.up * .25f, Vector3.down, .5f, ~0, QueryTriggerInteraction.Ignore), "Ground support: " + point.name);
        }
        foreach (var obstacle in layout.obstacles)
        {
            var part = parts.Single(p => p.category == "Obstacle" && p.designId == obstacle.id);
            Require(Vector3.Distance(part.designSize, new Vector3(obstacle.rect.Width, obstacle.height, obstacle.rect.Depth)) < .001f,
                "Canonical obstacle bounds: " + obstacle.id);
            Require(part.soundOccluder == obstacle.occludes && part.tagBlocker == obstacle.occludes,
                "Locker/other obstacle classification: " + obstacle.id);
            var solids = part.GetComponentsInChildren<Collider>().Where(c => !c.isTrigger).ToArray();
            Bounds bounds = solids[0].bounds;
            foreach (var solid in solids.Skip(1)) bounds.Encapsulate(solid.bounds);
            Require(Vector3.Distance(bounds.min, new Vector3(obstacle.rect.x0, FloorY(obstacle.floor), obstacle.rect.z0)) < .01f &&
                Vector3.Distance(bounds.max, new Vector3(obstacle.rect.x1, FloorY(obstacle.floor) + obstacle.height, obstacle.rect.z1)) < .01f,
                "Actual obstacle collider bounds: " + obstacle.id);
        }
        CheckDepth(20, 45, 1.8f); CheckDepth(36, 47.5f, 3.5f); CheckDepth(4, 44, 1.2f); CheckDepth(7, 47, 3.5f);
        CheckDepth(8.1f, 47, 3.5f); CheckDepth(8.3f, 47, 1.2f);
        var degrees = layout.rooms.ToDictionary(r => r.id, r => 0);
        foreach (var door in layout.doors) foreach (string id in door.connections) degrees[id]++;
        foreach (string id in new[] { "LOB", "CL", "ST2", "L2", "H", "ST" }) degrees[id]++;
        degrees["LOB"]++; degrees["LD"]++;
        Require(string.Join(",", degrees.Where(p => p.Value == 1).Select(p => p.Key).OrderBy(id => id)) == "A,C",
            "Only two dead-end regions in the design connection graph: A and C");
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) > 0) throw new InvalidOperationException("Missing script " + child.name);
        checks.Add("PASS: no missing scripts; no network or gameplay activation code added");
        ValidateConnectivity();
    }

    static void CheckDepth(float x, float z, float expected)
    {
        var hits = Physics.RaycastAll(new Vector3(x, .1f, z), Vector3.down, 5, ~0, QueryTriggerInteraction.Ignore);
        var floor = hits.Where(h => h.collider.GetComponentInParent<PoolV11Part>()?.category == "Pool").OrderBy(h => h.distance).FirstOrDefault();
        Require(floor.collider != null && Mathf.Abs(-floor.point.y - expected) < .02f,
            "Water depth at (" + x + "," + z + ") = " + expected + "m");
    }

    static void ValidateConnectivity()
    {
        foreach (int floor in new[] { 1, 2 })
        {
            Vector2 start = floor == 1 ? new Vector2(31, 6.5f) : new Vector2(32, 19);
            var tested = new HashSet<Vector2>(); var reached = new HashSet<Vector2>(); var pending = new Queue<Vector2>();
            pending.Enqueue(start); reached.Add(start); tested.Add(start);
            Vector2[] offsets = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };
            while (pending.Count > 0)
            {
                Vector2 current = pending.Dequeue();
                foreach (Vector2 offset in offsets)
                {
                    Vector2 next = current + offset * .5f;
                    if (next.x < .5f || next.x > 63.5f || next.y < .5f || next.y > 61.5f || !tested.Add(next)) continue;
                    Vector3 feet = new Vector3(next.x, FloorY(floor) + .05f, next.y);
                    if (!CapsuleFree(feet) || !CapsuleFree(new Vector3((current.x + next.x) / 2, feet.y, (current.y + next.y) / 2))) continue;
                    if (!Physics.Raycast(feet + Vector3.up * .15f, Vector3.down, out var hit, .3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(hit.point.y - FloorY(floor)) > .05f) continue;
                    reached.Add(next); pending.Enqueue(next);
                }
            }
            foreach (var room in layout.rooms.Where(r => r.floor == floor))
                Require(reached.Any(p => room.rects.Any(r => r.Contains(p.x, p.y))), "Connected floor route: " + room.id);
            foreach (var stair in layout.stairs)
            {
                Vector2 landing = stair.alongX
                    ? new Vector2(floor == 1 ? stair.rect.x0 - .5f : stair.rect.x1 + .5f, stair.rect.Center(0).z)
                    : new Vector2(stair.rect.Center(0).x, floor == 1 ? stair.rect.z0 - .5f : stair.rect.z1 + .5f);
                Require(reached.Contains(landing), "Stair landing connects to floor " + floor + ": " + stair.id);
            }
        }
        foreach (var stair in layout.stairs)
        {
            for (int i = 1; i < 10; i++)
            {
                float t = i / 10f;
                Vector3 point = stair.alongX ? new Vector3(stair.rect.x0 + stair.run * t, stair.rise * t, stair.rect.Center(0).z) :
                    new Vector3(stair.rect.Center(0).x, stair.rise * t, stair.rect.z0 + stair.run * t);
                Require(CapsuleFree(point + Vector3.up * .2f), "Stair headroom " + stair.id + " at " + t);
                Require(Physics.Raycast(point + Vector3.up * .3f, Vector3.down, out var support, .6f, ~0, QueryTriggerInteraction.Ignore) &&
                    support.collider.name == "ContinuousRamp_Collision" && Mathf.Abs(support.point.y - point.y) < .02f,
                    "Continuous stair collision support " + stair.id + " at " + t);
            }
        }
    }

    static void CaptureViews()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        Directory.CreateDirectory("PoolV11Reports");
        var target = new RenderTexture(1800, 1400, 24);
        try
        {
            SaveImage(target, "Overview");
            viewer.visibleFloor = 1; viewer.ApplyView(); SaveImage(target, "FirstFloor");
            environment.GetComponentsInChildren<PoolV11Part>().Where(p => p.category == "Pool").ToList().ForEach(p => p.gameObject.SetActive(false));
            viewer.visibleFloor = 2; viewer.ApplyView(); SaveImage(target, "SecondFloor");
            environment.GetComponentsInChildren<PoolV11Part>(true).Where(p => p.category == "Pool").ToList().ForEach(p => p.gameObject.SetActive(true));
        }
        finally
        {
            viewer.visibleFloor = 0; viewer.ApplyView(); viewer.viewCamera.targetTexture = null;
            RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(target);
        }
    }

    static void SaveImage(RenderTexture target, string name)
    {
        RenderPipeline.SubmitRenderRequest(viewer.viewCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        File.WriteAllBytes("PoolV11Reports/" + name + ".png", image.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(image);
    }

    static void WriteReports(string path)
    {
        Directory.CreateDirectory("PoolV11Reports");
        File.WriteAllLines("PoolV11Reports/Validation.txt", new[] { "Scene: " + path, "Source: Assets/PoolV11/Design/MapDesign_v1_1.md §§5,7,9", "Generated module folder: " + generated }.Concat(checks));
        File.WriteAllText("PoolV11Reports/Limitations.txt", "Geometry/coordinate/clearance validation only. Original document path lengths, acoustic coverage, valve audibility and match-balance metrics have not been reproduced. No gameplay activation, swimming, networking, or spawning logic. Supplementary first-aid beds are non-colliding approximate landmarks. Roofs hidden for inspection. Material cutaway affects visuals only; full wall collisions are retained.");
    }

    [MenuItem("Tools/Indoor Pool v1.1/Validate Saved Map")]
    public static void ValidateSavedMap()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
        layout = JsonUtility.FromJson<PoolV11Layout>(File.ReadAllText(Root + "/Design/Layout_v1_1.json"));
        spawnPoints = GameObject.Find("SpawnPoints").transform;
        Physics.SyncTransforms(); checks.Clear(); ValidateScene();
        ValidateVisualReplacement();
        Directory.CreateDirectory("PoolV11Reports"); File.WriteAllLines("PoolV11Reports/ReloadValidation.txt", checks);
        Debug.Log("POOL_V11_RELOAD_PASS");
    }

    static void ValidateVisualReplacement()
    {
        var pipe = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_ArchivedTests/Pipe/파이프.fbx");
        if (pipe == null) { checks.Add("SKIP: visual replacement sample FBX is not available"); return; }
        var part = UnityEngine.Object.FindObjectsByType<PoolV11Part>(FindObjectsSortMode.None).Single(p => p.designId == "o01" && p.category == "Obstacle");
        var original = part.GetComponentInChildren<MeshRenderer>();
        var collider = part.GetComponentInChildren<BoxCollider>(); Bounds before = collider.bounds;
        var replacement = PoolV11VisualReplacement.Replace(part, pipe);
        Bounds fitted = replacement.GetComponentsInChildren<Renderer>()[0].bounds;
        foreach (var renderer in replacement.GetComponentsInChildren<Renderer>().Skip(1)) fitted.Encapsulate(renderer.bounds);
        Require(!original.enabled && fitted.min.x >= before.min.x - .01f && fitted.max.x <= before.max.x + .01f &&
            fitted.min.y >= before.min.y - .01f && fitted.max.y <= before.max.y + .01f &&
            fitted.min.z >= before.min.z - .01f && fitted.max.z <= before.max.z + .01f,
            "FBX visual replacement fits uniformly inside the selected module");
        Require(Vector3.Distance(before.center, collider.bounds.center) < .001f && Vector3.Distance(before.size, collider.bounds.size) < .001f,
            "FBX replacement preserves original collision");
        Undo.PerformUndo();
        Require(original.enabled && replacement == null, "FBX visual replacement can be undone");
        // Do not save the temporary test edits. The published scene never references the sample FBX.
    }
}
