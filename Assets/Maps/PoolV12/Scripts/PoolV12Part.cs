using UnityEngine;

// Metadata only: no networking, activation, audio, or win-condition behaviour.
[ExecuteAlways]
public sealed class PoolV12Part : MonoBehaviour
{
    private static readonly int ClickerTerrainId = Shader.PropertyToID("_MarcoClickerTerrain");

    public string designId;
    public string displayName;
    public string category;
    public string zone;
    public int floor = 1;
    public Vector3 designPosition;
    public Vector3 designSize;
    public float designArea;
    public float openingWidth;
    public float openingHeight;
    public float radius;
    public bool soundOccluder;
    public bool tagBlocker;
    [TextArea] public string note;

    void OnEnable() => RestoreWallPreview();

    void RestoreWallPreview()
    {
        bool isWall = category == "Wall";
        bool isClickerTerrain = isWall || category == "Floor" || category == "Ceiling" ||
            category == "Stair" || category == "Guardrail" || category == "Room" ||
            category == "Door" || category == "ExternalDoor" || category == "Pool" ||
            category == "NonWalkable" || category == "ServiceVoid";
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            // Nested map parts own their own category; don't let a parent label override it.
            if (renderer.GetComponentInParent<PoolV12Part>(true) != this) continue;
            var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
            properties.SetFloat(ClickerTerrainId, isClickerTerrain ? 1f : 0f);
            if (isWall) properties.SetFloat("_WallBase", floor == 2 ? 3.5f : 0);
            renderer.SetPropertyBlock(properties);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (radius <= 0) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
