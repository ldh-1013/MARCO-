namespace Marco.Prototype
{
    // Local prototype state. Do not broadcast this health value to all network clients.
    public sealed class SurvivorHealth
    {
        public float Lives { get; private set; } = 2f;
        public bool IsEcho => Lives <= 0;
        public bool IsWounded => Lives == 1f;
        public float MovementMultiplier => Lives == 1.5f ? 0.85f : 1f;
        public bool Hit()
        {
            if (IsEcho) return false;
            Lives = Lives < 2f ? 0f : 1f;
            return true;
        }
        public void Bandage() { if (!IsEcho) Lives = System.Math.Min(2f, Lives + 0.5f); }
        public void Treat() { if (!IsEcho) Lives = 2f; }
        public void Reset() { Lives = 2f; }
    }
}
