using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;

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

            var payrolls = await _db.Set<Payroll>()
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            var approved = payrolls
                .Where(p => string.Equals(p.Status?.Trim(), PayrollStatusOptions.Approved, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var months = approved
                .GroupBy(p => new DateTime(p.PayPeriodEnd.Year, p.PayPeriodEnd.Month, 1))
                .Select(g => new MonthlyPayrollTotal
                {
                    Month = g.Key,
                    Amount = g.Sum(p => p.NetPay)
                })
                .OrderBy(m => m.Month)
                .ToList();

            if (months.Count < 2)
            {
                return new PayrollPredictionPage
                {
                    ProjectId = project.ProjectId,
                    ProjectName = project.ProjectName ?? "—",
                    GeneratedAt = DateTime.Now,
                    Error = "Need at least two months of approved payroll before a prediction can be made."
                };
            }

            var threshold = AnomalyPercent();
            var rows = new List<PayrollPredictionRow>();
            for (var i = 0; i < months.Count - 1; i++)
            {
                var first = months[i];
                var second = months[i + 1];
                var predictMonth = second.Month.AddMonths(1);
                var budget = AllocatedBudget(project, predictMonth);
                var forecast = await ForecastAsync(first.Amount, second.Amount, budget, threshold, cancellationToken);

                rows.Add(new PayrollPredictionRow
                {
                    PreviousMonth1 = first.Month,
                    PreviousAmount1 = first.Amount,
                    PreviousMonth2 = second.Month,
                    PreviousAmount2 = second.Amount,
                    PredictionMonth = predictMonth,
                    PredictedPayroll = forecast.PredictedPayroll,
                    AllocatedBudget = forecast.AllocatedBudget,
                    BudgetDifference = forecast.BudgetDifference,
                    ExceedsBudget = forecast.ExceedsBudget,
                    UnusualChange = forecast.UnusualChange,
                    ChangePercent = forecast.ChangePercent,
                    RiskTitle = forecast.RiskTitle,
                    RiskDetail = forecast.RiskDetail
                });
            }

            rows.Reverse();

            return new PayrollPredictionPage
            {
                ProjectId = project.ProjectId,
                ProjectName = project.ProjectName ?? "—",
                GeneratedAt = DateTime.Now,
                Rows = rows
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

        private decimal AnomalyPercent()
        {
            var raw = _config["Prediction:AnomalyChangePercent"];
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value > 0)
                return value;
            return PayrollPredictionEngine.DefaultAnomalyPercent;
        }

        private static decimal AllocatedBudget(Project project, DateTime month)
        {
            var match = project.MonthlyBudgets?
                .FirstOrDefault(b => b.MonthDate.Year == month.Year && b.MonthDate.Month == month.Month);
            if (match != null)
                return match.Amount;

            if (project.PayrollBudget.HasValue && project.PayrollBudget.Value > 0)
                return project.PayrollBudget.Value;

            return 0;
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

    public class MonthlyPayrollTotal
    {
        public DateTime Month { get; set; }
        public decimal Amount { get; set; }
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
        public decimal PreviousAmount1 { get; set; }
        public DateTime PreviousMonth2 { get; set; }
        public decimal PreviousAmount2 { get; set; }
        public DateTime PredictionMonth { get; set; }
        public decimal PredictedPayroll { get; set; }
        public decimal AllocatedBudget { get; set; }
        public decimal BudgetDifference { get; set; }
        public bool ExceedsBudget { get; set; }
        public bool UnusualChange { get; set; }
        public decimal ChangePercent { get; set; }
        public string? RiskTitle { get; set; }
        public string? RiskDetail { get; set; }
    }
}
