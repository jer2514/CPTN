namespace RSDSystem.Helpers
{
    public static class CashAdvanceDeduction
    {
        public readonly record struct Step(int CashAdvanceId, decimal DeductedAmount, decimal LeftoverAmount);

        /// <summary>
        /// FIFO: take whole pending rows while they fit, then split the next row
        /// when payroll cash-advance is smaller than that row. Never skip a larger
        /// row to deduct a later smaller one — that left pay deducted with no
        /// matching paid advance.
        /// </summary>
        public static List<Step> Plan(
            IEnumerable<(int Id, decimal Amount)> pendingOldestFirst,
            decimal payrollCashAdvance)
        {
            var steps = new List<Step>();
            var remaining = decimal.Round(payrollCashAdvance, 2);
            if (remaining <= 0)
                return steps;

            foreach (var (id, amount) in pendingOldestFirst)
            {
                if (remaining <= 0)
                    break;

                var rowAmount = decimal.Round(amount, 2);
                if (rowAmount <= 0)
                    continue;

                if (rowAmount <= remaining)
                {
                    steps.Add(new Step(id, rowAmount, 0));
                    remaining -= rowAmount;
                    continue;
                }

                steps.Add(new Step(id, remaining, rowAmount - remaining));
                break;
            }

            return steps;
        }
    }
}
