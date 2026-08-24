namespace RSDSystem.Helpers
{
    /// <summary>Display Staff IDs as 26-0001. Employee IDs go through EmployeeIds (00001).</summary>
    public static class IdFormatter
    {
        // User codes stay year-prefixed: "260001" → "26-0001"
        /// <summary>
        /// Formats a staff/user code as year-hyphen-sequence (<c>260001</c> → <c>26-0001</c>).
        /// Short or blank codes are returned as-is (or an em dash). Employee IDs should use <see cref="FormatEmployee"/> instead.
        /// </summary>
        public static string Format(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length < 3)
                return code ?? "—";

            var year = code.Substring(0, 2);
            var seq = code.Substring(2);
            return $"{year}-{seq}";
        }

        /// <summary>
        /// Forwards to <see cref="EmployeeIds.Format"/> so older payroll screens can still call IdFormatter for employee codes.
        /// </summary>
        public static string FormatEmployee(string? code) => EmployeeIds.Format(code);

        /// <summary>
        /// Forwards to <see cref="EmployeeIds.Sequence"/> for attendance matching from code that still imports IdFormatter.
        /// </summary>
        public static int? EmployeeSequence(string? code) => EmployeeIds.Sequence(code);
    }
}
