namespace Marco.Network.Vision
{
    // Client-only ripple switches (Phase C). Static fields rather than constants so the checks and the local
    // two-instance test can run on and off in one build; the shipped default is on. Server behaviour never
    // depends on these (RippleServerFeatures holds the server switches).
    public static class RippleClientFeatures
    {
        // §5.4 direction hint: a ring-display sound whose world ring is hidden by occlusion lights the screen edge.
        public static bool DirectionHint = true;
        // §5.5-2 · §16.1 [10-05] source snapshot: other characters' real bodies are hidden on a survivor's screen and
        // shown only as the frozen silhouette of a ring-display sound they made. Off = both skipped, behaviour as before.
        // [11-18 V39] off: characters are visible at all times again (pre-10-05 behaviour). On, the frozen silhouette
        // would be drawn over the visible real body (a double image), and the Echo knock would show a fake Runner.
        public static bool SourceSnapshot = false;
        // [11-18 V39] §16.1 role rim on the other living characters' bodies in a survivor's view (Runner #7FE7E0,
        // Seeker #EF6A4C), through the character shader's existing _FeelRim slot. The shader adds _FeelRim after the light,
        // so it glows in the dark at any distance, lit or not.
        // [11-22 V41] off (user decision): bodies carry no standing role colour. A body shows only inside this player's
        // own voice / ripple light, behind walls it is hidden, and the character stays the plain matt white of §16.1.
        // Off, SurvivorBodyVisibility writes nothing to _FeelRim (it keeps the shader default 0); the Seeker's own fingertip
        // rim, the 4.5 target rim and every other _FeelRim user are separate paths and are untouched.
        public static bool BodyRim = false;
    }
}
