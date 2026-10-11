namespace Marco.Prototype
{
    public enum PrototypePlayerRole { Hunter, Survivor }
    public struct MatchMember
    {
        public int Id;
        public PrototypePlayerRole Role;
        public string AssetName;
        public MatchMember(int id, PrototypePlayerRole role, string assetName)
        { Id = id; Role = role; AssetName = assetName; }
    }

    // Local launch data only; the room host sends each client's role before loading.
    public static class MatchLaunchContext
    {
        public const string GameScene = "Prototype_Game";
        public const string LobbyScene = "Prototype_Lobby";
        public static bool IsActive { get; private set; }
        public static PrototypePlayerRole LocalRole { get; private set; }
        public static int LocalPlayerId { get; private set; }
        public static MatchMember[] Members { get; private set; } = new MatchMember[0];

        public static void Begin(PrototypePlayerRole role)
        {
            // Legacy/editor launch retains two role templates, without a real network session.
            Begin(role == PrototypePlayerRole.Hunter ? 0 : 1, new[] {
                new MatchMember(0, PrototypePlayerRole.Hunter, "Demon"),
                new MatchMember(1, PrototypePlayerRole.Survivor, "UnityChan") });
        }
        public static void Begin(int localId, MatchMember[] members)
        {
            LocalPlayerId = localId; Members = (MatchMember[])members.Clone();
            foreach (var member in Members) if (member.Id == localId) LocalRole = member.Role;
            IsActive = true;
        }

        public static void Clear()
        {
            IsActive = false;
            LocalRole = PrototypePlayerRole.Hunter;
            LocalPlayerId = 0; Members = new MatchMember[0];
        }
    }
}
