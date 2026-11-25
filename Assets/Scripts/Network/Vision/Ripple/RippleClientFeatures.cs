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
        public static bool SourceSnapshot = true;
    }
}
