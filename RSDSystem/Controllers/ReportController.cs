using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    /// <summary>
    /// Admin printable reports. Index picks type + project + period;
    /// Periods returns JSON date ranges; Generate/Print build the HTML.
    /// Types: payroll, attendance, payslip, prediction, anomaly.
    /// </summary>
    public class ReportController : Controller
    {
        public static readonly string[] ReportTypes =
        {
            "Payroll Report",
            "Attendance Report",
            "Payslip Report",
            "Payroll Prediction Report",
            "Payroll Anomaly Report"
        };

        private static readonly CultureInfo Dates = CultureInfo.InvariantCulture;

        private readonly PayrollDbContext _db;
        private readonly AttendanceImportService _imports;
        private readonly PayrollPredictionService _predictions;

        /// <summary>
        /// Receives the database, attendance summaries, and prediction service used to build report HTML.
        /// </summary>
        public ReportController(
            PayrollDbContext db,
            AttendanceImportService imports,
            PayrollPredictionService predictions)
        {
            _db = db;
            _imports = imports;
            _predictions = predictions;
        }

        /// <summary>
        /// GET /Report. Admin Reports page: choose type, project, and period, then Generate or Print.
        /// Staff hitting this URL are sent to the to-do list.
        /// </summary>
        public async Task<IActionResult> Index()
        {
            if (!IsAdmin)
                return RedirectToAction("Index", "PayrollStaff");

            ViewData["Title"] = "Reports";
            ViewBag.PageTitle = "Reports";
            ViewBag.ReportTypes = ReportTypes;
            ViewBag.Projects = await _db.Projects
                .AsNoTracking()
                .OrderBy(p => p.ProjectName)
                .ToListAsync();
            return View();
        }

        /// <summary>
        /// GET /Report/Periods?projectId=. Reports page fills the period dropdown from schedules, payrolls, and attendance.
        /// </summary>
        /// <returns>JSON start/end/label rows, or an error if the caller is not Admin.</returns>
        [HttpGet]
        public async Task<IActionResult> Periods(int projectId)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });

            var periods = await LoadPeriodsAsync(projectId);
            return Json(new
            {
                success = true,
                periods = periods.Select(p => new
                {
                    start = p.Start.ToString("yyyy-MM-dd"),
                    end = p.End.ToString("yyyy-MM-dd"),
                    label = p.Start.ToString("MMMM dd", Dates) + " - " + p.End.ToString("MMMM dd, yyyy", Dates)
                })
            });
        }

        /// <summary>
        /// GET /Report/Generate. Reports page Generate button. Builds HTML for the preview pane via BuildAsync.
        /// Prediction and anomaly reports do not require a period.
        /// </summary>
        /// <returns>JSON title and html, or an error when type, project, or period is missing.</returns>
        [HttpGet]
        public async Task<IActionResult> Generate(string? reportType, int projectId, string? periodStart, string? periodEnd)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });

            var built = await BuildAsync(reportType, projectId, periodStart, periodEnd);
            if (built.Error != null)
                return Json(new { success = false, message = built.Error });

            return Json(new
            {
                success = true,
                title = built.Title,
                html = built.Html
            });
        }

        /// <summary>
        /// GET /Report/Print. Reports page Print opens a printer-friendly view of the same HTML as Generate.
        /// </summary>
        public async Task<IActionResult> Print(string? reportType, int projectId, string? periodStart, string? periodEnd)
        {
            if (!IsAdmin)
                return RedirectToAction("Index", "PayrollStaff");

            var built = await BuildAsync(reportType, projectId, periodStart, periodEnd);
            ViewBag.Title = built.Title ?? "Report";
            ViewBag.Error = built.Error;
            ViewBag.Html = built.Html ?? "";
            return View();
        }

        /// <summary>
        /// Pick the report builder from reportType. Generate and Print both call this.
        /// Payroll, attendance, and payslip need a period; prediction and anomaly do not.
        /// </summary>
        private async Task<ReportBuild> BuildAsync(string? reportType, int projectId, string? periodStart, string? periodEnd)
        {
            var type = (reportType ?? "").Trim();
            if (!ReportTypes.Contains(type))
                return new ReportBuild { Error = "Select a report type." };

            var project = await _db.Projects
                .AsNoTracking()
                .Include(p => p.MonthlyBudgets)
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);
            if (project == null)
                return new ReportBuild { Error = "Select a project first." };

            var start = ParseIso(periodStart);
            var end = ParseIso(periodEnd);
            if (type is not "Payroll Prediction Report" and not "Payroll Anomaly Report"
                && (!start.HasValue || !end.HasValue))
                return new ReportBuild { Error = "Select a payroll period." };

            var projectName = project.ProjectName ?? "Project";
            var periodLabel = start.HasValue && end.HasValue
                ? start.Value.ToString("MMMM dd", Dates) + " - " + end.Value.ToString("MMMM dd, yyyy", Dates)
                : "";

            return type switch
            {
                "Payroll Report" => await PayrollReportAsync(project, start!.Value, end!.Value, periodLabel),
                "Attendance Report" => await AttendanceReportAsync(project, start!.Value, end!.Value, periodLabel),
                "Payslip Report" => await PayslipReportAsync(project, start!.Value, end!.Value, periodLabel),
                "Payroll Prediction Report" => await PredictionReportAsync(project),
                _ => await AnomalyReportAsync(project, start, end, periodLabel)
            };
        }

        /// <summary>
        /// Payroll Report table: days, OT, gross, net, and status for each slip in the period, plus total net pay.
        /// </summary>
        private async Task<ReportBuild> PayrollReportAsync(Project project, DateTime start, DateTime end, string periodLabel)
        {
            var slips = await _db.Payrolls.AsNoTracking()
                .Include(p => p.Employee)
                .Where(p => p.ProjectId == project.ProjectId
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date)
                .OrderBy(p => p.Employee!.LastName)
                .ThenBy(p => p.Employee!.FirstName)
                .ToListAsync();

            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Payroll Report", periodLabel));
            html.Append("<table class=\"report-table\"><thead><tr>");
            html.Append("<th>Employee</th><th>Job</th><th>Days</th><th>OT Hours</th><th>Gross Pay</th><th>Net Pay</th><th>Status</th>");
            html.Append("</tr></thead><tbody>");
            if (slips.Count == 0)
            {
                html.Append("<tr><td colspan=\"7\">No payroll records for this period.</td></tr>");
            }
            else
            {
                foreach (var slip in slips)
                {
                    html.Append("<tr>");
                    html.Append($"<td>{Esc(slip.Employee?.FullName)}</td>");
                    html.Append($"<td>{Esc(slip.Employee?.JobClassification)}</td>");
                    html.Append($"<td>{slip.RegularDaysWorked}</td>");
                    html.Append($"<td>{slip.OvertimeHours:0.##}</td>");
                    html.Append($"<td>₱{slip.GrossPay:N2}</td>");
                    html.Append($"<td>₱{slip.NetPay:N2}</td>");
                    html.Append($"<td>{Esc(slip.Status)}</td>");
                    html.Append("</tr>");
                }
            }
            html.Append("</tbody></table>");
            html.Append($"<div class=\"report-total\">Total net pay: ₱{slips.Sum(s => s.NetPay):N2}</div>");
            return new ReportBuild { Title = "Payroll Report — " + project.ProjectName, Html = html.ToString() };
        }

        /// <summary>
        /// Attendance Report: KPI totals plus per-employee days worked, absent, late, half-day, regular, and OT hours.
        /// </summary>
        private async Task<ReportBuild> AttendanceReportAsync(Project project, DateTime start, DateTime end, string periodLabel)
        {
            var summary = await _imports.QuerySummaryAsync(
                project.ProjectId, start, end, null, "all", 1, 500, HttpContext.RequestAborted);
            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Attendance Report", periodLabel));
            html.Append("<div class=\"report-kpis\">");
            html.Append($"<span>Employees: {summary.Total}</span>");
            html.Append($"<span>Days worked: {summary.DaysWorked}</span>");
            html.Append($"<span>Absent: {summary.DaysAbsent}</span>");
            html.Append($"<span>Regular hours: {summary.RegularHours:0.00}</span>");
            html.Append($"<span>OT hours: {summary.OvertimeHours:0.00}</span>");
            html.Append("</div>");
            html.Append("<table class=\"report-table\"><thead><tr>");
            html.Append("<th>Employee</th><th>Days Worked</th><th>Absent</th><th>Late</th><th>Half-day</th><th>Regular</th><th>OT</th>");
            html.Append("</tr></thead><tbody>");
            if (summary.Rows.Count == 0)
            {
                html.Append("<tr><td colspan=\"7\">No attendance records for this period.</td></tr>");
            }
            else
            {
                foreach (var row in summary.Rows)
                {
                    html.Append("<tr>");
                    html.Append($"<td>{Esc(row.EmployeeName)}</td>");
                    html.Append($"<td>{row.DaysWorked}</td><td>{row.DaysAbsent}</td><td>{row.DaysLate}</td><td>{row.DaysIncomplete}</td>");
                    html.Append($"<td>{row.RegularHours:0.00}</td><td>{row.OvertimeHours:0.00}</td>");
                    html.Append("</tr>");
                }
            }
            html.Append("</tbody></table>");
            return new ReportBuild { Title = "Attendance Report — " + project.ProjectName, Html = html.ToString() };
        }

        /// <summary>
        /// Payslip Report: one mini table per employee with regular/OT/gross/cash advance/net for the period.
        /// </summary>
        private async Task<ReportBuild> PayslipReportAsync(Project project, DateTime start, DateTime end, string periodLabel)
        {
            var slips = await _db.Payrolls.AsNoTracking()
                .Include(p => p.Employee)
                .Where(p => p.ProjectId == project.ProjectId
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date)
                .OrderBy(p => p.Employee!.LastName)
                .ThenBy(p => p.Employee!.FirstName)
                .ToListAsync();

            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Payslip Report", periodLabel));
            if (slips.Count == 0)
            {
                html.Append("<p>No payslips for this period.</p>");
                return new ReportBuild { Title = "Payslip Report — " + project.ProjectName, Html = html.ToString() };
            }

            foreach (var slip in slips)
            {
                html.Append("<div class=\"report-slip\">");
                html.Append($"<div class=\"report-slip-name\">{Esc(slip.Employee?.FullName)}</div>");
                html.Append("<table class=\"report-table\"><tbody>");
                html.Append($"<tr><td>Job</td><td>{Esc(slip.Employee?.JobClassification)}</td></tr>");
                html.Append($"<tr><td>Regular days</td><td>{slip.RegularDaysWorked}</td></tr>");
                html.Append($"<tr><td>Overtime</td><td>{slip.OvertimeHours:0.##} hours</td></tr>");
                html.Append($"<tr><td>Regular pay</td><td>₱{slip.RegularPay:N2}</td></tr>");
                html.Append($"<tr><td>Overtime pay</td><td>₱{slip.OvertimePay:N2}</td></tr>");
                html.Append($"<tr><td>Gross pay</td><td>₱{slip.GrossPay:N2}</td></tr>");
                html.Append($"<tr><td>Cash advance</td><td>₱{slip.CashAdvance:N2}</td></tr>");
                html.Append($"<tr><td>Net pay</td><td>₱{slip.NetPay:N2}</td></tr>");
                html.Append("</tbody></table></div>");
            }

            return new ReportBuild { Title = "Payslip Report — " + project.ProjectName, Html = html.ToString() };
        }

        /// <summary>
        /// Payroll Prediction Report: previous two months, predicted amount, allocated budget, and exceed-budget flag.
        /// Uses the same LoadAsync math as the Prediction page (needs two finished Approved months).
        /// </summary>
        private async Task<ReportBuild> PredictionReportAsync(Project project)
        {
            var page = await _predictions.LoadAsync(project.ProjectId, HttpContext.RequestAborted);
            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Payroll Prediction Report", page.GeneratedAt.ToString("MMMM dd, yyyy", Dates)));
            if (page.Error != null && page.Rows.Count == 0)
            {
                html.Append($"<p>{Esc(page.Error)}</p>");
                return new ReportBuild { Title = "Payroll Prediction Report — " + project.ProjectName, Html = html.ToString() };
            }

            html.Append("<table class=\"report-table\"><thead><tr>");
            html.Append("<th>Previous month 1</th><th>Amount</th><th>Previous month 2</th><th>Amount</th><th>Predicted month</th><th>Predicted budget</th><th>Allocated budget</th><th>Anomaly</th>");
            html.Append("</tr></thead><tbody>");
            foreach (var row in page.Rows)
            {
                html.Append("<tr>");
                html.Append($"<td>{Esc(row.PreviousLabel1)}</td><td>₱{row.PreviousAmount1:N2}</td>");
                html.Append($"<td>{Esc(row.PreviousLabel2)}</td><td>₱{row.PreviousAmount2:N2}</td>");
                html.Append($"<td>{Esc(row.PredictionLabel)}</td><td>₱{row.PredictedPayroll:N2}</td>");
                html.Append(row.HasAllocatedBudget
                    ? $"<td>₱{row.AllocatedBudget:N2}</td>"
                    : "<td>—</td>");
                html.Append(row.ExceedsBudget
                    ? "<td>Next month exceeds the allocated budget</td>"
                    : "<td>None</td>");
                html.Append("</tr>");
            }
            html.Append("</tbody></table>");
            return new ReportBuild { Title = "Payroll Prediction Report — " + project.ProjectName, Html = html.ToString() };
        }

        /// <summary>
        /// Payroll Anomaly Report: lists budget-exceed and unusual-change flags, plus period net vs project PayrollBudget.
        /// </summary>
        private async Task<ReportBuild> AnomalyReportAsync(Project project, DateTime? start, DateTime? end, string periodLabel)
        {
            var page = await _predictions.LoadAsync(project.ProjectId, HttpContext.RequestAborted);
            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Payroll Anomaly Report", string.IsNullOrEmpty(periodLabel) ? page.GeneratedAt.ToString("MMMM dd, yyyy", Dates) : periodLabel));

            var flags = new List<string>();
            foreach (var row in page.Rows)
            {
                if (row.ExceedsBudget)
                    flags.Add($"The predicted amount for {row.PredictionLabel} (₱{row.PredictedPayroll:N2}) exceeds the allocated budget (₱{row.AllocatedBudget:N2}).");
                if (row.UnusualChange)
                    flags.Add("A significant change was detected compared with the usual payroll pattern.");
            }

            if (start.HasValue && end.HasValue)
            {
                var total = await _db.Payrolls.AsNoTracking()
                    .Where(p => p.ProjectId == project.ProjectId
                        && p.PayPeriodStart.Date == start.Value.Date
                        && p.PayPeriodEnd.Date == end.Value.Date)
                    .SumAsync(p => (decimal?)p.NetPay) ?? 0;
                if (project.PayrollBudget.HasValue && total > project.PayrollBudget.Value)
                    flags.Add($"Payroll for this period (₱{total:N2}) may exceed the allocated project budget (₱{project.PayrollBudget:N2}).");
            }

            if (flags.Count == 0)
                html.Append("<p>No payroll anomalies were detected for this project.</p>");
            else
            {
                html.Append("<ul class=\"report-flags\">");
                foreach (var flag in flags.Distinct())
                    html.Append($"<li>{Esc(flag)}</li>");
                html.Append("</ul>");
            }

            return new ReportBuild { Title = "Payroll Anomaly Report — " + project.ProjectName, Html = html.ToString() };
        }

        /// <summary>
        /// Unique date ranges for the Reports period dropdown: payroll schedules, saved slips, and imported attendance.
        /// </summary>
        private async Task<List<(DateTime Start, DateTime End)>> LoadPeriodsAsync(int projectId)
        {
            var map = new Dictionary<string, (DateTime Start, DateTime End)>(StringComparer.OrdinalIgnoreCase);

            // Deduplicate the same start/end coming from schedules, payrolls, and attendance.
            void Add(DateTime start, DateTime end)
            {
                var key = start.ToString("yyyy-MM-dd") + "|" + end.ToString("yyyy-MM-dd");
                map[key] = (start.Date, end.Date);
            }

            var schedules = await _db.PayrollSchedules.AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .ToListAsync();
            foreach (var schedule in schedules)
                Add(schedule.StartingDate, schedule.EndDate);

            var payrolls = await _db.Payrolls.AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .Select(p => new { p.PayPeriodStart, p.PayPeriodEnd })
                .Distinct()
                .ToListAsync();
            foreach (var payroll in payrolls)
                Add(payroll.PayPeriodStart, payroll.PayPeriodEnd);

            var attendance = await _imports.ListPeriodsAsync(projectId, HttpContext.RequestAborted);
            foreach (var period in attendance)
                Add(period.Start, period.End);

            return map.Values
                .OrderByDescending(p => p.Start)
                .ThenByDescending(p => p.End)
                .ToList();
        }

        /// <summary>
        /// HTML heading block used at the top of every generated report (type, project, period).
        /// </summary>
        private static string Header(string? projectName, string reportType, string period) =>
            $"<div class=\"report-head\"><div>{Esc(reportType)}</div><div>{Esc(projectName)}</div><div>{Esc(period)}</div></div>";

        /// <summary>
        /// HTML-encode a cell value. Blank strings become an em dash so empty names do not break the table.
        /// </summary>
        private static string Esc(string? value) =>
            System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value);

        /// <summary>
        /// Parse yyyy-MM-dd from the Reports period query string. Other formats fall back to DateTime.TryParse.
        /// </summary>
        private static DateTime? ParseIso(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", Dates, DateTimeStyles.None, out var date))
                return date.Date;
            return DateTime.TryParse(value, out var parsed) ? parsed.Date : null;
        }

        /// <summary>
        /// True when the session Role is Admin. Index, Periods, Generate, and Print all require this.
        /// </summary>
        private bool IsAdmin =>
            string.Equals(HttpContext.Session.GetString("Role"), "Admin", StringComparison.OrdinalIgnoreCase);

        private sealed class ReportBuild
        {
            public string? Title { get; set; }
            public string? Html { get; set; }
            public string? Error { get; set; }
        }
    }
}
