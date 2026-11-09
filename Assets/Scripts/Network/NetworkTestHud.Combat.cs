using Marco.Core.WinCondition;
using Marco.Network.Player;
using Marco.Network.Round;
using Marco.Network.Vision;
using UnityEngine;

namespace Marco.Network
{
    // Runs on the actual FishNet game HUD, including one-player ValvesOnly sessions.
    public sealed partial class NetworkTestHud
    {
        private Texture2D injuryEdges;

        private static void DrawShoutGauge(NetworkRound round)
        {
            var player = NetworkPlayer.Local;
            if (Event.current.type != EventType.Repaint || player == null || player.Role != PlayerRole.Seeker ||
                player.Status != PlayerStatus.Alive || round.Result != RoundResult.InProgress ||
                !player.TryGetComponent(out PlayerMovement movement)) return;

            int oldDepth = GUI.depth;
            GUI.depth = -3;
            Vector2 center = new(TestHudSkin.Width * .5f, TestHudSkin.Height * .5f);
            bool preparing = movement.IsPreparingShout;
            bool cooling = movement.ShoutCooldownRemaining > 0;
            bool locked = round.IsStartLocked(player.Role);
            Color accent = locked ? MutedColor : preparing ? RotatingColor : SeekerColor;
            float fraction = locked ? 0 : preparing ? movement.ShoutPreparationFraction
                : cooling ? movement.ShoutCooldownFraction : 1;

            // Outside the 28px tag cooldown ring: a continuous right-hand semicircle.
            DrawShoutArc(center, 1, new Color(0, 0, 0, .8f), 7);
            DrawShoutArc(center, 1, new Color(1, 1, 1, .2f), 3);
            DrawShoutArc(center, fraction, accent, 3);
            var label = new GUIStyle(TestHudSkin.Text(14, FontStyle.Bold, TextAnchor.MiddleCenter));
            label.normal.textColor = accent;
            GUI.Label(new Rect(center.x + 60, center.y - 21, 74, 22), "R · 외침", label);
            GUI.Label(new Rect(center.x + 60, center.y + 1, 74, 22), locked ? "대기"
                : preparing ? "준비 중" : cooling ? $"{Mathf.CeilToInt(movement.ShoutCooldownRemaining)}초" : "홀드", label);
            GUI.depth = oldDepth;
        }

        private static void DrawShoutArc(Vector2 center, float fraction, Color color, float thickness)
        {
            const float radius = 48;
            const int segments = 64;
            float progress = Mathf.Clamp01(fraction) * segments;
            Matrix4x4 previous = GUI.matrix;
            for (int i = 0; i < Mathf.CeilToInt(progress); i++)
            {
                float start = -Mathf.PI * .5f + Mathf.PI * i / segments;
                float end = -Mathf.PI * .5f + Mathf.PI * Mathf.Min(i + 1, progress) / segments;
                Vector2 from = center + new Vector2(Mathf.Cos(start), Mathf.Sin(start)) * radius;
                Vector2 to = center + new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * radius;
                Vector2 delta = to - from;
                GUI.matrix = previous * Matrix4x4.TRS(new Vector3(from.x, from.y, 0),
                    Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg), Vector3.one);
                TestHudSkin.Fill(new Rect(0, -thickness * .5f, delta.magnitude + .5f, thickness), color);
            }
            GUI.matrix = previous;
        }

