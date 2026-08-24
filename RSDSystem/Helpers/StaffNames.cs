using Microsoft.AspNetCore.Http;

namespace RSDSystem.Helpers
{
    /// <summary>
    /// Session FullName vs Project.AssignedPayrollStaff.
    /// Staff only work on projects assigned to their exact name (case-insensitive).
    /// </summary>
    public static class StaffNames
    {
        /// <summary>
        /// Reads the logged-in person's FullName from session. Returns null when blank so staff queries can skip name filters.
        /// PayrollStaff screens use this to load only projects assigned to that exact name.
        /// </summary>
        public static string? FromSession(ISession session)
        {
            var name = session.GetString("FullName")?.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        public static bool IsAssigned(string? assignedStaff, string? staffName) =>
            SamePerson(assignedStaff, staffName);

        public static bool SamePerson(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return false;

            return Keys(left).Overlaps(Keys(right));
        }

        public static List<string> LookupKeys(string? name) => Keys(name).ToList();

        public static HashSet<string> Keys(string? name)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var normalized = string.Join(" ",
                (name ?? string.Empty).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrEmpty(normalized))
                return keys;

            keys.Add(normalized);

            var parts = normalized.Split(' ');
            if (parts.Length >= 3)
            {
                var middle = parts[1].TrimEnd('.');
                if (middle.Length <= 2)
                    keys.Add(parts[0] + " " + string.Join(" ", parts.Skip(2)));
            }

            return keys;
        }
    }
}
