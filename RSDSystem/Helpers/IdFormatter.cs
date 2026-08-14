namespace RSDSystem.Helpers
{
    public static class IdFormatter
    {
        // "260001" → "26-0001"
        public static string Format(string? code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length < 3)
                return code ?? "—";

            var year = code.Substring(0, 2);
            var seq = code.Substring(2);
            return $"{year}-{seq}";
        }
    }
}