namespace Marco.Network.Round
{
    // One switch per server-side ripple change (2026-11-07 기획서 대조). Setting a switch to false restores
    // exactly the behaviour before that change — no revert needed. Server-only constants; each item adds
    // its own switch in its own commit.
    public static class RippleServerFeatures
    {
    }
}
