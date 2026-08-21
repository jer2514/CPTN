namespace RSDSystem.Helpers
{
    public static class EmployeeRates
    {
        public const int HoursPerDay = 8;

        public static decimal HourlyFromDaily(decimal dailyRate) =>
            dailyRate <= 0
                ? 0
                : Math.Round(dailyRate / HoursPerDay, 2, MidpointRounding.AwayFromZero);
    }
}
