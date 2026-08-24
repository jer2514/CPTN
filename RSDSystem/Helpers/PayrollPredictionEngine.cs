using System.Text.Json.Serialization;

namespace RSDSystem.Helpers
{
    /// <summary>Numbers sent into the next-month forecast: last two payroll/budget amounts plus anomaly threshold.</summary>
    public class PayrollForecastInput
    {
        /// <summary>Older of the two previous month amounts (month − 2).</summary>
        [JsonPropertyName("previousPayroll1")]
        public decimal PreviousPayroll1 { get; set; }

        /// <summary>Newer of the two previous month amounts (month − 1).</summary>
        [JsonPropertyName("previousPayroll2")]
        public decimal PreviousPayroll2 { get; set; }

        /// <summary>Admin-allocated budget for the month being predicted; compared to the forecast.</summary>
        [JsonPropertyName("allocatedBudget")]
        public decimal AllocatedBudget { get; set; }

        /// <summary>Percent jump between the two previous months that counts as an unusual change (default 25).</summary>
        [JsonPropertyName("anomalyPercent")]
        public decimal AnomalyPercent { get; set; } = PayrollPredictionEngine.DefaultAnomalyPercent;
    }

    /// <summary>Forecast numbers and risk flags shown on Admin Payroll Prediction and used for budget-exceed notifications.</summary>
    public class PayrollForecastResult
    {
        /// <summary>Linear-trend estimate for next month (cannot go below zero).</summary>
        public decimal PredictedPayroll { get; set; }

        /// <summary>Allocated budget copied from input (or the Python API response).</summary>
        public decimal AllocatedBudget { get; set; }

        /// <summary>Predicted minus allocated; positive means over budget.</summary>
        public decimal BudgetDifference { get; set; }

        /// <summary>True when predicted payroll is greater than allocated budget.</summary>
        public bool ExceedsBudget { get; set; }

        /// <summary>True when the month-to-month change percent is at or above the anomaly threshold.</summary>
        public bool UnusualChange { get; set; }

        /// <summary>Absolute percent change from previousPayroll1 to previousPayroll2.</summary>
        public decimal ChangePercent { get; set; }

        /// <summary>Short risk heading such as Budget Exceeding Risk, or null when there is no flag.</summary>
        public string? RiskTitle { get; set; }

        /// <summary>Sentence shown under the risk heading on the prediction page and in admin notifications.</summary>
        public string? RiskDetail { get; set; }
        public string Engine { get; set; } = "local";
        public string? Model { get; set; }
    }

    /// <summary>
    /// Proposed prediction API math from the capstone:
    /// next month = linear trend of the previous two payroll months,
    /// then rule-based budget exceedance and unusual-change flags.
    /// </summary>
    public static class PayrollPredictionEngine
    {
        public const decimal DefaultAnomalyPercent = 25m;

        /// <summary>
        /// Computes next-month payroll as month2 + (month2 − month1), then sets ExceedsBudget and UnusualChange.
        /// PayrollPredictionService calls this when the optional Python API is down; Admin Prediction and NotifyPayrollAlertsAsync consume the result.
        /// </summary>
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
                riskDetail = "The predicted amount for next month exceeds the allocated budget.";
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
                RiskDetail = riskDetail,
                Engine = "local",
                Model = "linear-trend"
            };
        }

        /// <summary>
        /// Rounds money to two decimal places away from zero so ₱1.005 becomes ₱1.01 in the forecast.
        /// </summary>
        private static decimal Round(decimal value) =>
            Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
