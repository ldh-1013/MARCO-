using UnityEngine;
using UnityEngine.InputSystem;

// A map-inspection camera, not a player controller. Fly mode intentionally passes through geometry.
public sealed class PoolV12Viewer : MonoBehaviour
{
    public Transform firstBuildings, secondBuildings, firstProps, secondProps;
    public Transform firstLabels, secondLabels, firstPoints, secondPoints;
    public GameObject roofs;
    public GameObject[] pools;
    public Camera viewCamera;
    public Vector3 target = new Vector3(32, 0, 31);
    public float cutHeight = 2.2f;
    public bool showLabels = true, showPoints = true, flyMode;
    public int visibleFloor;
    public float flySpeed = 8;
    float yaw, pitch = 52, distance = 95;
    Vector3 initialPosition;
    Quaternion initialRotation;

    void Awake()
    {
        if (!Application.isPlaying || viewCamera == null) return;
        initialPosition = viewCamera.transform.position;
        initialRotation = viewCamera.transform.rotation;
        Vector3 angles = initialRotation.eulerAngles;
        pitch = angles.x; yaw = angles.y;
        distance = Vector3.Distance(initialPosition, target);
        ApplyView();
    }

    public void ApplyView()
    {
        if (firstBuildings == null) return;
        bool first = visibleFloor != 2, second = visibleFloor != 1;
        firstBuildings.gameObject.SetActive(first); secondBuildings.gameObject.SetActive(second);
        firstProps.gameObject.SetActive(first); secondProps.gameObject.SetActive(second);
        firstLabels.gameObject.SetActive(first && showLabels); secondLabels.gameObject.SetActive(second && showLabels);
        firstPoints.gameObject.SetActive(first && showPoints); secondPoints.gameObject.SetActive(second && showPoints);
        if (pools != null) foreach (var pool in pools) pool.SetActive(first);
        Shader.SetGlobalFloat("_PoolV12CutHeight", cutHeight);
    }

    void Update()
    {
        var mouse = Mouse.current; var keys = Keyboard.current;
        if (mouse == null || keys == null) return;
        if (keys.escapeKey.wasPressedThisFrame) flyMode = false;
        if (mouse.position.ReadValue().x < 245 && mouse.position.ReadValue().y > Screen.height - 290) return;
        if (mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * .15f; pitch = Mathf.Clamp(pitch - delta.y * .15f, -85, 85);
            viewCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            if (flyMode)
            {
                Vector3 move = new Vector3((keys.dKey.isPressed ? 1 : 0) - (keys.aKey.isPressed ? 1 : 0),
                    (keys.eKey.isPressed ? 1 : 0) - (keys.qKey.isPressed ? 1 : 0),
                    (keys.wKey.isPressed ? 1 : 0) - (keys.sKey.isPressed ? 1 : 0));
                viewCamera.transform.position += viewCamera.transform.TransformDirection(move.normalized) * flySpeed *
                    (keys.leftShiftKey.isPressed ? 3 : 1) * Time.unscaledDeltaTime;
            }
        }
        if (!flyMode)
        {
            distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * .025f, 15, 180);
            viewCamera.transform.SetPositionAndRotation(target - Quaternion.Euler(pitch, yaw, 0) * Vector3.forward * distance,
                Quaternion.Euler(pitch, yaw, 0));
        }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, 12, 230, 280), GUI.skin.box);
        GUILayout.Label("INDOOR POOL / v1.2");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1F")) { visibleFloor = 1; ApplyView(); }
        if (GUILayout.Button("2F")) { visibleFloor = 2; ApplyView(); }
        if (GUILayout.Button("ALL")) { visibleFloor = 0; ApplyView(); }
        GUILayout.EndHorizontal();
        GUILayout.Label("Wall view height (0 = full)");
        float height = GUILayout.HorizontalSlider(cutHeight, 0, 9);
        bool labels = GUILayout.Toggle(showLabels, "Room labels");
        bool points = GUILayout.Toggle(showPoints, "Valves / exits / item markers");
        if (height != cutHeight || labels != showLabels || points != showPoints)
        { cutHeight = height; showLabels = labels; showPoints = points; ApplyView(); }
        if (GUILayout.Button(flyMode ? "Orbit view" : "Explore inside (fly)"))
        {
            flyMode = !flyMode;
            if (flyMode)
            {
                viewCamera.orthographic = false;
                viewCamera.transform.SetPositionAndRotation(new Vector3(31, 1.62f, 6.5f), Quaternion.identity);
                yaw = 0; pitch = 0; cutHeight = 0; ApplyView();
            }
            else
            {
                pitch = initialRotation.eulerAngles.x; yaw = initialRotation.eulerAngles.y;
                cutHeight = 2.2f; ApplyView();
            }
        }
        if (GUILayout.Button("Reset view"))
        {
            flyMode = false; pitch = initialRotation.eulerAngles.x; yaw = initialRotation.eulerAngles.y;
            viewCamera.transform.SetPositionAndRotation(initialPosition, initialRotation);
            visibleFloor = 0; cutHeight = 2.2f; ApplyView();
        }
        GUILayout.Label(flyMode ? "Hold RMB: look + WASD\nQ/E: down/up; Shift: faster\nPreview only: no collision/swimming" :
            "Hold RMB: orbit / wheel: zoom\nGeometry and collisions keep full size.");
        GUILayout.EndArea();
    }
}
