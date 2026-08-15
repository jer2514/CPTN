namespace RSDSystem.Helpers
{
    public static class IdFormatter
    {
        // User codes stay year-prefixed: "260001" → "26-0001"
        public static string Format(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length < 3)
                return code ?? "—";

            var year = code.Substring(0, 2);
            var seq = code.Substring(2);
            return $"{year}-{seq}";
        }

        // Employee biometric IDs: "00001", "00002", ...
        public static string FormatEmployee(string? code)
        {
            var seq = EmployeeSequence(code);
            return seq.HasValue ? seq.Value.ToString("D5") : (string.IsNullOrWhiteSpace(code) ? "—" : code.Trim());
        }

        public static int? EmployeeSequence(string? code)
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
