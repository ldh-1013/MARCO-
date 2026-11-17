using Marco.Network.Player;

namespace Marco.Network.Vision
{
    // 연출.md 7.2 화면 미세 진동 0.1초 — the owner's camera jitters a fraction of a degree, then returns exactly to where it
    // was. [V29] Now a channel of CameraFeelStack (summed and capped with the other camera feel effects, same values:
    // 0.1 s, 0.45°, low-frequency noise), so two components never write the camera's rotation against each other.
    public static class CameraNudge
    {
        public const float Seconds = .1f;

        public static void Play(NetworkPlayer owner)
        {
            if (owner == null || owner != NetworkPlayer.Local) return;
            CameraFeelStack.Instance?.Nudge();
        }
    }
}
