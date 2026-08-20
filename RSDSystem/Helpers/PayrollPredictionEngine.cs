using System.Text.Json.Serialization;

namespace RSDSystem.Helpers
{
    public class PayrollForecastInput
    {
        [JsonPropertyName("previousPayroll1")]
        public decimal PreviousPayroll1 { get; set; }

        [JsonPropertyName("previousPayroll2")]
        public decimal PreviousPayroll2 { get; set; }

        [JsonPropertyName("allocatedBudget")]
        public decimal AllocatedBudget { get; set; }

        [JsonPropertyName("anomalyPercent")]
        public decimal AnomalyPercent { get; set; } = PayrollPredictionEngine.DefaultAnomalyPercent;
    }

    public class PayrollForecastResult
    {
        public decimal PredictedPayroll { get; set; }
        public decimal AllocatedBudget { get; set; }
        public decimal BudgetDifference { get; set; }
        public bool ExceedsBudget { get; set; }
        public bool UnusualChange { get; set; }
        public decimal ChangePercent { get; set; }
        public string? RiskTitle { get; set; }
        public string? RiskDetail { get; set; }
    }

    /// <summary>
    /// Proposed prediction API math from the capstone:
    /// next month = linear trend of the previous two payroll months,
    /// then rule-based budget exceedance and unusual-change flags.
    /// </summary>
    public static class PayrollPredictionEngine
    {
        public const decimal DefaultAnomalyPercent = 25m;

        public static PayrollForecastResult Forecast(PayrollForecastInput input)
        {
            var month1 = Round(input.PreviousPayroll1);
            var month2 = Round(input.PreviousPayroll2);
            var budget = Round(input.AllocatedBudget);
            var threshold = input.AnomalyPercent > 0 ? input.AnomalyPercent : DefaultAnomalyPercent;

            var predicted = Round(month2 + (month2 - month1));
            if (predicted < 0)
                predicted = 0;

            var difference = Round(predicted - budget);
            var exceeds = predicted > budget;

            decimal changePercent;
            if (month1 == 0)
                changePercent = month2 == 0 ? 0 : 100;
            else
                changePercent = Round(Math.Abs(month2 - month1) / month1 * 100);

            var unusual = changePercent >= threshold;

            string? riskTitle = null;
            string? riskDetail = null;
            if (exceeds)
            {
                riskTitle = "Budget Exceeding Risk";
                riskDetail = "Predicted payroll exceeds the allocated budget.";
            }
            else if (unusual)
            {
                riskTitle = "Unusual Payroll Change";
                riskDetail = month2 > month1
                    ? "Payroll rose sharply between the last two months."
                    : "Payroll dropped sharply between the last two months.";
            }

            return new PayrollForecastResult
            {
                PredictedPayroll = predicted,
                AllocatedBudget = budget,
                BudgetDifference = difference,
                ExceedsBudget = exceeds,
                UnusualChange = unusual,
                ChangePercent = changePercent,
                RiskTitle = riskTitle,
                RiskDetail = riskDetail
            };
        }

        private static decimal Round(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
