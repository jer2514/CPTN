using System.Globalization;

namespace RSDSystem.Helpers
{
    public static class AttendanceDisplay
    {
        public const string Empty = "——";

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
