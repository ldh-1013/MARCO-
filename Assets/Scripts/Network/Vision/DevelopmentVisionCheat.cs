using UnityEngine;
using Marco.Network.Player;

namespace Marco.Network.Vision
{
    /// <summary>Vision debug shortcuts are compiled only for Editor and Development builds.</summary>
    public sealed class DevelopmentVisionCheat : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private MicrophoneVisionController vision;

        private void Start()
        {
            vision = GetComponent<MicrophoneVisionController>();
        }

        private void Update()
        {
            if (vision == null) return;
            if (Input.GetKeyDown(KeyCode.F7))
            {
                NetworkPlayer localPlayer = NetworkPlayer.Local;
                if (localPlayer != null && localPlayer.TryGetComponent(out PlayerInteractor interactor))
                    interactor.RequestClickerFlashTest();
                else
                    vision.TriggerClickerCameraFlash();
            }

            if (!Input.GetKeyDown(KeyCode.F8)) return;
            bool enabledVision = !vision.DevelopmentFullVision; vision.SetDevelopmentFullVision(enabledVision);
            Debug.Log("Development full vision: " + (enabledVision ? "ON" : "OFF"));
        }
#endif
    }
}
