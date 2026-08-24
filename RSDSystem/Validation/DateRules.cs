using System.ComponentModel.DataAnnotations;

namespace RSDSystem.Validation
{
    /// <summary>
    /// Date helpers used by payroll, projects, and schedules.
    /// Kept in its own type so callers do not depend on extra InputRules members.
    /// </summary>
    public static class DateRules
    {
        public const int MinCalendarYear = 2000;
        public const int MaxCalendarYear = 2099;
        public const string CalendarYearMessage = "Enter a valid date with a 4-digit year (2000–2099).";

        /// <summary>
        /// True when the date is null, default, or a two-digit year leftover. Payroll schedules and project dates treat these as empty.
        /// </summary>
        public static bool IsMissingDate(DateTime? value) =>
            !value.HasValue || value.Value == default || value.Value.Year < 1000;

        /// <summary>
        /// True when the date is present and the year is 2000–2099. Used before covering payroll periods and monthly budgets.
        /// </summary>
        public static bool IsUsableDate(DateTime? value) =>
            value.HasValue
            && !IsMissingDate(value)
            && value.Value.Year >= MinCalendarYear
            && value.Value.Year <= MaxCalendarYear;

        /// <summary>
        /// Calendar days in a pay period including both start and end (Aug 1–15 is 15 days). Payroll daily-rate math uses this.
        /// </summary>
        public static int InclusiveDays(DateTime start, DateTime end) =>
            (end.Date - start.Date).Days + 1;

        /// <summary>
        /// Counts Monday–Friday between two dates inclusive. Project duration and some payroll estimates skip weekends this way.
        /// </summary>
        public static int CountWeekdays(DateTime start, DateTime end)
        {
            var days = 0;
            for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    days++;
            }
            return days;
        }

        /// <summary>
        /// Yields model-validation errors if start/end are missing, outside 2000–2099, or end is before start.
        /// Project and payroll-schedule forms bind field names so the red text appears under the right input.
        /// </summary>
        public static IEnumerable<ValidationResult> ValidateDateRange(
            DateTime? start,
            DateTime? end,
            string startField,
            string endField,
            string startLabel = "Starting date",
            string endLabel = "End date")
        {
            if (IsMissingDate(start))
                yield return new ValidationResult($"{startLabel} is required.", new[] { startField });
            else if (!IsUsableDate(start))
                yield return new ValidationResult(CalendarYearMessage, new[] { startField });

            if (IsMissingDate(end))
                yield return new ValidationResult($"{endLabel} is required.", new[] { endField });
            else if (!IsUsableDate(end))
                yield return new ValidationResult(CalendarYearMessage, new[] { endField });

            if (IsUsableDate(start) && IsUsableDate(end) && end!.Value.Date < start!.Value.Date)
            {
                yield return new ValidationResult(
                    $"{endLabel} must be on or after the starting date.",
                    new[] { endField });
            }
        }

        public static DateTime MonthOfPeriod(DateTime payPeriodStart) =>
            new(payPeriodStart.Year, payPeriodStart.Month, 1);

        public static DateTime FirstOfMonth(DateTime value) =>
            new(value.Year, value.Month, 1);

        public static DateTime LastDayOfMonth(DateTime value) =>
            FirstOfMonth(value).AddMonths(1).AddDays(-1);

        /// <summary>
        /// True after the last calendar day of that month (Philippines local date).
        /// </summary>
        public static bool IsCalendarMonthFinished(DateTime month, DateTime today) =>
            today.Date > LastDayOfMonth(month);
    }
}
