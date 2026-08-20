using System.Globalization;
using RSDSystem.Models;
using RSDSystem.Validation;

namespace RSDSystem.Helpers
{
    /// <summary>
    /// One admin payroll schedule is one generate-able payroll period.
    /// Staff generate slips against the current open schedule; a new schedule
    /// unlocks another slip for the same employees.
    /// </summary>
    public static class PayrollPeriods
    {
        private static readonly CultureInfo Dates = CultureInfo.InvariantCulture;

        public static PayrollSchedule? Open(IEnumerable<PayrollSchedule> schedules) =>
            schedules
                .Where(s => !s.TaskCompleted)
                .OrderByDescending(s => s.StartingDate.Date)
                .ThenByDescending(s => s.PayrollScheduleId)
                .FirstOrDefault();

        public static PayrollSchedule? Covering(
            IEnumerable<PayrollSchedule> schedules, DateTime start, DateTime end)
        {
            start = start.Date;
            end = end.Date;
            return schedules.FirstOrDefault(s =>
                DateRules.IsUsableDate(s.StartingDate)
                && DateRules.IsUsableDate(s.EndDate)
                && s.StartingDate.Date <= start
                && end <= s.EndDate.Date);
        }

        public static PayrollSchedule? ForPayroll(
            IEnumerable<PayrollSchedule> schedules, Payroll payroll)
        {
            if (payroll.PayrollScheduleId is int id && id > 0)
            {
                var linked = schedules.FirstOrDefault(s => s.PayrollScheduleId == id);
                if (linked != null)
                    return linked;
            }

            return Covering(schedules, payroll.PayPeriodStart, payroll.PayPeriodEnd);
        }

        public static bool BelongsTo(Payroll payroll, PayrollSchedule schedule) =>
            BelongsTo(payroll.PayrollScheduleId, payroll.PayPeriodStart, payroll.PayPeriodEnd, schedule);

        public static bool BelongsTo(
            int? payrollScheduleId, DateTime payPeriodStart, DateTime payPeriodEnd, PayrollSchedule schedule)
        {
            if (payrollScheduleId == schedule.PayrollScheduleId)
                return true;

            if (payrollScheduleId.HasValue)
                return false;

            return payPeriodStart.Date == schedule.StartingDate.Date
                && payPeriodEnd.Date == schedule.EndDate.Date;
        }

        public static string Label(PayrollSchedule schedule) =>
            schedule.StartingDate.ToString("MMM d, yyyy", Dates)
            + " – "
            + schedule.EndDate.ToString("MMM d, yyyy", Dates);

        public static string Label(DateTime start, DateTime end) =>
            start.ToString("MMM d, yyyy", Dates)
            + " – "
            + end.ToString("MMM d, yyyy", Dates);
    }
}
