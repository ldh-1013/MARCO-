namespace Marco.Network.Vision
{
    // Client-only ripple switches (Phase C). Static fields rather than constants so the checks and the local
    // two-instance test can run on and off in one build; the shipped default is off. Server behaviour never
    // depends on these (RippleServerFeatures holds the server switches).
    public static class RippleClientFeatures
    {
        // §5.4 direction hint: a ring-display sound whose world ring is hidden by occlusion lights the screen edge.
        public static bool DirectionHint = true;
    }
}
