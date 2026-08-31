using RSDSystem.Models;

namespace RSDSystem.Helpers
{
    /// <summary>
    /// Pending cash advances are deducted on "the next payroll" only.
    /// An in-flight slip that already snapshotted the pending amount must
    /// reserve it so a later schedule cannot charge the same balance again.
    /// </summary>
    public static class CashAdvanceReservation
    {
        public static decimal AvailableForPayroll(
            decimal pendingAmount,
            IEnumerable<(string Status, decimal CashAdvance)> otherPayrolls)
        {
            if (pendingAmount <= 0)
                return 0;

            var reserved = 0m;
            foreach (var payroll in otherPayrolls)
            {
                if (PayrollStatusOptions.IsApproved(payroll.Status))
                    continue;
                if (payroll.CashAdvance > 0)
                    reserved += payroll.CashAdvance;
            }

            return pendingAmount > reserved ? pendingAmount - reserved : 0;
        }
    }
}
