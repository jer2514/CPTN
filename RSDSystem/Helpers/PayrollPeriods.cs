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
        /// Latest admin schedule for generate/import. Mark as Done only checks
        /// off the staff to-do list; it does not close this pay period.
        /// </summary>
        public static PayrollSchedule? Open(IEnumerable<PayrollSchedule> schedules) =>
            schedules
                .OrderByDescending(s => s.StartingDate.Date)
                .ThenByDescending(s => s.PayrollScheduleId)
                .FirstOrDefault();

        public static int PacketId(Payroll payroll) =>
            payroll.PayrollScheduleId is int id && id > 0
                ? id
                : payroll.PayPeriodStart.Year * 10000 + payroll.PayPeriodStart.Month * 100 + payroll.PayPeriodStart.Day;

        public static bool SamePacket(Payroll payroll, int projectId, DateTime start, DateTime end, int? scheduleId)
        {
            if (payroll.ProjectId != projectId)
                return false;
            if (scheduleId is int id && id > 0)
                return payroll.PayrollScheduleId == id
                    || (payroll.PayrollScheduleId == null
                        && payroll.PayPeriodStart.Date == start.Date
                        && payroll.PayPeriodEnd.Date == end.Date);
            return payroll.PayPeriodStart.Date == start.Date && payroll.PayPeriodEnd.Date == end.Date;
        }

        public static string ReviewUrl(int projectId, DateTime start, DateTime end, int? scheduleId = null)
        {
            var url = "/Payroll/ReviewProject?projectId=" + projectId
                + "&start=" + start.ToString("yyyy-MM-dd", Dates)
                + "&end=" + end.ToString("yyyy-MM-dd", Dates);
            if (scheduleId is int id && id > 0)
                url += "&scheduleId=" + id;
            return url;
        }

        public static string ReviewUrl(Payroll payroll) =>
            ReviewUrl(payroll.ProjectId, payroll.PayPeriodStart, payroll.PayPeriodEnd, payroll.PayrollScheduleId);

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
