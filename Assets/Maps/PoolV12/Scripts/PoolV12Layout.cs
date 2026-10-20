using System;
using UnityEngine;

[Serializable] public class PoolV12Rect
{
    public float x0, z0, x1, z1;
    public float Width => x1 - x0;
    public float Depth => z1 - z0;
    public bool Contains(float x, float z) => x >= x0 && x < x1 && z >= z0 && z < z1;
    public Vector3 Center(float y) => new Vector3((x0 + x1) / 2, y, (z0 + z1) / 2);
}
[Serializable] public class PoolV12Room
{
    public string id, name, floorMaterial, landmark;
    public int floor;
    public float ceiling, area;
    public PoolV12Rect[] rects;
}
[Serializable] public class PoolV12Door
{
    public string id, name, kind;
    public string[] connections;
    public int floor;
    public float x, z, width;
    public bool alongX;
}
[Serializable] public class PoolV12Stair
{
    public string id, name;
    public PoolV12Rect rect;
    public float rise, run, width;
    public bool alongX;
}
[Serializable] public class PoolV12Point
{
    public string id, name, zone;
    public int floor;
    public float x, z, height;
    public Vector3 Position => new Vector3(x, height, z);
}
[Serializable] public class PoolV12Obstacle
{
    public string id, name, zone;
    public int floor;
    public PoolV12Rect rect;
    public float height;
    public bool occludes, stepped;
}
[Serializable] public class PoolV12Layout
{
    public string version;
    public float width, depth, upperFloor;
    public PoolV12Room[] rooms, voids;
    public PoolV12Door[] doors;
    public PoolV12Stair[] stairs;
    public PoolV12Point[] points;
    public PoolV12Obstacle[] obstacles;
}
