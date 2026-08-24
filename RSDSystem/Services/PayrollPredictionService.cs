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

        /// <summary>
        /// Builds the Admin Payroll/Prediction page for one project. Needs two monthly budgets; forecasts the next month
        /// (or the next month that has a previous pair). Flags ExceedsBudget vs allocated amount and UnusualChange vs last two months.
        /// </summary>
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

            if (budgets.Count < 2)
            {
                return new PayrollPredictionPage
                {
                    ProjectId = project.ProjectId,
                    ProjectName = project.ProjectName ?? "—",
                    GeneratedAt = DateTime.Now,
                    Error = budgets.Count == 0
                        ? "This project has no monthly budget. Set at least two monthly budgets on the project first."
                        : "Need two months of project budget before the next monthly budget can be predicted."
                };
            }

            var byMonth = new Dictionary<DateTime, ProjectMonthlyBudget>();
            foreach (var row in budgets)
                byMonth[MonthKey(row.MonthDate)] = row;

            var nextMonth = MonthKey(DateTime.Today).AddMonths(1);
            DateTime predictMonth;
            ProjectMonthlyBudget previous1;
            ProjectMonthlyBudget previous2;
            if (TryPreviousPair(byMonth, nextMonth, out previous1, out previous2))
            {
                predictMonth = nextMonth;
            }
            else
            {
                var upcoming = byMonth.Keys
                    .Where(month => month >= nextMonth && TryPreviousPair(byMonth, month, out _, out _))
                    .OrderBy(month => month)
                    .FirstOrDefault();
                if (upcoming != default)
                {
                    predictMonth = upcoming;
                    TryPreviousPair(byMonth, predictMonth, out previous1, out previous2);
                }
                else
                {
                    previous1 = budgets[^2];
                    previous2 = budgets[^1];
                    predictMonth = MonthKey(previous2.MonthDate).AddMonths(1);
                }
            }

            var amount1 = previous1.Amount;
            var amount2 = previous2.Amount;
            var hasAllocated = byMonth.TryGetValue(predictMonth, out var allocatedRow);
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
                riskTitle = "Unusual Budget Change";
                riskDetail = amount2 > amount1
                    ? "Monthly budget rose sharply between the last two months."
                    : "Monthly budget dropped sharply between the last two months.";
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
                        PreviousMonth1 = MonthKey(previous1.MonthDate),
                        PreviousLabel1 = BudgetLabel(previous1, culture),
                        PreviousAmount1 = amount1,
                        PreviousMonth2 = MonthKey(previous2.MonthDate),
                        PreviousLabel2 = BudgetLabel(previous2, culture),
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

        /// <summary>
        /// Forecasts next-month payroll from two previous amounts. Tries the Python API first, then <see cref="PayrollPredictionEngine"/>.
        /// PayrollPredictionApi and NotifyPayrollAlertsAsync also call this with explicit numbers.
        /// </summary>
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

        /// <summary>
        /// Looks up the two monthly budgets immediately before <paramref name="predictMonth"/> (month−2 and month−1). False if either is missing.
        /// </summary>
        private static bool TryPreviousPair(
            IReadOnlyDictionary<DateTime, ProjectMonthlyBudget> byMonth,
            DateTime predictMonth,
            out ProjectMonthlyBudget previous1,
            out ProjectMonthlyBudget previous2)
        {
            if (byMonth.TryGetValue(predictMonth.AddMonths(-2), out previous1!)
                && byMonth.TryGetValue(predictMonth.AddMonths(-1), out previous2!))
                return true;

            previous1 = null!;
            previous2 = null!;
            return false;
        }

        /// <summary>
        /// Column heading for a budget month: stored MonthYear text if present, otherwise "MMMM yyyy".
        /// </summary>
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
        }
    }

    /// <summary>View model for Admin Payroll/Prediction: project header plus one forecast row or an Error string.</summary>
    public class PayrollPredictionPage
    {
        /// <summary>Project being predicted; posted back on GetPrediction.</summary>
        public int ProjectId { get; set; }
        /// <summary>Project name shown on the prediction page header.</summary>
        public string ProjectName { get; set; } = "—";
        /// <summary>When this forecast was computed (local time).</summary>
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
        /// <summary>Why prediction cannot run (missing project or fewer than two monthly budgets); null on success.</summary>
        public string? Error { get; set; }
        /// <summary>Usually a single next-month row comparing last two budgets to the forecast.</summary>
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
    }
}
