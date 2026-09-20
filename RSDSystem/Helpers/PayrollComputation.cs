using RSDSystem.Models;

namespace RSDSystem.Helpers
{
    public static class PayrollComputation
    {
        public static decimal PaidRegularHours(Payroll payroll)
        {
            if (payroll.RegularHours > 0)
                return payroll.RegularHours;
            return payroll.RegularDaysWorked * 8m;
        }

        public static (decimal RegularPay, decimal OvertimePay, decimal GrossPay, decimal NetPay) Compute(
            decimal ratePerHour, decimal regularHours, decimal overtimeHours, decimal cashAdvance)
        {
            var regularPay = Math.Round(ratePerHour * regularHours, 2);
            var overtimePay = Math.Round(ratePerHour * overtimeHours, 2);
            var gross = regularPay + overtimePay;
            var net = Math.Round(gross - cashAdvance, 2);
            if (net < 0) net = 0;
            return (regularPay, overtimePay, gross, net);
        }

        /// <summary>
        /// Pending cash advances deduct from this slip, but never more than gross pay.
        /// </summary>
        public static decimal CappedCashAdvance(decimal pendingAmount, decimal grossBeforeDeduction)
        {
            if (pendingAmount <= 0 || grossBeforeDeduction <= 0)
                return 0;
            return pendingAmount > grossBeforeDeduction ? grossBeforeDeduction : pendingAmount;
        }
    }
}
