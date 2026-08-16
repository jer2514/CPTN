using System.Globalization;

namespace RSDSystem.Helpers
{
    public static class AttendanceDisplay
    {
        public const string Empty = "——";

        public static string HtmlTime(string? value)
        {
            if (!TryParseTime(value, out var time))
                return "";
            return DateTime.Today.Add(time).ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

        public static string LongDate(DateTime? date) =>
            date?.ToString("MMMM dd, yyyy", English) ?? Empty;

        public static string DayLabel(DateTime date) =>
            date.ToString("dd", English) + " " + date.ToString("ddd", English)[..2];

        public static decimal HoursBetween(string? timeIn, string? timeOut)
        {
            if (!TryParseTime(timeIn, out var start) || !TryParseTime(timeOut, out var end))
                return 0;

            var span = end - start;
            if (span < TimeSpan.Zero)
                span += TimeSpan.FromDays(1);
            return Math.Round((decimal)span.TotalHours, 2);
        }

        public static decimal RegularHours(string? in1, string? out1, string? in2, string? out2) =>
            HoursBetween(in1, out1) + HoursBetween(in2, out2);

        public static decimal OvertimeHours(string? overtimeIn, string? overtimeOut) =>
            HoursBetween(overtimeIn, overtimeOut);

        public static string? Clock(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == Empty)
                return null;

            if (TryParseTime(value, out var time))
                return DateTime.Today.Add(time).ToString("h:mm tt", CultureInfo.InvariantCulture);

            return value.Trim();
        }

        public static string EmployeeId(string? code)
        {
            var formatted = EmployeeIds.Format(code);
            return formatted == "—" ? Empty : formatted;
        }

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
