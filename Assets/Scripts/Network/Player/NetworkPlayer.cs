using FishNet.Object;
using UnityEngine;

namespace Marco.Network.Player
{
    // Turns the camera/input on only for the local owner's capsule and swaps out the scene camera.
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private GameObject ownerCamera;

        private Camera sceneCamera;

        public override void OnStartClient()
        {
            base.OnStartClient();
            ownerCamera.SetActive(IsOwner);
            if (!IsOwner) return;

            sceneCamera = Camera.main;
            if (sceneCamera != null && sceneCamera.gameObject != ownerCamera) sceneCamera.gameObject.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (!IsOwner) return;

            if (sceneCamera != null) sceneCamera.gameObject.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
        }
    }
}
