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

        /// <summary>
        /// Picks the current open payroll schedule (not TaskCompleted), newest start date first.
        /// Staff generate slips and import attendance against this window until Admin approves the task as done.
        /// </summary>
        public static PayrollSchedule? Open(IEnumerable<PayrollSchedule> schedules) =>
            schedules
                .Where(s => !s.TaskCompleted)
                .OrderByDescending(s => s.StartingDate.Date)
                .ThenByDescending(s => s.PayrollScheduleId)
                .FirstOrDefault();

        /// <summary>
        /// Finds the schedule whose start/end fully contain the given pay period dates.
        /// Used when a payroll row has no PayrollScheduleId yet (older databases) so review screens still group by schedule.
        /// </summary>
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

        /// <summary>
        /// Resolves the schedule for one payroll slip: linked PayrollScheduleId first, otherwise date covering.
        /// Admin review and staff pending lists use this to show the right period label.
        /// </summary>
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

        /// <summary>
        /// True when this payroll slip belongs to the given schedule (by id, or by matching dates if id is null).
        /// </summary>
        public static bool BelongsTo(Payroll payroll, PayrollSchedule schedule) =>
            BelongsTo(payroll.PayrollScheduleId, payroll.PayPeriodStart, payroll.PayPeriodEnd, schedule);

        /// <summary>
        /// Compares a slip's schedule id and pay dates to one admin schedule. A set id must match exactly;
        /// a null id matches only when start and end dates equal the schedule window.
        /// </summary>
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

        /// <summary>
        /// Human period label from a schedule, for example <c>Aug 1, 2026 – Aug 15, 2026</c>, shown on to-do and review screens.
        /// </summary>
        public static string Label(PayrollSchedule schedule) =>
            schedule.StartingDate.ToString("MMM d, yyyy", Dates)
            + " – "
            + schedule.EndDate.ToString("MMM d, yyyy", Dates);

        /// <summary>
        /// Same period label from raw dates (attendance imports and notifications that do not have a PayrollSchedule object).
        /// </summary>
        public static string Label(DateTime start, DateTime end) =>
            start.ToString("MMM d, yyyy", Dates)
            + " – "
            + end.ToString("MMM d, yyyy", Dates);
    }
}
