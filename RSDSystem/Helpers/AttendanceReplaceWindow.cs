using RSDSystem.Models;

namespace RSDSystem.Helpers
{
    /// <summary>
    /// Re-import replaces attendance only for the incoming period.
    /// A wider earlier import (for example a full month that overlaps 1–15 and 16–end)
    /// must not lose unlocked days that fall outside the new file's dates.
    /// </summary>
    public static class AttendanceReplaceWindow
    {
        public static bool Contains(DateTime? workDate, DateTime periodStart, DateTime periodEnd)
        {
            var start = periodStart.Date;
            var end = periodEnd.Date;
            if (end < start)
                (start, end) = (end, start);

            var day = AttendanceDisplay.UsableDate(workDate);
            if (!day.HasValue)
                return true;

            return start <= day.Value && day.Value <= end;
        }

        public static List<AttendanceRecord> UnlockedInWindow(
            IEnumerable<AttendanceRecord> records,
            DateTime periodStart,
            DateTime periodEnd,
            IEnumerable<ClosedPayrollWindow> closedPayrolls)
        {
            return records
                .Where(r => Contains(r.WorkDate, periodStart, periodEnd)
                    && !PayrollAttendanceLock.IsLocked(closedPayrolls, r.EmployeeId, r.WorkDate))
                .ToList();
        }
    }
}
