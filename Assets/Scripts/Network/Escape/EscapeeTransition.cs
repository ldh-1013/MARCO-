using Marco.Network.Player;
using Marco.Network.Round;
using UnityEngine;

namespace Marco.Network.Escape
{
    // Owner only: the escape fade on this player's own screen (연출.md 5). Other players see nothing of
    // it — survivors just stop rendering the body, Echo bodies see it turn cyan. NetworkPlayer adds this
    // to the local player and calls Begin when its own status goes Alive → Escaped.
    //
    // Until the screen is fully cyan (1.0 s) the owner keeps the living body and cannot move, so the
    // change to the Echo body happens out of sight; then the cyan clears over the Echo view.
    public sealed class EscapeeTransition : MonoBehaviour
    {
        private NetworkPlayer player;
        private bool running;
        private float startedAt;
        // Set when the result screen takes over mid-fade: clear from this opacity instead.
        private bool cutShort;
        private float cutFrom, cutAt;
        private bool bodySwitched;
#if UNITY_EDITOR
        // Verification renders (MARCO > Escapee) pin the timeline to exact moments.
        private float? frozenElapsed;
        public void FreezeAt(float? elapsed) => frozenElapsed = elapsed;
#endif

        // The owner still wears the living body (look, collision) while this is true.
        public bool HoldsLivingBody => running && !bodySwitched;
        // No movement while the screen fades out.
        public bool FreezesMovement => HoldsLivingBody;
        public bool IsRunning => running;

        private float Elapsed
        {
            get
            {
#if UNITY_EDITOR
                if (frozenElapsed is float frozen) return frozen;
#endif
                return Time.unscaledTime - startedAt;
            }
        }

        private void Awake() => player = GetComponent<NetworkPlayer>();

        public void Begin()
        {
            if (running) return;
            running = true;
            cutShort = false;
            bodySwitched = false;
            startedAt = Time.unscaledTime;
        }

        // Status went back to Alive (rematch, lobby): drop everything at once, no fade.
        public void Cancel()
        {
            bool wasHolding = HoldsLivingBody;
            running = false;
            cutShort = false;
            bodySwitched = false;
            if (wasHolding && player != null) player.RefreshLooks();
        }

        private void Update()
        {
            if (!running) return;
            // The round ended on this escape (last Runner out) or right after it: let the result screen
            // through instead of covering its opening, but still switch the body first.
            var round = NetworkRound.Instance;
            if (!cutShort && round != null && round.IsResultScreenShown)
            {
                cutShort = true;
                cutFrom = EscapeFade.Alpha(Elapsed);
                cutAt = Time.unscaledTime;
            }
            if (!bodySwitched && (cutShort || EscapeFade.HasSwitchedBody(Elapsed)))
            {
                bodySwitched = true;
                if (player != null) player.RefreshLooks();
            }
            if (cutShort ? Time.unscaledTime - cutAt >= EscapeFade.RevealSeconds : EscapeFade.IsFinished(Elapsed))
                running = false;
        }

        // Overlay opacity this frame (also read by the batch-mode render check, which has no IMGUI).
        public float OverlayAlpha => CurrentAlpha;

        private float CurrentAlpha => !running ? 0f
            : cutShort ? EscapeFade.Reveal(cutFrom, Time.unscaledTime - cutAt)
            : EscapeFade.Alpha(Elapsed);

        private void OnGUI()
        {
            // Over everything, HUD included (lower depth draws on top). Set on every event, every frame:
            // Unity sorts behaviours by the depth they leave behind.
            GUI.depth = -1000;
            float alpha = CurrentAlpha;
            if (alpha <= 0f || Event.current.type != EventType.Repaint) return;
            Color saved = GUI.color;
            var cyan = EscapeFade.Cyan;
            GUI.color = new Color(cyan.r, cyan.g, cyan.b, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = saved;
        }
    }
}
