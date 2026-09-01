using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Helpers
{
    public static class PayrollAttendanceLock
    {
        public static bool IsClosed(string? status)
        {
            var value = (status ?? "").Trim();
            return string.Equals(value, PayrollStatusOptions.Approved, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Covers(DateTime periodStart, DateTime periodEnd, DateTime workDate)
        {
            var day = workDate.Date;
            return periodStart.Date <= day && day <= periodEnd.Date;
        }

        public static async Task<List<ClosedPayrollWindow>> LoadClosedAsync(
            PayrollDbContext db, int projectId, CancellationToken cancellationToken = default)
        {
            return await db.Payrolls.AsNoTracking()
                .Where(p => p.ProjectId == projectId
                    && p.Status == PayrollStatusOptions.Approved)
                .Select(p => new ClosedPayrollWindow
                {
                    EmployeeId = p.EmployeeId,
                    Start = p.PayPeriodStart,
                    End = p.PayPeriodEnd
                })
                .ToListAsync(cancellationToken);
        }

        public static async Task<bool> IsLockedAsync(
            PayrollDbContext db,
            int projectId,
            int? employeeId,
            DateTime? workDate,
            CancellationToken cancellationToken = default)
        {
            return await HasPayrollInPeriodAsync(
                db, projectId, employeeId, workDate, approvedOnly: true, cancellationToken);
        }

        /// <summary>
        /// Submitted payroll has already snapshotted overtime. Changing the decision
        /// after that would leave the slip paying hours admin later rejected, or
        /// omitting hours they later authorized.
        /// </summary>
        public static async Task<bool> BlocksOvertimeReviewAsync(
            PayrollDbContext db,
            int projectId,
            int? employeeId,
            DateTime? workDate,
            CancellationToken cancellationToken = default)
        {
            return await HasPayrollInPeriodAsync(
                db, projectId, employeeId, workDate, approvedOnly: false, cancellationToken);
        }

        private static async Task<bool> HasPayrollInPeriodAsync(
            PayrollDbContext db,
            int projectId,
            int? employeeId,
            DateTime? workDate,
            bool approvedOnly,
            CancellationToken cancellationToken)
        {
            if (employeeId is not int id || id <= 0 || workDate is not DateTime day)
                return false;

            var date = day.Date;
            return await db.Payrolls.AsNoTracking()
                .AnyAsync(p => p.ProjectId == projectId
                    && p.EmployeeId == id
                    && (approvedOnly
                        ? p.Status == PayrollStatusOptions.Approved
                        : (p.Status == PayrollStatusOptions.Approved
                            || p.Status == PayrollStatusOptions.Submitted))
                    && p.PayPeriodStart.Date <= date
                    && p.PayPeriodEnd.Date >= date, cancellationToken);
        }

        public static bool IsLocked(
            IEnumerable<ClosedPayrollWindow> windows, int? employeeId, DateTime? workDate)
        {
            if (employeeId is not int id || id <= 0 || workDate is not DateTime day)
                return false;

            var date = day.Date;
            return windows.Any(w => w.EmployeeId == id && Covers(w.Start, w.End, date));
        }
    }

    public class ClosedPayrollWindow
    {
        public int EmployeeId { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
    }
}
