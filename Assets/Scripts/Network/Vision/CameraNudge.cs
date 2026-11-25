using Marco.Network.Player;

namespace Marco.Network.Vision
{
    // 연출.md 7.2 화면 미세 진동 0.1초 — the owner's camera jitters a fraction of a degree, then returns exactly to where it
    // was. [V29] A channel of CameraFeelStack (summed and capped with the other camera feel effects), so two components never
    // write the camera against each other. [VB B-3] 0.12° · 2 mm, low-frequency noise under a decaying envelope, the lowest
    // priority (EchoFeelRules).
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
