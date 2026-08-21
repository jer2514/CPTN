using System.Globalization;

namespace RSDSystem.Helpers
{
    public static class PhilippinesTime
    {
        public static readonly TimeZoneInfo Zone = Resolve();
        private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

        public static DateTime Now =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

        public static DateTime Today => Now.Date;

        public static string FormatDate(DateTime value) =>
            ToLocal(value).ToString("MM-dd-yy", English);

        public static string FormatDateTime(DateTime value) =>
            ToLocal(value).ToString("MM-dd-yy h:mm tt", English);

        public static string FormatLongDate(DateTime value) =>
            ToLocal(value).ToString("MMMM dd, yyyy", English);

        public static string FormatLongDateTime(DateTime value) =>
            ToLocal(value).ToString("MMMM dd, yyyy h:mm tt", English);

        public static DateTime ToLocal(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
                return TimeZoneInfo.ConvertTimeFromUtc(value, Zone);
            if (value.Kind == DateTimeKind.Local)
                return TimeZoneInfo.ConvertTime(value, Zone);
            return value;
        }

        private static TimeZoneInfo Resolve()
        {
            foreach (var id in new[] { "Asia/Manila", "Singapore Standard Time", "Taipei Standard Time" })
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            return TimeZoneInfo.CreateCustomTimeZone("PHT", TimeSpan.FromHours(8), "Philippines Time", "PHT");
        }
    }
}
