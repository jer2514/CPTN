using System.Globalization;

namespace RSDSystem.Helpers
{
    /// <summary>Format punch times and dates for the attendance tables (empty → ——).</summary>
    public static class AttendanceDisplay
    {
        public const string Empty = "——";

        /// <summary>
        /// Converts a punch string to 24-hour <c>HH:mm</c> for HTML time inputs on attendance edit screens.
        /// Input is a stored punch (for example <c>8:00 AM</c>); output is empty when the value is not a time.
        /// </summary>
        public static string HtmlTime(string? value)
        {
            if (!TryParseTime(value, out var time))
                return "";
            return DateTime.Today.Add(time).ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

        /// <summary>
        /// Formats a work date as <c>MMMM d, yyyy</c> (English) for attendance tables and period labels.
        /// Dummy dates before year 1900 and nulls become the empty dash <see cref="Empty"/>.
        /// </summary>
        public static string LongDate(DateTime? date) =>
            UsableDate(date)?.ToString("MMMM d, yyyy", English) ?? Empty;

        /// <summary>
        /// Returns the calendar date if it is a real work date (year 1900+); otherwise null.
        /// Import, filters, and payroll totals use this so Excel dummy dates are ignored.
        /// </summary>
        public static DateTime? UsableDate(DateTime? date)
        {
            if (!date.HasValue || date.Value.Year < 1900)
                return null;
            return date.Value.Date;
        }

        /// <summary>
        /// Short day heading for the month-edit grid, for example <c>15 Mo</c>.
        /// Input is a calendar day; output is day-of-month plus two-letter weekday.
        /// </summary>
        public static string DayLabel(DateTime date) =>
            date.ToString("dd", English) + " " + date.ToString("ddd", English)[..2];

        /// <summary>
        /// Hours from one punch-in to punch-out. Overnight spans wrap past midnight.
        /// Returns 0 when either time is missing. Used as the building block for regular and overtime hours.
        /// </summary>
        public static decimal HoursBetween(string? timeIn, string? timeOut)
        {
            if (!TryParseTime(timeIn, out var start) || !TryParseTime(timeOut, out var end))
                return 0;

            var span = end - start;
            if (span < TimeSpan.Zero)
                span += TimeSpan.FromDays(1);
            return Math.Round((decimal)span.TotalHours, 2);
        }

        /// <summary>
        /// Morning plus afternoon session hours from four punch strings (in1/out1 + in2/out2).
        /// Display helper only; payroll uses <see cref="AttendanceRules.RegularHours"/> which clips to the shift windows.
        /// </summary>
        public static decimal RegularHours(string? in1, string? out1, string? in2, string? out2) =>
            HoursBetween(in1, out1) + HoursBetween(in2, out2);

        /// <summary>
        /// Hours between overtime in and overtime out punches for display on attendance rows.
        /// </summary>
        public static decimal OvertimeHours(string? overtimeIn, string? overtimeOut) =>
            HoursBetween(overtimeIn, overtimeOut);

        /// <summary>
        /// Formats a punch for storage as 12-hour clock text (<c>h:mm tt</c>). Returns null for blank or dash placeholders.
        /// Attendance import clips this result before writing <c>TimeIn1</c> and related columns.
        /// </summary>
        public static string? Clock(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == Empty)
                return null;

            if (TryParseTime(value, out var time))
                return DateTime.Today.Add(time).ToString("h:mm tt", CultureInfo.InvariantCulture);

            return value.Trim();
        }

        /// <summary>
        /// Shows a biometric employee code as five digits (<c>00001</c>) in attendance tables.
        /// Missing codes become <see cref="Empty"/> instead of a single em dash.
        /// </summary>
        public static string EmployeeId(string? code)
        {
            var formatted = EmployeeIds.Format(code);
            return formatted == "—" ? Empty : formatted;
        }

        /// <summary>
        /// Parses a punch string into a <see cref="TimeSpan"/>. Returns false when the cell is blank or not a time.
        /// Shared by display formatting and <see cref="AttendanceRules"/> late/early calculations.
        /// </summary>
        public static bool TryParseTime(string? value, out TimeSpan time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var text = value.Trim();
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                || DateTime.TryParse(text, out dt))
            {
                time = dt.TimeOfDay;
                return true;
            }

            return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out time);
        }
    }
}
