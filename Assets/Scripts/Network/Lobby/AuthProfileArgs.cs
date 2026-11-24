using System;

namespace Marco.Network.Lobby
{
    // [11-25 V61] Same-PC testing: the launch argument "-profile <name>" picks a Unity Authentication profile, so two copies on one PC
    // sign in as two different anonymous players (without it both are the same player and the second room join answers 409 Conflict).
    // No argument = the default profile, exactly as before. Unity's profile rule: letters, digits, '-' and '_', at most 30 characters.
    public static class AuthProfileArgs
    {
        public const int MaxLength = 30;

        public enum Result { None, Valid, Invalid }

        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > MaxLength) return false;
            foreach (char c in name)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) return false;
            return true;
        }

        // `args` is Environment.GetCommandLineArgs(). Result.None: no "-profile" (or no value after it); Invalid: a value that breaks the rule.
        public static Result Parse(string[] args, out string name)
        {
            name = null;
            if (args == null) return Result.None;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], "-profile", StringComparison.OrdinalIgnoreCase)) continue;
                string value = args[i + 1];
                if (IsValidName(value)) { name = value; return Result.Valid; }
                name = value;
                return Result.Invalid;
            }
            return Result.None;
        }
    }
}
