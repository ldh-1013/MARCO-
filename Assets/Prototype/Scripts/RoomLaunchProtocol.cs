using System;
using System.Collections.Generic;
using System.Text;

namespace Marco.Prototype
{
    // Only the host sends this command. Scene names are never accepted from the network.
    public static class RoomLaunchProtocol
    {
        private const string Prefix = "MARCO/2 START ";
        public static string StartCommand(PrototypePlayerRole role) => Prefix + role;

        public static bool TryParseStart(string message, out PrototypePlayerRole role)
        {
            role = PrototypePlayerRole.Hunter;
            if (message == Prefix + "Hunter") return true;
            if (message == Prefix + "Survivor") { role = PrototypePlayerRole.Survivor; return true; }
            return false;
        }

        public static PrototypePlayerRole RoleFor(int playerId, int hunterId) =>
            playerId == hunterId ? PrototypePlayerRole.Hunter : PrototypePlayerRole.Survivor;

        public static string StartCommand(int localId, MatchMember[] members)
            => AssignmentCommand("MARCO/3 START ", localId, members);

        private static string AssignmentCommand(string prefix, int localId, MatchMember[] members)
        {
            var entries = new List<string>();
            foreach (var member in members)
                entries.Add(member.Id + ":" + member.Role + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(member.AssetName)));
            return prefix + localId + " " + string.Join("|", entries);
        }
        public static bool TryParseMatch(string line, out int localId, out MatchMember[] members)
            => TryParseAssignment(line, "MARCO/3 START ", false, out localId, out members);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Development commands are intentionally separate from normal game start.
        // The request carries no ID: the host uses the accepted socket identity.
        public const string DevelopmentRoleRequest = "MARCO/3 DEV_TOGGLE_ROLE";
        public static string DevelopmentRolesCommand(int localId, MatchMember[] members)
            => AssignmentCommand("MARCO/3 DEV_ROLES ", localId, members);
        public static bool TryParseDevelopmentRoles(string line, out int localId, out MatchMember[] members)
            => TryParseAssignment(line, "MARCO/3 DEV_ROLES ", true, out localId, out members);
#endif

        private static bool TryParseAssignment(string line, string prefix, bool allowSoloSurvivor, out int localId, out MatchMember[] members)
        {
            localId = -1; members = null;
            if (line == null || line.Length > 4096 || !line.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var parts = line.Substring(prefix.Length).Split(' ');
            if (parts.Length != 2 || !int.TryParse(parts[0], out localId) || localId < 0) return false;
            var entries = parts[1].Split('|');
            if (entries.Length < 1 || entries.Length > 16) return false;
            var result = new List<MatchMember>(); var ids = new HashSet<int>(); int hunters = 0;
            foreach (string entry in entries)
            {
                var fields = entry.Split(':');
                if (fields.Length != 3 || !int.TryParse(fields[0], out int id) || id < 0 || !ids.Add(id)) return false;
                PrototypePlayerRole role;
                if (fields[1] == "Hunter") { role = PrototypePlayerRole.Hunter; hunters++; }
                else if (fields[1] == "Survivor") role = PrototypePlayerRole.Survivor;
                else return false;
                string name;
                try { name = Encoding.UTF8.GetString(Convert.FromBase64String(fields[2])); } catch (FormatException) { return false; }
                if (string.IsNullOrWhiteSpace(name) || name.Length > 80) return false;
                foreach (char c in name) if (!char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-') return false;
                result.Add(new MatchMember(id, role, name));
            }
            if ((hunters != 1 && !(allowSoloSurvivor && result.Count == 1 && hunters == 0)) || !ids.Contains(localId)) return false;
            members = result.ToArray(); return true;
        }
    }
}
