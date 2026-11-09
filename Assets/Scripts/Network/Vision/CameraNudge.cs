using Marco.Network.Player;
using UnityEngine;

namespace Marco.Network.Vision
{
    // 연출.md 7.2 화면 미세 진동 0.1초 — the owner's camera jitters a fraction of a degree, then returns
    // exactly to where it was. Rotation only, on the camera itself (its pivot keeps the look direction),
    // so nothing else that moves the camera is disturbed. Added on demand; reusable for the knock feedback.
    [DefaultExecutionOrder(1000)]
    public sealed class CameraNudge : MonoBehaviour
    {
        public const float Seconds = .1f;
        private const float Degrees = .45f;

        private float until;
        private bool nudging;
        private Quaternion rest;

        public static void Play(NetworkPlayer owner)
        {
            var camera = owner != null ? owner.GetComponentInChildren<Camera>() : null;
            if (camera == null) return;
            var nudge = camera.GetComponent<CameraNudge>() ?? camera.gameObject.AddComponent<CameraNudge>();
            nudge.Begin();
        }

        private void Begin()
        {
            if (!nudging) rest = transform.localRotation;
            nudging = true;
            until = Time.unscaledTime + Seconds;
        }

        private void LateUpdate()
        {
            if (!nudging) return;
            if (Time.unscaledTime >= until) { Stop(); return; }
            float x = (Mathf.PerlinNoise(Time.unscaledTime * 90f, 0f) - .5f) * 2f * Degrees;
            float y = (Mathf.PerlinNoise(0f, Time.unscaledTime * 90f) - .5f) * 2f * Degrees;
            transform.localRotation = rest * Quaternion.Euler(x, y, 0f);
        }

        private void Stop()
        {
            nudging = false;
            transform.localRotation = rest;
        }

        private void OnDisable() { if (nudging) Stop(); }
    }
}
