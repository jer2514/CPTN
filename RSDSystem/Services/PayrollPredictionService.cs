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
    /// <summary>
    /// Next-month payroll estimate.
    /// LoadAsync(projectId): needs two finished months of Approved payroll.
    /// Uses previous month totals → linear trend (PayrollPredictionEngine) or optional Python API.
    /// Flags ExceedsBudget if predicted &gt; allocated monthly budget, UnusualChange if jump is large.
    /// Admin Payroll/Prediction page calls this via GetPrediction.
    /// </summary>
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

        /// <summary>
        /// Stores DbContext, Prediction:ApiUrl config, and the named HttpClient used to call the optional Python forecast API.
        /// </summary>
        public PayrollPredictionService(
            PayrollDbContext db,
            IConfiguration config,
            IHttpClientFactory httpFactory)
        {
            _db = db;
            _config = config;
            _httpFactory = httpFactory;
        }

        public async Task<PayrollPredictionPage> LoadAsync(
            int projectId, bool persistHistory = false, CancellationToken cancellationToken = default)
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
                .Where(p => p.ProjectId == projectId && p.Status == PayrollStatusOptions.Approved)
                .ToListAsync(cancellationToken);

            var today = PhilippinesTime.Today;
            var allMonths = MonthTotals(payrolls);
            var generated = CompletedMonthTotals(allMonths, today);
            var currentRows = new List<PayrollPredictionRow>();
            string? error = null;

            if (generated.Count < 2)
            {
                error = allMonths.Count == 0
                    ? "This project has no approved payroll yet. Approve payroll for two finished months first."
                    : generated.Count == 0
                        ? "Wait until the whole payroll month is finished before the next month can be predicted."
                        : "Need two finished months of approved payroll before the next month can be predicted.";
            }
            else
            {
                var months = generated.Keys.ToList();
                for (var i = 0; i < months.Count - 1; i++)
                {
                    var month1 = months[i];
                    var month2 = months[i + 1];
                    currentRows.Add(await BuildForecastRowAsync(
                        budgets,
                        month1,
                        generated[month1],
                        month2,
                        generated[month2],
                        culture,
                        cancellationToken));
                }

                if (persistHistory)
                    await SaveHistoryAsync(project.ProjectId, currentRows, cancellationToken);
            }

            var history = await _db.PayrollPredictionHistories.AsNoTracking()
                .Where(h => h.ProjectId == project.ProjectId)
                .OrderByDescending(h => h.GeneratedAt)
                .ToListAsync(cancellationToken);

            var currentKeys = currentRows.Select(SnapshotKey).ToHashSet(StringComparer.Ordinal);
            var currentWindows = currentRows.Select(WindowKey).ToHashSet(StringComparer.Ordinal);
            var shownPreviousWindows = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<PayrollPredictionRow>(currentRows);
            foreach (var saved in history)
            {
                var previous = ToRow(saved, culture, isPrevious: true);
                if (!DateRules.IsCalendarMonthFinished(previous.PreviousMonth2, PhilippinesTime.Today))
                    continue;
                if (currentKeys.Contains(SnapshotKey(previous)))
                    continue;
                if (currentWindows.Contains(WindowKey(previous)))
                    continue;
                if (!shownPreviousWindows.Add(WindowKey(previous)))
                    continue;
                rows.Add(previous);
            }

            rows = rows
                .OrderByDescending(r => r.PredictionMonth)
                .ThenBy(r => r.IsPrevious)
                .ThenByDescending(r => r.GeneratedAt)
                .ToList();

            var latestCurrent = currentRows.LastOrDefault();
            return new PayrollPredictionPage
            {
                ProjectId = project.ProjectId,
                ProjectName = project.ProjectName ?? "—",
                GeneratedAt = PhilippinesTime.Now,
                Engine = latestCurrent?.Engine ?? rows.LastOrDefault(r => !r.IsPrevious)?.Engine ?? rows.FirstOrDefault()?.Engine ?? "local",
                Model = latestCurrent != null ? "linear-trend" : rows.FirstOrDefault()?.Engine,
                Error = error,
                Rows = rows
            };
        }

        private async Task<PayrollPredictionRow> BuildForecastRowAsync(
            List<ProjectMonthlyBudget> budgets,
            DateTime month1,
            decimal amount1,
            DateTime month2,
            decimal amount2,
            CultureInfo culture,
            CancellationToken cancellationToken)
        {
            var predictMonth = month2.AddMonths(1);
            var allocatedRow = budgets.LastOrDefault(b => MonthKey(b.MonthDate) == predictMonth);
            var hasAllocated = allocatedRow != null;
            var allocated = hasAllocated ? allocatedRow!.Amount : 0m;
            var forecast = await ForecastAsync(
                amount1, amount2, allocated, AnomalyPercent(), cancellationToken);

            var predicted = forecast.PredictedPayroll;
            var exceeds = hasAllocated && predicted > allocated;
            var difference = hasAllocated ? predicted - allocated : predicted - amount2;
            var predictLabel = hasAllocated
                ? BudgetLabel(allocatedRow!, culture)
                : predictMonth.ToString("MMMM yyyy", culture);

            string? riskTitle = null;
            string? riskDetail = null;
            if (exceeds)
            {
                riskTitle = "Budget Exceeding Risk";
                riskDetail = "Predicted payroll exceeds the allocated budget.";
            }
            else if (forecast.UnusualChange)
            {
                riskTitle = "Unusual Payroll Change";
                riskDetail = amount2 > amount1
                    ? "Generated payroll rose sharply between these two months."
                    : "Generated payroll dropped sharply between these two months.";
            }

            return new PayrollPredictionRow
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
                AllocatedBudget = allocated,
                HasAllocatedBudget = hasAllocated,
                BudgetDifference = Math.Round(difference, 2, MidpointRounding.AwayFromZero),
                ExceedsBudget = exceeds,
                UnusualChange = forecast.UnusualChange,
                ChangePercent = forecast.ChangePercent,
                RiskTitle = riskTitle,
                RiskDetail = riskDetail,
                GeneratedAt = PhilippinesTime.Now,
                Engine = forecast.Engine,
                IsPrevious = false
            };
        }

        private async Task SaveHistoryAsync(
            int projectId, List<PayrollPredictionRow> rows, CancellationToken cancellationToken)
        {
            if (rows.Count == 0)
                return;

            var cutoff = PhilippinesTime.Now.AddMinutes(-2);
            var recent = await _db.PayrollPredictionHistories
                .Where(h => h.ProjectId == projectId && h.GeneratedAt >= cutoff)
                .ToListAsync(cancellationToken);
            var recentKeys = recent.Select(SnapshotKey).ToHashSet(StringComparer.Ordinal);

            foreach (var row in rows)
            {
                if (!recentKeys.Add(SnapshotKey(row)))
                    continue;

                _db.PayrollPredictionHistories.Add(new PayrollPredictionHistory
                {
                    ProjectId = projectId,
                    PreviousMonth1 = row.PreviousMonth1,
                    PreviousAmount1 = row.PreviousAmount1,
                    PreviousMonth2 = row.PreviousMonth2,
                    PreviousAmount2 = row.PreviousAmount2,
                    PredictionMonth = row.PredictionMonth,
                    PredictionLabel = row.PredictionLabel,
                    PredictedPayroll = row.PredictedPayroll,
                    AllocatedBudget = row.AllocatedBudget,
                    HasAllocatedBudget = row.HasAllocatedBudget,
                    BudgetDifference = row.BudgetDifference,
                    ExceedsBudget = row.ExceedsBudget,
                    UnusualChange = row.UnusualChange,
                    ChangePercent = row.ChangePercent,
                    RiskTitle = row.RiskTitle,
                    RiskDetail = row.RiskDetail,
                    Engine = row.Engine,
                    GeneratedAt = row.GeneratedAt
                });
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private static PayrollPredictionRow ToRow(
            PayrollPredictionHistory saved, CultureInfo culture, bool isPrevious)
        {
            return new PayrollPredictionRow
            {
                PreviousMonth1 = saved.PreviousMonth1,
                PreviousLabel1 = saved.PreviousMonth1.ToString("MMMM yyyy", culture),
                PreviousAmount1 = saved.PreviousAmount1,
                PreviousMonth2 = saved.PreviousMonth2,
                PreviousLabel2 = saved.PreviousMonth2.ToString("MMMM yyyy", culture),
                PreviousAmount2 = saved.PreviousAmount2,
                PredictionMonth = saved.PredictionMonth,
                PredictionLabel = string.IsNullOrWhiteSpace(saved.PredictionLabel)
                    ? saved.PredictionMonth.ToString("MMMM yyyy", culture)
                    : saved.PredictionLabel,
                PredictedPayroll = saved.PredictedPayroll,
                AllocatedBudget = saved.AllocatedBudget,
                HasAllocatedBudget = saved.HasAllocatedBudget,
                BudgetDifference = saved.BudgetDifference,
                ExceedsBudget = saved.ExceedsBudget,
                UnusualChange = saved.UnusualChange,
                ChangePercent = saved.ChangePercent,
                RiskTitle = saved.RiskTitle,
                RiskDetail = saved.RiskDetail,
                GeneratedAt = saved.GeneratedAt,
                Engine = saved.Engine,
                IsPrevious = isPrevious
            };
        }

        private static string SnapshotKey(PayrollPredictionRow row) =>
            string.Create(CultureInfo.InvariantCulture,
                $"{row.PreviousMonth1:yyyy-MM}|{row.PreviousAmount1:0.00}|{row.PreviousMonth2:yyyy-MM}|{row.PreviousAmount2:0.00}|{row.PredictionMonth:yyyy-MM}|{row.PredictedPayroll:0.00}|{row.AllocatedBudget:0.00}");

        private static string SnapshotKey(PayrollPredictionHistory saved) =>
            string.Create(CultureInfo.InvariantCulture,
                $"{saved.PreviousMonth1:yyyy-MM}|{saved.PreviousAmount1:0.00}|{saved.PreviousMonth2:yyyy-MM}|{saved.PreviousAmount2:0.00}|{saved.PredictionMonth:yyyy-MM}|{saved.PredictedPayroll:0.00}|{saved.AllocatedBudget:0.00}");

        private static string WindowKey(PayrollPredictionRow row) =>
            string.Create(CultureInfo.InvariantCulture,
                $"{row.PreviousMonth1:yyyy-MM}|{row.PreviousMonth2:yyyy-MM}|{row.PredictionMonth:yyyy-MM}");

        private static SortedDictionary<DateTime, decimal> CompletedMonthTotals(
            SortedDictionary<DateTime, decimal> months, DateTime today)
        {
            var completed = new SortedDictionary<DateTime, decimal>();
            foreach (var pair in months)
            {
                if (DateRules.IsCalendarMonthFinished(pair.Key, today))
                    completed[pair.Key] = pair.Value;
            }
            return completed;
        }

        private static SortedDictionary<DateTime, decimal> MonthTotals(List<Payroll> payrolls)
        {
            var generated = new SortedDictionary<DateTime, decimal>();
            foreach (var slip in payrolls)
            {
                var source = DateRules.IsUsableDate(slip.PayPeriodStart)
                    ? slip.PayPeriodStart
                    : slip.PayPeriodEnd;
                var month = DateRules.MonthOfPeriod(source);
                generated[month] = generated.TryGetValue(month, out var current)
                    ? current + slip.NetPay
                    : slip.NetPay;
            }
            return generated;
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
            if (remote != null)
                return remote;

            var local = PayrollPredictionEngine.Forecast(input);
            local.Engine = "local";
            return local;
        }

        /// <summary>
        /// POSTs both camelCase and snake_case fields to Prediction:ApiUrl/predict. Returns null on missing URL, HTTP error, or exception
        /// so the C# engine can run instead.
        /// </summary>
        private async Task<PayrollForecastResult?> TryRemoteForecastAsync(
            PayrollForecastInput input, CancellationToken cancellationToken)
        {
            var apiUrl = (_config["Prediction:ApiUrl"] ?? "").Trim();
            if (apiUrl.Length == 0)
                return null;

            try
            {
                var client = _httpFactory.CreateClient("PayrollPrediction");
                using var request = new HttpRequestMessage(HttpMethod.Post, Combine(apiUrl, "/predict"));
                var apiKey = (_config["Prediction:ApiKey"] ?? "").Trim();
                if (apiKey.Length > 0)
                    request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

                request.Content = JsonContent.Create(new
                {
                    previous_payroll_1 = input.PreviousPayroll1,
                    previous_payroll_2 = input.PreviousPayroll2,
                    allocated_budget = input.AllocatedBudget,
                    anomaly_percent = input.AnomalyPercent,
                    previousPayroll1 = input.PreviousPayroll1,
                    previousPayroll2 = input.PreviousPayroll2,
                    allocatedBudget = input.AllocatedBudget,
                    anomalyPercent = input.AnomalyPercent
                }, options: JsonOptions);

                using var response = await client.SendAsync(request, cancellationToken);

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
                    RiskDetail = payload.RiskDetail,
                    Engine = "python",
                    Model = string.IsNullOrWhiteSpace(payload.Model)
                        ? "sklearn.linear_model.LinearRegression"
                        : payload.Model
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

        /// <summary>
        /// First day of that calendar month, used as the dictionary key when pairing previous budgets.
        /// </summary>
        private static DateTime MonthKey(DateTime value) =>
            new(value.Year, value.Month, 1);

        /// <summary>
        /// Reads Prediction:AnomalyChangePercent from config, or 25% from the engine, for the unusual-change flag.
        /// </summary>
        private decimal AnomalyPercent()
        {
            var raw = _config["Prediction:AnomalyChangePercent"];
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value > 0)
                return value;
            return PayrollPredictionEngine.DefaultAnomalyPercent;
        }

        /// <summary>
        /// Joins base API URL and path without a double slash (for example http://host + /predict).
        /// </summary>
        private static string Combine(string baseUrl, string path)
        {
            return baseUrl.TrimEnd('/') + path;
        }

        /// <summary>
        /// JSON DTO for the Python /predict response. Each money/flag property has a camelCase name plus a snake_case alias setter.
        /// </summary>
        private sealed class RemoteForecastDto
        {
            /// <summary>Predicted next-month amount from the API.</summary>
            public decimal PredictedPayroll { get; set; }

            /// <summary>Snake_case alias that writes <see cref="PredictedPayroll"/>.</summary>
            [JsonPropertyName("predicted_payroll")]
            public decimal PredictedPayrollSnake
            {
                get => PredictedPayroll;
                set => PredictedPayroll = value;
            }

            /// <summary>Allocated budget returned by the API, or null to keep the C# input budget.</summary>
            public decimal? AllocatedBudget { get; set; }

            /// <summary>Snake_case alias for allocated budget.</summary>
            [JsonPropertyName("allocated_budget")]
            public decimal? AllocatedBudgetSnake
            {
                get => AllocatedBudget;
                set => AllocatedBudget = value;
            }

            /// <summary>Predicted minus budget from the API, if provided.</summary>
            public decimal? BudgetDifference { get; set; }

            /// <summary>Snake_case alias for budget difference.</summary>
            [JsonPropertyName("budget_difference")]
            public decimal? BudgetDifferenceSnake
            {
                get => BudgetDifference;
                set => BudgetDifference = value;
            }

            /// <summary>API flag that predicted amount is over budget.</summary>
            public bool? ExceedsBudget { get; set; }

            /// <summary>Snake_case alias for exceeds-budget.</summary>
            [JsonPropertyName("exceeds_budget")]
            public bool? ExceedsBudgetSnake
            {
                get => ExceedsBudget;
                set => ExceedsBudget = value;
            }

            /// <summary>API flag for a large month-to-month jump.</summary>
            public bool? UnusualChange { get; set; }

            /// <summary>Snake_case alias for unusual change.</summary>
            [JsonPropertyName("unusual_change")]
            public bool? UnusualChangeSnake
            {
                get => UnusualChange;
                set => UnusualChange = value;
            }

            /// <summary>Percent change between the two previous months from the API.</summary>
            public decimal? ChangePercent { get; set; }

            /// <summary>Snake_case alias for change percent.</summary>
            [JsonPropertyName("change_percent")]
            public decimal? ChangePercentSnake
            {
                get => ChangePercent;
                set => ChangePercent = value;
            }

            /// <summary>Optional risk heading from the API (Budget Exceeding Risk, etc.).</summary>
            public string? RiskTitle { get; set; }

            /// <summary>Snake_case alias for risk title.</summary>
            [JsonPropertyName("risk_title")]
            public string? RiskTitleSnake
            {
                get => RiskTitle;
                set => RiskTitle = value;
            }

            /// <summary>Optional risk sentence from the API for the prediction page.</summary>
            public string? RiskDetail { get; set; }

            /// <summary>Snake_case alias for risk detail.</summary>
            [JsonPropertyName("risk_detail")]
            public string? RiskDetailSnake
            {
                get => RiskDetail;
                set => RiskDetail = value;
            }

            public string? Engine { get; set; }
            public string? Model { get; set; }
        }
    }

    /// <summary>View model for Admin Payroll/Prediction: project header plus one forecast row or an Error string.</summary>
    public class PayrollPredictionPage
    {
        /// <summary>Project being predicted; posted back on GetPrediction.</summary>
        public int ProjectId { get; set; }
        /// <summary>Project name shown on the prediction page header.</summary>
        public string ProjectName { get; set; } = "—";
        public DateTime GeneratedAt { get; set; } = PhilippinesTime.Now;
        public string? Error { get; set; }
        public string Engine { get; set; } = "local";
        public string? Model { get; set; }
        public List<PayrollPredictionRow> Rows { get; set; } = new();
    }

    /// <summary>One next-month forecast line: two previous budget months, predicted amount, allocated budget, and risk flags.</summary>
    public class PayrollPredictionRow
    {
        /// <summary>First day of the older previous month.</summary>
        public DateTime PreviousMonth1 { get; set; }
        /// <summary>Label for the older month (MonthYear or MMMM yyyy).</summary>
        public string PreviousLabel1 { get; set; } = "";
        /// <summary>Allocated/used amount for the older month.</summary>
        public decimal PreviousAmount1 { get; set; }
        /// <summary>First day of the newer previous month.</summary>
        public DateTime PreviousMonth2 { get; set; }
        /// <summary>Label for the newer previous month.</summary>
        public string PreviousLabel2 { get; set; } = "";
        /// <summary>Allocated/used amount for the newer previous month.</summary>
        public decimal PreviousAmount2 { get; set; }
        /// <summary>First day of the month being predicted.</summary>
        public DateTime PredictionMonth { get; set; }
        /// <summary>Heading for the predicted month (allocated row label or MMMM yyyy).</summary>
        public string PredictionLabel { get; set; } = "";
        /// <summary>Linear-trend (or Python API) estimate for that month.</summary>
        public decimal PredictedPayroll { get; set; }
        /// <summary>Admin-allocated budget for the predicted month when one exists.</summary>
        public decimal AllocatedBudget { get; set; }
        /// <summary>True when the predicted month already has a ProjectMonthlyBudget row to compare against.</summary>
        public bool HasAllocatedBudget { get; set; }
        /// <summary>Predicted minus allocated (or minus last month if no allocation).</summary>
        public decimal BudgetDifference { get; set; }
        /// <summary>True when predicted amount is greater than allocated budget — triggers an admin notification.</summary>
        public bool ExceedsBudget { get; set; }
        /// <summary>True when the last two months jumped by at least the anomaly percent.</summary>
        public bool UnusualChange { get; set; }
        /// <summary>Absolute percent change between the two previous amounts.</summary>
        public decimal ChangePercent { get; set; }
        /// <summary>Risk heading shown on the page, or null when both flags are off.</summary>
        public string? RiskTitle { get; set; }
        /// <summary>Explanation under the risk heading (over budget vs sharp rise/drop).</summary>
        public string? RiskDetail { get; set; }
        public DateTime GeneratedAt { get; set; }
        public string Engine { get; set; } = "local";
        public bool IsPrevious { get; set; }
    }
}
