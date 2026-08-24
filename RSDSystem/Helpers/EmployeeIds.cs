namespace RSDSystem.Helpers
{
    /// <summary>
    /// Biometric employee IDs: 00001, 00002, ...
    /// Kept in its own type so callers do not depend on extra IdFormatter members.
    /// </summary>
    public static class EmployeeIds
    {
        /// <summary>
        /// Displays a biometric employee code as five digits (<c>00001</c>). Non-numeric leftover text is returned trimmed.
        /// Used on attendance tables, payroll slips, and employee lists so staff IDs and employee IDs stay distinct.
        /// </summary>
        public static string Format(string? code)
        {
            var seq = Sequence(code);
            return seq.HasValue
                ? seq.Value.ToString("D5")
                : (string.IsNullOrWhiteSpace(code) ? "—" : code.Trim());
        }

        /// <summary>
        /// Extracts the numeric sequence from a biometric or year-prefixed code so import can match file User IDs to Employees.
        /// Six-or-more-digit codes keep only the last four (old <c>YY + seq</c> style, for example 260001 → 1).
        /// </summary>
        public static int? Sequence(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            var digits = new string(code.Where(char.IsDigit).ToArray());
            if (digits.Length == 0)
                return null;

            // Old year-prefixed codes (YY + 4-digit sequence), e.g. "260001"
            if (digits.Length >= 6 && int.TryParse(digits[^4..], out var oldSeq))
                return oldSeq;

            return int.TryParse(digits, out var seq) ? seq : null;
        }
    }
}
