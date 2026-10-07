using UnityEngine;

namespace Marco.Prototype
{
    // All world objects are serialized in the scene. This component only draws the prototype HUD.
    public sealed class PrototypeSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private PrototypeSceneKind sceneKind = PrototypeSceneKind.Lobby;
        private void OnGUI()
        {
            if (sceneKind == PrototypeSceneKind.Lobby) return;
            bool hunter = sceneKind == PrototypeSceneKind.HunterTest;
            GUI.Label(new Rect(35, 25, 800, 35), hunter ? "HUNTER TEST" : "SURVIVOR TEST");
            GUI.Label(new Rect(35, 55, 1050, 28), hunter ? "WASD 이동 · 마우스 회전 · 좌클릭 태그" : "WASD 이동 · 마우스 회전 · E 상호작용");
        }
    }
    public enum PrototypeSceneKind { Lobby, HunterTest, SurvivorTest }
}
