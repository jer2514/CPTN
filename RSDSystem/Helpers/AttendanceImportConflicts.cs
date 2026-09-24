namespace RSDSystem.Helpers
{
    /// <summary>
    /// Attendance has a global unique (EmployeeId, WorkDate) index. Re-import may
    /// replace the current project's unlocked rows, but it must not steal days that
    /// already belong to another project (for example after an employee transfer).
    /// </summary>
    public static class AttendanceImportConflicts
    {
        public static bool BelongsToProject(int recordProjectId, int? importProjectId, int projectId)
        {
            if (recordProjectId == projectId)
                return true;
            if (importProjectId is int importId && importId == projectId)
                return true;

            // Legacy rows with no project pointer stay replaceable so the unique
            // employee+date index does not block a valid same-project import.
            return recordProjectId <= 0 && !importProjectId.HasValue;
        }

        public static string EmployeeDateKey(int employeeId, DateTime? workDate)
        {
            var date = AttendanceDisplay.UsableDate(workDate);
            return date.HasValue
                ? employeeId + ":" + date.Value.ToString("yyyy-MM-dd")
                : employeeId + ":none";
        }

        public static bool IncomingConflictsWithOtherProject(
            int? employeeId, DateTime? workDate, ISet<string> otherProjectKeys)
        {
            if (employeeId is not int id || id <= 0 || otherProjectKeys.Count == 0)
                return false;

            return otherProjectKeys.Contains(EmployeeDateKey(id, workDate));
        }
    }
}
