using System;
using UnityEngine;

[Serializable] public class PoolV11Rect
{
    public float x0, z0, x1, z1;
    public float Width => x1 - x0;
    public float Depth => z1 - z0;
    public bool Contains(float x, float z) => x >= x0 && x < x1 && z >= z0 && z < z1;
    public Vector3 Center(float y) => new Vector3((x0 + x1) / 2, y, (z0 + z1) / 2);
}
[Serializable] public class PoolV11Room
{
    public string id, name, floorMaterial, landmark;
    public int floor;
    public float ceiling, area;
    public PoolV11Rect[] rects;
}
[Serializable] public class PoolV11Door
{
    public string id, name, kind;
    public string[] connections;
    public int floor;
    public float x, z, width;
    public bool alongX;
}
[Serializable] public class PoolV11Stair
{
    public string id, name;
    public PoolV11Rect rect;
    public float rise, run, width;
    public bool alongX;
}
[Serializable] public class PoolV11Point
{
    public string id, name, zone;
    public int floor;
    public float x, z, height;
    public Vector3 Position => new Vector3(x, height, z);
}
[Serializable] public class PoolV11Obstacle
{
    public string id, name, zone;
    public int floor;
    public PoolV11Rect rect;
    public float height;
    public bool occludes, stepped;
}
[Serializable] public class PoolV11Layout
{
    public string version;
    public float width, depth, upperFloor;
    public PoolV11Room[] rooms, voids;
    public PoolV11Door[] doors;
    public PoolV11Stair[] stairs;
    public PoolV11Point[] points;
    public PoolV11Obstacle[] obstacles;
}
