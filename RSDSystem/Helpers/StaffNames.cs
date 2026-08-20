using Microsoft.AspNetCore.Http;

namespace RSDSystem.Helpers
{
    public static class StaffNames
    {
        public static string? FromSession(ISession session)
        {
            var name = session.GetString("FullName")?.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        public static bool IsAssigned(string? assignedStaff, string? staffName)
        {
            if (string.IsNullOrWhiteSpace(assignedStaff) || string.IsNullOrWhiteSpace(staffName))
                return false;

            return string.Equals(assignedStaff.Trim(), staffName.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
