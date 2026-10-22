using UnityEngine;

namespace Marco.Network.Vision
{
    /// <summary>F8 is compiled only for Editor and Development builds.</summary>
    public sealed class DevelopmentVisionCheat : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private MicrophoneVisionController vision;
        private void Start() { vision = GetComponent<MicrophoneVisionController>(); }
        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F8)) return;
            bool enabledVision = !vision.DevelopmentFullVision; vision.SetDevelopmentFullVision(enabledVision);
            Debug.Log("Development full vision: " + (enabledVision ? "ON" : "OFF"));
        }
        private void OnGUI()
        {
            if (vision == null) return;
            GUI.Label(new Rect(Screen.width - 390, 25, 355, 28), vision.DevelopmentFullVision
                ? "[DEV] 전체 시야 ON · F8로 해제" : "[DEV] F8 전체 시야 켜기");
        }
#endif
    }
}