        private static void DrawSeekerReticle(NetworkRound round)
        {
            var player = NetworkPlayer.Local;
            if (Event.current.type != EventType.Repaint || player == null || player.Role != PlayerRole.Seeker ||
                player.Status != PlayerStatus.Alive || round.Result != RoundResult.InProgress ||
                !player.TryGetComponent(out PlayerInteractor interactor)) return;

            int oldDepth = GUI.depth;
            GUI.depth = -3;
            Vector2 center = new(TestHudSkin.Width * 0.5f, TestHudSkin.Height * 0.5f);
            bool waiting = interactor.SwingPending;
            bool cooling = interactor.SwingCooldownRemaining > 0;
            bool locked = round.IsStartLocked(PlayerRole.Seeker);
            var vision = player.GetComponentInChildren<MicrophoneVisionController>();
            bool reachable = !waiting && !cooling && !locked && (vision == null || vision.IsCenterVisible) &&
                round.TryFindTagTarget(player.transform, round.TagRadius, out _);
            Color color = waiting || cooling || locked ? new Color(0.6f, 0.65f, 0.7f) : reachable ? SeekerColor : Color.white;
            TestHudSkin.Fill(new Rect(center.x - 4, center.y - 4, 8, 8), new Color(0, 0, 0, 0.8f));
            TestHudSkin.Fill(new Rect(center.x - 2, center.y - 2, 4, 4), color);
            TestHudSkin.Fill(new Rect(center.x - 13, center.y - 1, 6, 2), color);
            TestHudSkin.Fill(new Rect(center.x + 7, center.y - 1, 6, 2), color);
            TestHudSkin.Fill(new Rect(center.x - 1, center.y - 13, 2, 6), color);
            TestHudSkin.Fill(new Rect(center.x - 1, center.y + 7, 2, 6), color);
            const int segments = 96;
            int remaining = Mathf.CeilToInt(interactor.SwingCooldownFraction * segments);
            int spinner = Mathf.FloorToInt(Time.unscaledTime * 60) % segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2;
                Vector2 point = center + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * 28;
                bool lit = waiting ? (i - spinner + segments) % segments < 18 : i < remaining;
                TestHudSkin.Fill(new Rect(point.x - 3, point.y - 3, 6, 6), new Color(0, 0, 0, .45f));
                TestHudSkin.Fill(new Rect(point.x - 2, point.y - 2, 4, 4), lit
                    ? new Color(1f, 0.72f, 0.25f, 1f) : new Color(1, 1, 1, 0.45f));
            }
            GUI.depth = oldDepth;
        }

        private void DrawRunnerInjury(NetworkRound round)
        {
            var player = NetworkPlayer.Local;
            if (Event.current.type != EventType.Repaint || player == null || player.Role != PlayerRole.Runner ||
                player.Status != PlayerStatus.Alive || player.HealthHalves <= 0 || player.HealthHalves >= round.FullHalves) return;
            if (injuryEdges == null) CreateInjuryEdges();
            bool wounded = player.HealthHalves <= 2;
            float pulse = 1 + Mathf.Sin(Time.time * Mathf.PI * 2 * (wounded ? 1.1f : 0.8f)) * (wounded ? 0.08f : 0.04f);
            float sinceHit = Time.time - player.LastInjuryTime;
            float flash = player.LastInjuryTime >= 0 && sinceHit >= 0 ? 0.22f * (1 - Mathf.Clamp01(sinceHit / 0.45f)) : 0;
            Color tint = wounded ? new Color(0.9f, 0.08f, 0.025f) : new Color(1f, 0.38f, 0.045f);
            tint.a = Mathf.Clamp01((wounded ? 0.55f : 0.32f) * pulse + flash);
            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldColor = GUI.color;
            int oldDepth = GUI.depth;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = 50; // Above darkness (100), behind the normal HUD (0).
            GUI.color = tint;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), injuryEdges);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
            GUI.depth = oldDepth;
        }

        private void CreateInjuryEdges()
        {
            const int size = 256;
            injuryEdges = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Network survivor injury edges", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float horizontal = 1 - Mathf.SmoothStep(0, 1, Mathf.Min(x, size - 1 - x) / ((size - 1) * 0.22f));
                float vertical = 1 - Mathf.SmoothStep(0, 1, Mathf.Min(y, size - 1 - y) / ((size - 1) * 0.22f));
                float alpha = Mathf.Clamp01(Mathf.Max(horizontal, vertical) * 0.8f + Mathf.Min(horizontal, vertical) * 0.2f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
            injuryEdges.SetPixels32(pixels);
            injuryEdges.Apply(false, true);
        }

        private void OnDestroy()
        {
            if (injuryEdges == null) return;
            if (Application.isPlaying) Destroy(injuryEdges);
            else DestroyImmediate(injuryEdges);
        }
    }
}
