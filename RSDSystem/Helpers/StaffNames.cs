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

        /// <summary>
        /// True when Project.AssignedPayrollStaff equals the current staff name (trim + case-insensitive).
        /// Staff cannot import attendance or generate payroll for a project assigned to someone else.
        /// </summary>
        public static bool IsAssigned(string? assignedStaff, string? staffName)
        {
            if (string.IsNullOrWhiteSpace(assignedStaff) || string.IsNullOrWhiteSpace(staffName))
                return false;

            return string.Equals(assignedStaff.Trim(), staffName.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
