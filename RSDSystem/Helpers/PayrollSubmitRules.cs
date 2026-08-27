using RSDSystem.Models;

namespace RSDSystem.Helpers
{
    public static class PayrollSubmitRules
    {
        public const string PendingCorrectionMessage =
            "This employee has a pending attendance correction. Wait for admin approval before submitting payroll.";

        public const string AlreadyClosedMessage =
            "Submitted or approved payroll cannot be changed.";

        public static string? BlockReason(string? status, bool hasPendingCorrection)
        {
            var value = (status ?? "").Trim();
            if (string.Equals(value, PayrollStatusOptions.Submitted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, PayrollStatusOptions.Approved, StringComparison.OrdinalIgnoreCase))
                return AlreadyClosedMessage;

            if (hasPendingCorrection)
                return PendingCorrectionMessage;

            return null;
        }
    }
}
