using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Validation;

namespace RSDSystem.Services
{
    public class PayrollPredictionService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly PayrollDbContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpClientFactory _httpFactory;

        public PayrollPredictionService(
            PayrollDbContext db,
            IConfiguration config,
            IHttpClientFactory httpFactory)
        {
            _db = db;
            _config = config;
            _httpFactory = httpFactory;
        }

        public async Task<PayrollPredictionPage> LoadAsync(int projectId, CancellationToken cancellationToken = default)
        {
            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.MonthlyBudgets)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId, cancellationToken);
            if (project == null)
                return new PayrollPredictionPage { Error = "Project not found." };

            var culture = CultureInfo.GetCultureInfo("en-US");
            var budgets = (project.MonthlyBudgets ?? new List<ProjectMonthlyBudget>())
                .Where(b => DateRules.IsUsableDate(b.MonthDate))
                .OrderBy(b => b.MonthDate)
                .ThenBy(b => b.Id)
                .ToList();

            var payrolls = await _db.Payrolls.AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            var generated = new SortedDictionary<DateTime, decimal>();
            foreach (var slip in payrolls)
            {
                var month = MonthKey(DateRules.IsUsableDate(slip.PayPeriodEnd)
                    ? slip.PayPeriodEnd
                    : slip.PayPeriodStart);
                generated[month] = generated.TryGetValue(month, out var current)
                    ? current + slip.NetPay
                    : slip.NetPay;
            }

            if (generated.Count < 2)
            {
                return new PayrollPredictionPage
                {
                    ProjectId = project.ProjectId,
                    ProjectName = project.ProjectName ?? "—",
                    GeneratedAt = DateTime.Now,
                    Error = generated.Count == 0
                        ? "This project has no generated payroll yet. Generate payroll for two months first."
                        : "Need two months of generated payroll before the next month can be predicted."
                };
            }

            var months = generated.Keys.ToList();
            var month1 = months[^2];
            var month2 = months[^1];
            var amount1 = generated[month1];
            var amount2 = generated[month2];
            var predictMonth = month2.AddMonths(1);
            var allocatedRow = budgets.LastOrDefault(b => MonthKey(b.MonthDate) == predictMonth);
            var hasAllocated = allocatedRow != null;
            var allocated = hasAllocated ? allocatedRow!.Amount : (decimal?)null;
            var forecast = await ForecastAsync(
                amount1, amount2, allocated ?? 0, AnomalyPercent(), cancellationToken);

            var predicted = forecast.PredictedPayroll;
            var exceeds = hasAllocated && predicted > allocatedRow!.Amount;
            var difference = hasAllocated
                ? predicted - allocatedRow!.Amount
                : predicted - amount2;
            var predictLabel = hasAllocated
                ? BudgetLabel(allocatedRow!, culture)
                : predictMonth.ToString("MMMM yyyy", culture);

            string? riskTitle = null;
            string? riskDetail = null;
            if (exceeds)
            {
                riskTitle = "Budget Exceeding Risk";
                riskDetail = "The predicted amount for " + predictLabel
                    + " exceeds the allocated budget of ₱" + allocatedRow!.Amount.ToString("N2", culture) + ".";
            }
            else if (forecast.UnusualChange)
            {
                riskTitle = "Unusual Payroll Change";
                riskDetail = amount2 > amount1
                    ? "Generated payroll rose sharply between the last two months."
                    : "Generated payroll dropped sharply between the last two months.";
            }

            return new PayrollPredictionPage
            {
                ProjectId = project.ProjectId,
                ProjectName = project.ProjectName ?? "—",
                GeneratedAt = DateTime.Now,
                Rows =
                {
                    new PayrollPredictionRow
                    {
                        PreviousMonth1 = month1,
                        PreviousLabel1 = month1.ToString("MMMM yyyy", culture),
                        PreviousAmount1 = amount1,
                        PreviousMonth2 = month2,
                        PreviousLabel2 = month2.ToString("MMMM yyyy", culture),
                        PreviousAmount2 = amount2,
                        PredictionMonth = predictMonth,
                        PredictionLabel = predictLabel,
                        PredictedPayroll = predicted,
                        AllocatedBudget = allocated ?? 0,
                        HasAllocatedBudget = hasAllocated,
                        BudgetDifference = Math.Round(difference, 2, MidpointRounding.AwayFromZero),
                        ExceedsBudget = exceeds,
                        UnusualChange = forecast.UnusualChange,
                        ChangePercent = forecast.ChangePercent,
                        RiskTitle = riskTitle,
                        RiskDetail = riskDetail
                    }
                }
            };
        }

        public async Task<PayrollForecastResult> ForecastAsync(
            decimal previous1,
            decimal previous2,
            decimal allocatedBudget,
            decimal? anomalyPercent = null,
            CancellationToken cancellationToken = default)
        {
            var input = new PayrollForecastInput
            {
                PreviousPayroll1 = previous1,
                PreviousPayroll2 = previous2,
                AllocatedBudget = allocatedBudget,
                AnomalyPercent = anomalyPercent ?? AnomalyPercent()
            };

            var remote = await TryRemoteForecastAsync(input, cancellationToken);
            return remote ?? PayrollPredictionEngine.Forecast(input);
        }

        private async Task<PayrollForecastResult?> TryRemoteForecastAsync(
            PayrollForecastInput input, CancellationToken cancellationToken)
        {
            var apiUrl = (_config["Prediction:ApiUrl"] ?? "").Trim();
            if (apiUrl.Length == 0)
                return null;

            try
            {
                var client = _httpFactory.CreateClient("PayrollPrediction");
                using var response = await client.PostAsJsonAsync(
                    Combine(apiUrl, "/predict"),
                    new
                    {
                        previous_payroll_1 = input.PreviousPayroll1,
                        previous_payroll_2 = input.PreviousPayroll2,
                        allocated_budget = input.AllocatedBudget,
                        anomaly_percent = input.AnomalyPercent,
                        previousPayroll1 = input.PreviousPayroll1,
                        previousPayroll2 = input.PreviousPayroll2,
                        allocatedBudget = input.AllocatedBudget,
                        anomalyPercent = input.AnomalyPercent
                    },
                    JsonOptions,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                var payload = await response.Content.ReadFromJsonAsync<RemoteForecastDto>(JsonOptions, cancellationToken);
                if (payload == null)
                    return null;

                var predicted = payload.PredictedPayroll;
                var budget = payload.AllocatedBudget ?? input.AllocatedBudget;
                var difference = payload.BudgetDifference ?? Math.Round(predicted - budget, 2);
                var exceeds = payload.ExceedsBudget ?? predicted > budget;

                return new PayrollForecastResult
                {
                    PredictedPayroll = predicted,
                    AllocatedBudget = budget,
                    BudgetDifference = difference,
                    ExceedsBudget = exceeds,
                    UnusualChange = payload.UnusualChange ?? false,
                    ChangePercent = payload.ChangePercent ?? 0,
                    RiskTitle = payload.RiskTitle,
                    RiskDetail = payload.RiskDetail
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine("Prediction API fallback: " + ex.Message);
                return null;
            }
        }

        private static string BudgetLabel(ProjectMonthlyBudget row, CultureInfo culture)
        {
            if (!string.IsNullOrWhiteSpace(row.MonthYear))
                return row.MonthYear.Trim();
            return MonthKey(row.MonthDate).ToString("MMMM yyyy", culture);
        }

        private static DateTime MonthKey(DateTime value) =>
            new(value.Year, value.Month, 1);

        private decimal AnomalyPercent()
        {
            var raw = _config["Prediction:AnomalyChangePercent"];
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value > 0)
                return value;
            return PayrollPredictionEngine.DefaultAnomalyPercent;
        }

        private static string Combine(string baseUrl, string path)
        {
            return baseUrl.TrimEnd('/') + path;
        }

        private sealed class RemoteForecastDto
        {
            public decimal PredictedPayroll { get; set; }

            [JsonPropertyName("predicted_payroll")]
            public decimal PredictedPayrollSnake
            {
                get => PredictedPayroll;
                set => PredictedPayroll = value;
            }

            public decimal? AllocatedBudget { get; set; }

            [JsonPropertyName("allocated_budget")]
            public decimal? AllocatedBudgetSnake
            {
                get => AllocatedBudget;
                set => AllocatedBudget = value;
            }

            public decimal? BudgetDifference { get; set; }

            [JsonPropertyName("budget_difference")]
            public decimal? BudgetDifferenceSnake
            {
                get => BudgetDifference;
                set => BudgetDifference = value;
            }

            public bool? ExceedsBudget { get; set; }

            [JsonPropertyName("exceeds_budget")]
            public bool? ExceedsBudgetSnake
            {
                get => ExceedsBudget;
                set => ExceedsBudget = value;
            }

            public bool? UnusualChange { get; set; }

            [JsonPropertyName("unusual_change")]
            public bool? UnusualChangeSnake
            {
                get => UnusualChange;
                set => UnusualChange = value;
            }

            public decimal? ChangePercent { get; set; }

            [JsonPropertyName("change_percent")]
            public decimal? ChangePercentSnake
            {
                get => ChangePercent;
                set => ChangePercent = value;
            }

            public string? RiskTitle { get; set; }

            [JsonPropertyName("risk_title")]
            public string? RiskTitleSnake
            {
                get => RiskTitle;
                set => RiskTitle = value;
            }

            public string? RiskDetail { get; set; }

            [JsonPropertyName("risk_detail")]
            public string? RiskDetailSnake
            {
                get => RiskDetail;
                set => RiskDetail = value;
            }
        }
    }

    public class PayrollPredictionPage
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "—";
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        public string? Error { get; set; }
        public List<PayrollPredictionRow> Rows { get; set; } = new();
    }

    public class PayrollPredictionRow
    {
        public DateTime PreviousMonth1 { get; set; }
        public string PreviousLabel1 { get; set; } = "";
        public decimal PreviousAmount1 { get; set; }
        public DateTime PreviousMonth2 { get; set; }
        public string PreviousLabel2 { get; set; } = "";
        public decimal PreviousAmount2 { get; set; }
        public DateTime PredictionMonth { get; set; }
        public string PredictionLabel { get; set; } = "";
        public decimal PredictedPayroll { get; set; }
        public decimal AllocatedBudget { get; set; }
        public bool HasAllocatedBudget { get; set; }
        public decimal BudgetDifference { get; set; }
        public bool ExceedsBudget { get; set; }
        public bool UnusualChange { get; set; }
        public decimal ChangePercent { get; set; }
        public string? RiskTitle { get; set; }
        public string? RiskDetail { get; set; }
    }
}
