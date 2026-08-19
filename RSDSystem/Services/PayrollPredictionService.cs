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

            var history = BuildHistory(payrolls);
            var budgets = (project.MonthlyBudgets ?? new List<ProjectMonthlyBudget>())
                .OrderBy(b => b.MonthDate)
                .ToList();

            if (budgets.Count == 0)
            {
                return new PayrollPredictionPage
                {
                    ProjectId = project.ProjectId,
                    ProjectName = project.ProjectName ?? "—",
                    GeneratedAt = DateTime.Now,
                    Error = "This project has no monthly allocated budget. Set monthly budgets on the project first."
                };
            }

            if (history.Count < 2)
            {
                return new PayrollPredictionPage
                {
                    ProjectId = project.ProjectId,
                    ProjectName = project.ProjectName ?? "—",
                    GeneratedAt = DateTime.Now,
                    Error = HistoryError(payrolls, history)
                };
            }

            var culture = CultureInfo.GetCultureInfo("en-US");
            var threshold = AnomalyPercent();
            var actuals = new Dictionary<DateTime, decimal>();
            var labels = new Dictionary<DateTime, string>();
            foreach (var month in history)
            {
                var key = MonthKey(month.Month);
                actuals[key] = actuals.TryGetValue(key, out var current)
                    ? current + month.Amount
                    : month.Amount;
                if (!labels.ContainsKey(key) || string.IsNullOrWhiteSpace(labels[key]))
                    labels[key] = month.Label;
            }

            if (actuals.Count < 2)
            {
                return new PayrollPredictionPage
                {
                    ProjectId = project.ProjectId,
                    ProjectName = project.ProjectName ?? "—",
                    GeneratedAt = DateTime.Now,
                    Error = HistoryError(payrolls, history)
                };
            }

            var series = new Dictionary<DateTime, decimal>(actuals);
            var rows = new List<PayrollPredictionRow>();

            foreach (var budgetRow in budgets)
            {
                var predictMonth = MonthKey(budgetRow.MonthDate);
                var previous1 = predictMonth.AddMonths(-2);
                var previous2 = predictMonth.AddMonths(-1);
                if (!series.TryGetValue(previous1, out var amount1) ||
                    !series.TryGetValue(previous2, out var amount2))
                    continue;

                var allocated = budgetRow.Amount;
                var forecast = await ForecastAsync(amount1, amount2, allocated, threshold, cancellationToken);
                var predictLabel = string.IsNullOrWhiteSpace(budgetRow.MonthYear)
                    ? predictMonth.ToString("MMMM yyyy", culture)
                    : budgetRow.MonthYear;

                if (!actuals.ContainsKey(predictMonth))
                {
                    series[predictMonth] = forecast.PredictedPayroll;
                    labels[predictMonth] = predictLabel;
                }

                rows.Add(new PayrollPredictionRow
                {
                    PreviousMonth1 = previous1,
                    PreviousLabel1 = LabelFor(previous1, labels, culture),
                    PreviousAmount1 = amount1,
                    PreviousMonth2 = previous2,
                    PreviousLabel2 = LabelFor(previous2, labels, culture),
                    PreviousAmount2 = amount2,
                    PredictionMonth = predictMonth,
                    PredictionLabel = predictLabel,
                    PredictedPayroll = forecast.PredictedPayroll,
                    AllocatedBudget = allocated,
                    BudgetDifference = forecast.BudgetDifference,
                    ExceedsBudget = forecast.ExceedsBudget,
                    UnusualChange = forecast.UnusualChange,
                    ChangePercent = forecast.ChangePercent,
                    RiskTitle = forecast.RiskTitle,
                    RiskDetail = forecast.RiskDetail
                });
            }

            if (rows.Count == 0)
            {
                return new PayrollPredictionPage
                {
                    ProjectId = project.ProjectId,
                    ProjectName = project.ProjectName ?? "—",
                    GeneratedAt = DateTime.Now,
                    Error = "Need payroll in two months before a project budget month. Generate payroll, then load prediction again."
                };
            }

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

        private static List<MonthlyPayrollTotal> BuildHistory(List<Payroll> payrolls)
        {
            var calendar = SplitAcrossCalendarMonths(payrolls);
            if (calendar.Count >= 2)
                return calendar;

            return payrolls
                .GroupBy(p => (Start: p.PayPeriodStart.Date, End: p.PayPeriodEnd.Date))
                .Select(g =>
                {
                    var used = PreferOfficial(g);
                    return new MonthlyPayrollTotal
                    {
                        Month = new DateTime(g.Key.Start.Year, g.Key.Start.Month, 1),
                        SortDate = g.Key.Start,
                        Amount = used.Sum(p => p.NetPay),
                        Label = PeriodLabel(g.Key.Start, g.Key.End)
                    };
                })
                .OrderBy(m => m.SortDate)
                .ThenBy(m => m.Label)
                .ToList();
        }

        private static List<MonthlyPayrollTotal> SplitAcrossCalendarMonths(List<Payroll> payrolls)
        {
            var culture = CultureInfo.GetCultureInfo("en-US");
            var amounts = new Dictionary<DateTime, decimal>();

            foreach (var period in payrolls.GroupBy(p => (Start: p.PayPeriodStart.Date, End: p.PayPeriodEnd.Date)))
            {
                var total = PreferOfficial(period).Sum(p => p.NetPay);
                var start = period.Key.Start;
                var end = period.Key.End < start ? start : period.Key.End;
                var spanDays = (end - start).TotalDays + 1;
                if (spanDays <= 0)
                    continue;

                for (var month = new DateTime(start.Year, start.Month, 1);
                     month <= end;
                     month = month.AddMonths(1))
                {
                    var monthEnd = month.AddMonths(1).AddDays(-1);
                    var overlapStart = start > month ? start : month;
                    var overlapEnd = end < monthEnd ? end : monthEnd;
                    if (overlapEnd < overlapStart)
                        continue;

                    var share = (decimal)((overlapEnd - overlapStart).TotalDays + 1) / (decimal)spanDays;
                    amounts.TryGetValue(month, out var current);
                    amounts[month] = current + Math.Round(total * share, 2);
                }
            }

            return amounts
                .OrderBy(kv => kv.Key)
                .Select(kv => new MonthlyPayrollTotal
                {
                    Month = kv.Key,
                    SortDate = kv.Key,
                    Amount = kv.Value,
                    Label = kv.Key.ToString("MMMM yyyy", culture)
                })
                .ToList();
        }

        private static List<Payroll> PreferOfficial(IEnumerable<Payroll> rows)
        {
            var list = rows.ToList();
            var approved = list.Where(IsStatus(PayrollStatusOptions.Approved)).ToList();
            if (approved.Count > 0)
                return approved;

            var submitted = list.Where(IsStatus(PayrollStatusOptions.Submitted)).ToList();
            if (submitted.Count > 0)
                return submitted;

            return list;
        }

        private static Func<Payroll, bool> IsStatus(string status) =>
            p => string.Equals(p.Status?.Trim(), status, StringComparison.OrdinalIgnoreCase);

        private static string PeriodLabel(DateTime start, DateTime end)
        {
            var culture = CultureInfo.GetCultureInfo("en-US");
            if (start.Year == end.Year && start.Month == end.Month)
                return start.ToString("MMMM d", culture) + "–" + end.ToString("d, yyyy", culture);
            return start.ToString("MMM d", culture) + " – " + end.ToString("MMM d, yyyy", culture);
        }

        private static string HistoryError(List<Payroll> payrolls, List<MonthlyPayrollTotal> months)
        {
            if (payrolls.Count == 0)
                return "This project has no generated payroll yet. Generate payroll for two periods first.";

            if (months.Count <= 1)
            {
                var label = months.Count == 1 ? months[0].Label : "one period";
                return $"This project currently has payroll in {label} only. Generate payroll for a second month or pay period, then load prediction again.";
            }

            return "Need at least two months of payroll before a prediction can be made.";
        }

        private static DateTime MonthKey(DateTime value) =>
            new(value.Year, value.Month, 1);

        private static string LabelFor(
            DateTime month, Dictionary<DateTime, string> labels, CultureInfo culture)
        {
            var key = MonthKey(month);
            if (labels.TryGetValue(key, out var label) && !string.IsNullOrWhiteSpace(label))
                return label;
            return key.ToString("MMMM yyyy", culture);
        }

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

    public class MonthlyPayrollTotal
    {
        public DateTime Month { get; set; }
        public DateTime SortDate { get; set; }
        public decimal Amount { get; set; }
        public string Label { get; set; } = "";
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
        public decimal BudgetDifference { get; set; }
        public bool ExceedsBudget { get; set; }
        public bool UnusualChange { get; set; }
        public decimal ChangePercent { get; set; }
        public string? RiskTitle { get; set; }
        public string? RiskDetail { get; set; }
    }
}
