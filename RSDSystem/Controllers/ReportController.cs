using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Services;
using RSDSystem.Validation;

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
            "Project Report",
            "Payroll Report",
            "Monthly Payroll Report",
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
        public async Task<IActionResult> Months(int projectId)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });

            var months = await LoadMonthsAsync(projectId);
            return Json(new
            {
                success = true,
                months = months.Select(m => new
                {
                    start = m.Start.ToString("yyyy-MM-dd"),
                    end = m.End.ToString("yyyy-MM-dd"),
                    label = m.Start.ToString("MMMM yyyy", Dates)
                })
            });
        }

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
            if (type is not "Project Report" and not "Payroll Prediction Report" and not "Payroll Anomaly Report"
                && (!start.HasValue || !end.HasValue))
                return new ReportBuild { Error = "Select a payroll period." };

            var projectName = project.ProjectName ?? "Project";
            var periodLabel = start.HasValue && end.HasValue
                ? start.Value.ToString("MMMM dd", Dates) + " - " + end.Value.ToString("MMMM dd, yyyy", Dates)
                : "";

            return type switch
            {
                "Project Report" => await ProjectReportAsync(project),
                "Payroll Report" => await PayrollReportAsync(project, start!.Value, end!.Value, periodLabel),
                "Monthly Payroll Report" => await MonthlyPayrollReportAsync(project, start!.Value, end!.Value),
                "Attendance Report" => await AttendanceReportAsync(project, start!.Value, end!.Value, periodLabel),
                "Payslip Report" => await PayslipReportAsync(project, start!.Value, end!.Value, periodLabel),
                "Payroll Prediction Report" => await PredictionReportAsync(project),
                _ => await AnomalyReportAsync(project, start, end, periodLabel)
            };
        }

        private async Task<ReportBuild> ProjectReportAsync(Project project)
        {
            var slips = await _db.Payrolls.AsNoTracking()
                .Where(p => p.ProjectId == project.ProjectId
                    && p.Status == PayrollStatusOptions.Approved)
                .Select(p => new { p.PayPeriodStart, p.PayPeriodEnd, p.NetPay })
                .ToListAsync();

            var payrollByMonth = new SortedDictionary<DateTime, decimal>();
            foreach (var slip in slips)
            {
                var source = DateRules.IsUsableDate(slip.PayPeriodStart)
                    ? slip.PayPeriodStart
                    : slip.PayPeriodEnd;
                var month = DateRules.MonthOfPeriod(source);
                payrollByMonth[month] = payrollByMonth.TryGetValue(month, out var current)
                    ? current + slip.NetPay
                    : slip.NetPay;
            }

            var budgetByMonth = new Dictionary<DateTime, decimal>();
            foreach (var row in project.MonthlyBudgets ?? new List<ProjectMonthlyBudget>())
            {
                if (!DateRules.IsUsableDate(row.MonthDate))
                    continue;
                var month = DateRules.FirstOfMonth(row.MonthDate);
                budgetByMonth[month] = budgetByMonth.TryGetValue(month, out var current)
                    ? current + row.Amount
                    : row.Amount;
            }

            var months = new SortedSet<DateTime>();
            foreach (var month in payrollByMonth.Keys)
                months.Add(month);
            foreach (var month in budgetByMonth.Keys)
                months.Add(month);
            if (DateRules.IsUsableDate(project.StartingDate) && DateRules.IsUsableDate(project.EstimateEndDate))
            {
                for (var month = DateRules.FirstOfMonth(project.StartingDate!.Value);
                     month <= DateRules.FirstOfMonth(project.EstimateEndDate!.Value);
                     month = month.AddMonths(1))
                {
                    months.Add(month);
                }
            }

            var employees = await LoadProjectEmployeesAsync(project.ProjectId);

            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Project Report", PhilippinesTime.FormatLongDate(PhilippinesTime.Today)));
            html.Append("<div class=\"report-section-title\">Project Information</div>");
            html.Append("<div class=\"report-info\">");
            html.Append(InfoField("Project Name", project.ProjectName));
            html.Append(InfoField("Status", ProjectStatusOptions.Normalize(project.Status)));
            html.Append(InfoField("Location", project.Location));
            html.Append(InfoField("Type of Service", project.TypeOfService));
            html.Append(InfoField("Starting Date", DateLabel(project.StartingDate)));
            html.Append(InfoField("Estimate End Date", DateLabel(project.EstimateEndDate)));
            html.Append(InfoField("Assigned Payroll Staff", project.AssignedPayrollStaff));
            html.Append(InfoField("Payroll Distribution", project.PayrollDistribution));
            html.Append(InfoField("Payroll Budget", Money(project.PayrollBudget)));
            html.Append("</div>");

            html.Append("<div class=\"report-section-title\">Assigned Employees</div>");
            html.Append("<table class=\"report-table\"><thead><tr>");
            html.Append("<th>Employee ID</th><th>Employee Name</th><th>Job</th>");
            html.Append("</tr></thead><tbody>");
            if (employees.Count == 0)
            {
                html.Append("<tr><td colspan=\"3\">No employees are assigned to this project.</td></tr>");
            }
            else
            {
                foreach (var emp in employees)
                {
                    html.Append("<tr>");
                    html.Append($"<td>{Esc(EmployeeIds.Format(emp.EmployeeCode))}</td>");
                    html.Append($"<td>{Esc(emp.FullName)}</td>");
                    html.Append($"<td>{Esc(emp.JobClassification)}</td>");
                    html.Append("</tr>");
                }
            }
            html.Append("</tbody></table>");

            html.Append("<div class=\"report-section-title\">Monthly Payroll and Budget</div>");
            html.Append("<table class=\"report-table\"><thead><tr>");
            html.Append("<th>Month</th><th>Allocated Budget</th><th>Total Payroll</th><th>Difference</th><th>Margin</th>");
            html.Append("</tr></thead><tbody>");

            decimal totalAllocated = 0;
            decimal totalPayroll = 0;
            if (months.Count == 0)
            {
                html.Append("<tr><td colspan=\"5\">No monthly budget or approved payroll for this project.</td></tr>");
            }
            else
            {
                foreach (var month in months)
                {
                    var hasBudget = budgetByMonth.TryGetValue(month, out var allocated);
                    var payroll = payrollByMonth.GetValueOrDefault(month);
                    if (hasBudget)
                        totalAllocated += allocated;
                    totalPayroll += payroll;

                    html.Append("<tr>");
                    html.Append($"<td>{Esc(month.ToString("MMMM yyyy", Dates))}</td>");
                    html.Append($"<td>{(hasBudget ? Money(allocated) : "—")}</td>");
                    html.Append($"<td>{Money(payroll)}</td>");
                    if (hasBudget)
                    {
                        var difference = allocated - payroll;
                        html.Append($"<td class=\"{MarginClass(difference)}\">{Money(difference)}</td>");
                        html.Append($"<td class=\"{MarginClass(difference)}\">{Esc(MarginLabel(difference))}</td>");
                    }
                    else
                    {
                        html.Append("<td>—</td><td>—</td>");
                    }
                    html.Append("</tr>");
                }
            }
            html.Append("</tbody></table>");

            var monthlyMargin = totalAllocated - totalPayroll;
            html.Append("<div class=\"report-total\">Total allocated budget: " + Money(totalAllocated) + "</div>");
            html.Append("<div class=\"report-total\">Total payroll: " + Money(totalPayroll) + "</div>");
            html.Append($"<div class=\"report-total {MarginClass(monthlyMargin)}\">Monthly budget difference: {Money(monthlyMargin)} ({Esc(MarginLabel(monthlyMargin))})</div>");
            if (project.PayrollBudget.HasValue)
            {
                var projectMargin = project.PayrollBudget.Value - totalPayroll;
                html.Append($"<div class=\"report-total {MarginClass(projectMargin)}\">Project payroll budget difference: {Money(projectMargin)} ({Esc(MarginLabel(projectMargin))})</div>");
            }

            return new ReportBuild { Title = "Project Report — " + project.ProjectName, Html = html.ToString() };
        }

        private async Task<ReportBuild> PayrollReportAsync(Project project, DateTime start, DateTime end, string periodLabel)
        {
            var slips = await _db.Payrolls.AsNoTracking()
                .Include(p => p.Employee)
                .Where(p => p.ProjectId == project.ProjectId
                    && p.Status == PayrollStatusOptions.Approved
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date)
                .OrderBy(p => p.Employee!.LastName)
                .ThenBy(p => p.Employee!.FirstName)
                .ToListAsync();

            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Payroll Report", periodLabel));
            html.Append("<table class=\"report-table\"><thead><tr>");
            html.Append("<th>Employee</th><th>Job</th><th>Days</th><th>Hours</th><th>OT Hours</th><th>Gross Pay</th><th>Net Pay</th><th>Status</th>");
            html.Append("</tr></thead><tbody>");
            if (slips.Count == 0)
            {
                html.Append("<tr><td colspan=\"8\">No payroll records for this period.</td></tr>");
            }
            else
            {
                foreach (var slip in slips)
                {
                    html.Append("<tr>");
                    html.Append($"<td>{Esc(slip.Employee?.FullName)}</td>");
                    html.Append($"<td>{Esc(slip.Employee?.JobClassification)}</td>");
                    html.Append($"<td>{slip.RegularDaysWorked}</td>");
                    html.Append($"<td>{PayrollComputation.PaidRegularHours(slip):0.##}</td>");
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

        private async Task<ReportBuild> MonthlyPayrollReportAsync(Project project, DateTime start, DateTime end)
        {
            var monthStart = new DateTime(start.Year, start.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var monthLabel = monthStart.ToString("MMMM yyyy", Dates);

            var slips = await _db.Payrolls.AsNoTracking()
                .Include(p => p.Employee)
                .Where(p => p.ProjectId == project.ProjectId
                    && p.Status == PayrollStatusOptions.Approved
                    && p.PayPeriodStart.Date >= monthStart
                    && p.PayPeriodStart.Date <= monthEnd)
                .OrderBy(p => p.PayPeriodStart)
                .ThenBy(p => p.Employee!.LastName)
                .ThenBy(p => p.Employee!.FirstName)
                .ToListAsync();

            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Monthly Payroll Report", monthLabel));
            html.Append("<table class=\"report-table\"><thead><tr>");
            html.Append("<th>Employee</th><th>Job</th><th>Period</th><th>Days</th><th>Hours</th><th>OT Hours</th><th>Gross Pay</th><th>Net Pay</th><th>Status</th>");
            html.Append("</tr></thead><tbody>");
            if (slips.Count == 0)
            {
                html.Append("<tr><td colspan=\"9\">No payroll records for this month.</td></tr>");
            }
            else
            {
                foreach (var slip in slips)
                {
                    var period = slip.PayPeriodStart.ToString("MMM dd", Dates)
                        + " - " + slip.PayPeriodEnd.ToString("MMM dd, yyyy", Dates);
                    html.Append("<tr>");
                    html.Append($"<td>{Esc(slip.Employee?.FullName)}</td>");
                    html.Append($"<td>{Esc(slip.Employee?.JobClassification)}</td>");
                    html.Append($"<td>{Esc(period)}</td>");
                    html.Append($"<td>{slip.RegularDaysWorked}</td>");
                    html.Append($"<td>{PayrollComputation.PaidRegularHours(slip):0.##}</td>");
                    html.Append($"<td>{slip.OvertimeHours:0.##}</td>");
                    html.Append($"<td>₱{slip.GrossPay:N2}</td>");
                    html.Append($"<td>₱{slip.NetPay:N2}</td>");
                    html.Append($"<td>{Esc(slip.Status)}</td>");
                    html.Append("</tr>");
                }
            }
            html.Append("</tbody></table>");
            html.Append($"<div class=\"report-total\">Total net pay: ₱{slips.Sum(s => s.NetPay):N2}</div>");
            html.Append($"<div class=\"report-total\">Total regular hours: {slips.Sum(s => PayrollComputation.PaidRegularHours(s)):0.##}</div>");
            return new ReportBuild { Title = "Monthly Payroll Report — " + project.ProjectName, Html = html.ToString() };
        }

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
                    && p.Status == PayrollStatusOptions.Approved
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
                html.Append($"<tr><td>Regular hours</td><td>{PayrollComputation.PaidRegularHours(slip):0.##}</td></tr>");
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
            var page = await _predictions.LoadAsync(project.ProjectId, cancellationToken: HttpContext.RequestAborted);
            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Payroll Prediction Report", PhilippinesTime.FormatLongDate(page.GeneratedAt)));
            html.Append(string.Equals(page.Engine, "python", StringComparison.OrdinalIgnoreCase)
                ? "<p>Predicted by the Python payroll model (scikit-learn Linear Regression).</p>"
                : "<p>Predicted with the local payroll formula (Python API offline).</p>");
            if (page.Error != null && page.Rows.Count == 0)
            {
                html.Append($"<p>{Esc(page.Error)}</p>");
                return new ReportBuild { Title = "Payroll Prediction Report — " + project.ProjectName, Html = html.ToString() };
            }

            html.Append("<table class=\"report-table\"><thead><tr>");
            html.Append("<th>Load</th><th>Previous month 1</th><th>Amount</th><th>Previous month 2</th><th>Amount</th><th>Predicted month</th><th>Predicted budget</th><th>Allocated budget</th><th>Anomaly</th>");
            html.Append("</tr></thead><tbody>");
            foreach (var row in page.Rows)
            {
                html.Append("<tr>");
                html.Append(row.IsPrevious ? "<td>Previous load</td>" : "<td>Current</td>");
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
            var page = await _predictions.LoadAsync(project.ProjectId, cancellationToken: HttpContext.RequestAborted);
            var html = new StringBuilder();
            html.Append(Header(project.ProjectName, "Payroll Anomaly Report", string.IsNullOrEmpty(periodLabel) ? PhilippinesTime.FormatLongDate(page.GeneratedAt) : periodLabel));

            var flags = new List<string>();
            foreach (var row in page.Rows.Where(r => !r.IsPrevious))
            {
                if (row.ExceedsBudget)
                    flags.Add($"The predicted amount for {row.PredictionLabel} (₱{row.PredictedPayroll:N2}) exceeds the allocated budget (₱{row.AllocatedBudget:N2}).");
                if (row.UnusualChange)
                    flags.Add($"A significant change was detected for {row.PredictionLabel} compared with the usual payroll pattern.");
            }

            if (start.HasValue && end.HasValue)
            {
                var total = await _db.Payrolls.AsNoTracking()
                    .Where(p => p.ProjectId == project.ProjectId
                        && p.Status == PayrollStatusOptions.Approved
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

        private async Task<List<Employee>> LoadProjectEmployeesAsync(int projectId)
        {
            var assigned = _db.Employees.AsNoTracking()
                .Where(e => e.ProjectId == projectId)
                .Select(e => e.EmployeeId);
            var history = _db.ProjectEmployeeHistories.AsNoTracking()
                .Where(h => h.ProjectId == projectId)
                .Select(h => h.EmployeeId);
            var payroll = _db.Payrolls.AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .Select(p => p.EmployeeId);

            var ids = await assigned.Union(history).Union(payroll).ToListAsync();
            if (ids.Count == 0)
                return new List<Employee>();

            return await _db.Employees.AsNoTracking()
                .Where(e => ids.Contains(e.EmployeeId))
                .OrderBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToListAsync();
        }

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

        private async Task<List<(DateTime Start, DateTime End)>> LoadMonthsAsync(int projectId)
        {
            var map = new Dictionary<string, (DateTime Start, DateTime End)>(StringComparer.OrdinalIgnoreCase);

            void Add(DateTime date)
            {
                var start = new DateTime(date.Year, date.Month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                map[start.ToString("yyyy-MM")] = (start, end);
            }

            var payrolls = await _db.Payrolls.AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .Select(p => new { p.PayPeriodStart, p.PayPeriodEnd })
                .ToListAsync();
            foreach (var payroll in payrolls)
                Add(payroll.PayPeriodStart);

            var budgets = await _db.ProjectMonthlyBudgets.AsNoTracking()
                .Where(m => m.ProjectId == projectId)
                .Select(m => m.MonthDate)
                .ToListAsync();
            foreach (var month in budgets)
                Add(month);

            if (map.Count == 0)
                Add(DateTime.Today);

            return map.Values
                .OrderByDescending(m => m.Start)
                .ToList();
        }

        private static string Header(string? projectName, string reportType, string period) =>
            $"<div class=\"report-head\"><div>{Esc(reportType)}</div><div>{Esc(projectName)}</div><div>{Esc(period)}</div></div>";

        private static string InfoField(string label, string? value) =>
            $"<div><span class=\"report-info-lbl\">{Esc(label)}</span><div>{Esc(value)}</div></div>";

        private static string DateLabel(DateTime? value) =>
            DateRules.IsUsableDate(value)
                ? value!.Value.ToString("MMMM dd, yyyy", Dates)
                : "—";

        private static string Money(decimal? value) =>
            value.HasValue ? "₱" + value.Value.ToString("N2", Dates) : "—";

        private static string MarginClass(decimal difference) =>
            difference < 0 ? "report-over" : difference > 0 ? "report-under" : "";

        private static string MarginLabel(decimal difference) =>
            difference < 0 ? "Over budget" : difference > 0 ? "Under budget" : "On budget";

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
