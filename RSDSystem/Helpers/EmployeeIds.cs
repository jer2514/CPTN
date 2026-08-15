namespace RSDSystem.Helpers
{
    /// <summary>
    /// Biometric employee IDs: 00001, 00002, ...
    /// Kept in its own type so callers do not depend on extra IdFormatter members.
    /// </summary>
    public static class EmployeeIds
    {
        public static string Format(string? code)
        {
            var seq = Sequence(code);
            return seq.HasValue
                ? seq.Value.ToString("D5")
                : (string.IsNullOrWhiteSpace(code) ? "—" : code.Trim());
        }

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
