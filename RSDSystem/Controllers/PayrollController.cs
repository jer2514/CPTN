using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Services;
using RSDSystem.Validation;

namespace RSDSystem.Controllers
{
    public class PayrollController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly PayrollPredictionService _predictions;
        private readonly NotificationService _notifications;
        private readonly ActivityLogService _logs;
        private static readonly CultureInfo DateCulture = CultureInfo.InvariantCulture;

        public PayrollController(
            PayrollDbContext db,
            PayrollPredictionService predictions,
            NotificationService notifications,
            ActivityLogService logs)
        {
            _db = db;
            _predictions = predictions;
            _notifications = notifications;
            _logs = logs;
        }

        private IActionResult? RequireAdmin()
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "PayrollStaff");
            return null;
        }

        public async Task<IActionResult> Index(string? projectName, int? projectId, int? month)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            await BindProjectSuggestionsAsync();
            var periods = await LoadPeriodsAsync(projectName, month, projectId, approvedOnly: true);
            ViewBag.PageTitle = "View Payroll";
            ViewBag.ProjectName = projectName ?? "";
            ViewBag.SelectedProjectId = projectId;
            ViewBag.Month = month;
            ViewBag.Months = MonthOptions();
            return View(periods);
        }

        public async Task<IActionResult> Period(int projectId, DateTime start, DateTime end)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            ViewBag.PageTitle = "View Payroll";
            ViewBag.ProjectId = projectId;
            ViewBag.ProjectName = project.ProjectName;
            ViewBag.Start = start.Date;
            ViewBag.End = end.Date;
            ViewBag.StartLabel = start.ToString("MMMM dd, yyyy", DateCulture);
            ViewBag.EndLabel = end.ToString("MMMM dd, yyyy", DateCulture);
            ViewBag.FileName = PayslipFileName(project.ProjectName, start, end);
            ViewBag.IsFinished = ProjectStatusOptions.IsFinished(project.Status);
            return View(await LoadPeriodEmployeesAsync(projectId, start.Date, end.Date, approvedOnly: true));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePayslips(int projectId, DateTime start, DateTime end)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            if (ProjectStatusOptions.IsFinished(project.Status))
            {
                TempData["Error"] = "Finished projects cannot generate payslips.";
                return RedirectToAction(nameof(Period), new { projectId, start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") });
            }

            var employees = await _db.Employees
                .Where(e => e.ProjectId == projectId && e.IsActive)
                .ToListAsync();

            if (employees.Count == 0)
            {
                TempData["Error"] = "No active employees are assigned to this project.";
                return RedirectToAction(nameof(Period), new { projectId, start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") });
            }

            var existing = await _db.Set<Payroll>()
                .Where(p => p.ProjectId == projectId
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date)
                .ToListAsync();

            var covering = PayrollPeriods.Covering(
                await _db.Set<PayrollSchedule>().Where(s => s.ProjectId == projectId).ToListAsync(),
                start.Date, end.Date);

            var daysWorked = Math.Max(1, DateRules.CountWeekdays(start.Date, end.Date));
            var generatedBy = HttpContext.Session.GetString("FullName") ?? "Admin";
            var created = 0;

            foreach (var emp in employees)
            {
                if (existing.Any(p => p.EmployeeId == emp.EmployeeId))
                    continue;
                if (covering != null && existing.Any(p => PayrollPeriods.BelongsTo(p, covering) && p.EmployeeId == emp.EmployeeId))
                    continue;

                var regularPay = emp.DailyRate * daysWorked;
                var regularHours = daysWorked * 8m;
                _db.Set<Payroll>().Add(new Payroll
                {
                    EmployeeId = emp.EmployeeId,
                    ProjectId = projectId,
                    PayrollScheduleId = covering?.PayrollScheduleId,
                    PayPeriodStart = start.Date,
                    PayPeriodEnd = end.Date,
                    RegularDaysWorked = daysWorked,
                    RegularHours = regularHours,
                    OvertimeHours = 0,
                    AbsentDays = 0,
                    RegularPay = regularPay,
                    OvertimePay = 0,
                    GrossPay = regularPay,
                    CashAdvance = 0,
                    NetPay = regularPay,
                    Status = PayrollStatusOptions.Draft,
                    GeneratedBy = generatedBy,
                    GeneratedDate = PhilippinesTime.Now
                });
                created++;
            }

            if (created > 0)
                await _db.SaveChangesAsync();

            TempData["Success"] = created > 0
                ? $"Generated {created} payslip(s) for {project.ProjectName}."
                : "Payslips for this period already exist.";

            return RedirectToAction(nameof(GeneratedPayslips), new
            {
                projectId,
                start = start.ToString("yyyy-MM-dd"),
                end = end.ToString("yyyy-MM-dd")
            });
        }

        public async Task<IActionResult> Prediction()
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            ViewBag.PageTitle = "Payroll Prediction";
            var projects = await _db.Projects
                .AsNoTracking()
                .OrderBy(p => p.ProjectName)
                .ToListAsync();
            return View(projects);
        }

        [HttpGet]
        public async Task<IActionResult> GetPrediction(int projectId, string? projectName = null)
        {
            var blocked = RequireAdmin();
            if (blocked != null)
                return Json(new { success = false, message = "Admin access is required." });

            if (projectId <= 0 && !string.IsNullOrWhiteSpace(projectName))
            {
                var named = await _db.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ProjectName != null && p.ProjectName.ToLower() == projectName.Trim().ToLower());
                if (named != null)
                    projectId = named.ProjectId;
            }

            var page = await _predictions.LoadAsync(projectId, persistHistory: true, HttpContext.RequestAborted);
            if (page.Error != null && page.Rows.Count == 0)
            {
                return Json(new
                {
                    success = false,
                    projectName = page.ProjectName,
                    message = page.Error
                });
            }

            await _logs.LogAsync(
                ActivityTypes.GeneratePrediction,
                ActivityModules.Prediction,
                $"Loaded payroll prediction for {page.ProjectName}.",
                page.ProjectId);

            return Json(new
            {
                success = true,
                projectId = page.ProjectId,
                projectName = page.ProjectName,
                generatedAt = PhilippinesTime.FormatLongDateTime(page.GeneratedAt),
                engine = page.Engine,
                model = page.Model,
                usedPythonApi = string.Equals(page.Engine, "python", StringComparison.OrdinalIgnoreCase),
                warning = page.Error,
                rows = page.Rows.Select(r => new
                {
                    previousMonth1 = string.IsNullOrWhiteSpace(r.PreviousLabel1)
                        ? r.PreviousMonth1.ToString("MMMM yyyy", DateCulture)
                        : r.PreviousLabel1,
                    previousAmount1 = r.PreviousAmount1,
                    previousMonth2 = string.IsNullOrWhiteSpace(r.PreviousLabel2)
                        ? r.PreviousMonth2.ToString("MMMM yyyy", DateCulture)
                        : r.PreviousLabel2,
                    previousAmount2 = r.PreviousAmount2,
                    predictionMonth = string.IsNullOrWhiteSpace(r.PredictionLabel)
                        ? r.PredictionMonth.ToString("MMMM yyyy", DateCulture)
                        : r.PredictionLabel,
                    predictedBudget = r.PredictedPayroll,
                    predictedPayroll = r.PredictedPayroll,
                    allocatedBudget = r.AllocatedBudget,
                    hasAllocatedBudget = r.HasAllocatedBudget,
                    budgetDifference = r.BudgetDifference,
                    exceedsBudget = r.ExceedsBudget,
                    unusualChange = r.UnusualChange,
                    changePercent = r.ChangePercent,
                    riskTitle = r.RiskTitle,
                    riskDetail = r.RiskDetail,
                    isPrevious = r.IsPrevious,
                    generatedAt = PhilippinesTime.FormatLongDateTime(r.GeneratedAt == default ? page.GeneratedAt : r.GeneratedAt)
                })
            });
        }

        public async Task<IActionResult> GeneratedPayslips(int projectId, DateTime start, DateTime end, int page = 1)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            const int pageSize = 4;
            var slips = await _db.Set<Payroll>()
                .Include(p => p.Employee)
                .Include(p => p.Project)
                .Where(p => p.ProjectId == projectId
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date)
                .OrderByDescending(p => p.GeneratedDate)
                .ThenByDescending(p => p.PayrollId)
                .ToListAsync();

            var totalPages = Math.Max(1, (int)Math.Ceiling(slips.Count() / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            ViewBag.PageTitle = "View Payroll";
            ViewBag.ProjectId = projectId;
            ViewBag.ProjectName = project.ProjectName;
            ViewBag.Start = start.Date;
            ViewBag.End = end.Date;
            ViewBag.StartLabel = start.ToString("MMMM dd, yyyy", DateCulture);
            ViewBag.EndLabel = end.ToString("MMMM dd, yyyy", DateCulture);
            ViewBag.FileName = PayslipFileName(project.ProjectName, start, end);
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.IsFinished = ProjectStatusOptions.IsFinished(project.Status);
            return View(slips.Skip((page - 1) * pageSize).Take(pageSize).ToList());
        }

        public async Task<IActionResult> PrintPayslips(int projectId, DateTime start, DateTime end)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            var slips = (await _db.Set<Payroll>()
                .Include(p => p.Employee)
                .Include(p => p.Project)
                .Where(p => p.ProjectId == projectId
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date
                    && p.Status == PayrollStatusOptions.Approved)
                .ToListAsync())
                .OrderBy(p => p.Employee?.LastName)
                .ThenBy(p => p.Employee?.FirstName)
                .ToList();

            if (slips.Count == 0)
            {
                TempData["Error"] = "Payslips can be printed only after admin has approved this payroll.";
                return RedirectToAction(nameof(GeneratedPayslips), new { projectId, start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") });
            }

            ViewBag.ProjectName = project.ProjectName;
            ViewBag.FileName = PayslipFileName(project.ProjectName, start, end);
            ViewBag.StartLabel = start.ToString("MMMM dd, yyyy", DateCulture);
            ViewBag.EndLabel = end.ToString("MMMM dd, yyyy", DateCulture);
            return View(slips);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendToStaff(int projectId, DateTime start, DateTime end)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            if (ProjectStatusOptions.IsFinished(project.Status))
            {
                TempData["Error"] = "Finished projects cannot send payslips.";
                return RedirectToAction(nameof(GeneratedPayslips), new { projectId, start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") });
            }

            var slips = await _db.Set<Payroll>()
                .Where(p => p.ProjectId == projectId
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date)
                .Select(p => p.Status)
                .ToListAsync();

            if (slips.Count == 0)
            {
                TempData["Error"] = "There are no payslips to send for this period.";
                return RedirectToAction(nameof(GeneratedPayslips), new { projectId, start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") });
            }

            if (slips.Any(status => !PayrollStatusOptions.IsApproved(status)))
            {
                TempData["Error"] = "Payslips can be sent only after admin has approved this payroll.";
                return RedirectToAction(nameof(GeneratedPayslips), new { projectId, start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") });
            }

            var count = slips.Count;

            var staff = string.IsNullOrWhiteSpace(project.AssignedPayrollStaff)
                ? "payroll staff"
                : project.AssignedPayrollStaff;

            await _notifications.NotifyPayslipsSentAsync(project, start.Date, end.Date, count, HttpContext.RequestAborted);
            await _logs.LogAsync(
                ActivityTypes.SendPayslips,
                ActivityModules.Payroll,
                $"Sent {count} approved payslip(s) for {project.ProjectName} ({PayrollPeriods.Label(start.Date, end.Date)}) to {staff}.",
                project.ProjectId);

            TempData["Success"] = $"{PayslipFileName(project.ProjectName, start, end)} was sent to {staff}.";
            return RedirectToAction(nameof(GeneratedPayslips), new
            {
                projectId,
                start = start.ToString("yyyy-MM-dd"),
                end = end.ToString("yyyy-MM-dd")
            });
        }

        private async Task BindProjectSuggestionsAsync()
        {
            var projects = await _db.Projects
                .OrderBy(p => p.ProjectName)
                .Select(p => new
                {
                    id = p.ProjectId,
                    name = p.ProjectName,
                    location = p.Location
                })
                .ToListAsync();

            ViewBag.ProjectSuggestionsJson = JsonSerializer.Serialize(projects);
        }

        private async Task<List<PayrollPeriodRow>> LoadPeriodsAsync(
            string? projectName, int? month, int? projectId = null, bool approvedOnly = false)
        {
            var schedules = approvedOnly
                ? new List<PayrollSchedule>()
                : await _db.Set<PayrollSchedule>().Include(s => s.Project).ToListAsync();
            var payrolls = await _db.Set<Payroll>().Include(p => p.Project).ToListAsync();
            if (approvedOnly)
                payrolls = payrolls.Where(IsApproved).ToList();
            var rows = new Dictionary<string, PayrollPeriodRow>(StringComparer.OrdinalIgnoreCase);

            void Add(int projectId, string name, DateTime start, DateTime end, string? staff)
            {
                var key = projectId + "|" + start.ToString("yyyy-MM-dd") + "|" + end.ToString("yyyy-MM-dd");
                if (!rows.TryGetValue(key, out var row))
                {
                    rows[key] = new PayrollPeriodRow
                    {
                        ProjectId = projectId,
                        ProjectName = string.IsNullOrWhiteSpace(name) ? "—" : name,
                        StartDate = start.Date,
                        EndDate = end.Date,
                        PayrollStaff = string.IsNullOrWhiteSpace(staff) ? "—" : staff.Trim()
                    };
                    return;
                }

                if (row.PayrollStaff == "—" && !string.IsNullOrWhiteSpace(staff))
                    row.PayrollStaff = staff.Trim();
            }

            foreach (var schedule in schedules)
            {
                Add(schedule.ProjectId, schedule.Project?.ProjectName ?? "—",
                    schedule.StartingDate, schedule.EndDate, schedule.Project?.AssignedPayrollStaff);
            }

            foreach (var group in payrolls.GroupBy(p => new
            {
                p.ProjectId,
                Start = p.PayPeriodStart.Date,
                End = p.PayPeriodEnd.Date
            }))
            {
                var sample = group.First();
                var staff = group.Select(p => p.GeneratedBy).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
                    ?? sample.Project?.AssignedPayrollStaff;
                Add(group.Key.ProjectId, sample.Project?.ProjectName ?? "—",
                    group.Key.Start, group.Key.End, staff);
            }

            IEnumerable<PayrollPeriodRow> list = rows.Values;
            if (projectId.HasValue && projectId.Value > 0)
            {
                list = list.Where(r => r.ProjectId == projectId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(projectName))
            {
                var term = projectName.Trim();
                list = list.Where(r => r.ProjectName.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            if (month is >= 1 and <= 12)
                list = list.Where(r => r.StartDate.Month == month);

            return list
                .OrderByDescending(r => r.StartDate)
                .ThenByDescending(r => r.EndDate)
                .ThenBy(r => r.ProjectName)
                .ToList();
        }

        private async Task<List<PayrollPeriodEmployeeRow>> LoadPeriodEmployeesAsync(
            int projectId, DateTime start, DateTime end, bool approvedOnly = false)
        {
            var payrolls = await _db.Set<Payroll>()
                .Include(p => p.Employee)
                .Where(p => p.ProjectId == projectId
                    && p.PayPeriodStart.Date == start
                    && p.PayPeriodEnd.Date == end)
                .ToListAsync();

            if (approvedOnly)
                payrolls = payrolls.Where(IsApproved).ToList();

            if (approvedOnly)
            {
                return payrolls
                    .OrderBy(p => p.Employee?.LastName)
                    .ThenBy(p => p.Employee?.FirstName)
                    .Select(slip => new PayrollPeriodEmployeeRow
                    {
                        PayrollId = slip.PayrollId,
                        EmployeeName = slip.Employee?.FullName ?? "—",
                        Job = slip.Employee?.JobClassification ?? "—",
                        RegularHours = PayrollComputation.PaidRegularHours(slip),
                        OtHours = slip.OvertimeHours,
                        NetPay = slip.NetPay,
                        Status = slip.Status
                    })
                    .ToList();
            }

            var employees = await _db.Employees
                .Where(e => e.ProjectId == projectId && e.IsActive)
                .OrderBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToListAsync();

            return employees.Select(emp =>
            {
                var slip = payrolls.FirstOrDefault(p => p.EmployeeId == emp.EmployeeId);
                return new PayrollPeriodEmployeeRow
                {
                    PayrollId = slip?.PayrollId,
                    EmployeeName = emp.FullName,
                    Job = emp.JobClassification,
                    RegularHours = slip == null ? 0 : PayrollComputation.PaidRegularHours(slip),
                    OtHours = slip?.OvertimeHours ?? 0,
                    NetPay = slip?.NetPay ?? 0,
                    Status = slip?.Status ?? "—"
                };
            }).ToList();
        }

        private static bool IsApproved(Payroll payroll) =>
            string.Equals(payroll.Status?.Trim(), PayrollStatusOptions.Approved, StringComparison.OrdinalIgnoreCase);

        private static Dictionary<int, string> MonthOptions()
        {
            return Enumerable.Range(1, 12).ToDictionary(
                m => m,
                m => DateCulture.DateTimeFormat.GetMonthName(m));
        }

        private static string PayslipFileName(string projectName, DateTime start, DateTime end)
        {
            var safe = new string((projectName ?? "Project").Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(safe)) safe = "Project";
            return safe + "_Payslip_"
                + start.ToString("MMMdd", DateCulture)
                + "-"
                + end.ToString("MMMdd", DateCulture)
                + ".pdf";
        }

        // GET /Payroll/View/{id}
        public async Task<IActionResult> View(int id)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var payroll = await _db.Set<Payroll>()
                                   .Include(p => p.Employee)
                                   .Include(p => p.Project)
                                   .FirstOrDefaultAsync(p => p.PayrollId == id);

            if (payroll == null) return NotFound();

            ViewBag.DisplayId = EmployeeIds.Format(payroll.Employee?.EmployeeCode);
            return View(payroll);
        }

        // GET /Payroll/ReviewProject?projectId=
        public async Task<IActionResult> ReviewProject(int projectId, int page = 1)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            const int pageSize = 2;
            var slips = await _db.Set<Payroll>()
                .Include(p => p.Employee)
                .Include(p => p.Project)
                .Where(p => p.ProjectId == projectId && p.Status == PayrollStatusOptions.Submitted)
                .ToListAsync();

            if (slips.Count == 0)
                return RedirectToAction("Index", "Home");

            slips = slips
                .OrderBy(p => p.Employee?.LastName ?? "")
                .ThenBy(p => p.Employee?.FirstName ?? "")
                .ThenBy(p => p.PayPeriodStart)
                .ToList();

            var totalPages = Math.Max(1, (int)Math.Ceiling(slips.Count / (double)pageSize));
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            ViewBag.ProjectId = projectId;
            ViewBag.ProjectName = slips[0].Project?.ProjectName ?? "Project";
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            return View(slips.Skip((page - 1) * pageSize).Take(pageSize).ToList());
        }

        [HttpGet]
        public async Task<IActionResult> ViewPartial(int id)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var payroll = await _db.Set<Payroll>()
                                   .Include(p => p.Employee)
                                   .Include(p => p.Project)
                                   .FirstOrDefaultAsync(p => p.PayrollId == id);

            if (payroll == null) return NotFound();

            ViewBag.DisplayId = EmployeeIds.Format(payroll.Employee?.EmployeeCode);
            return PartialView("_PayrollPartial", payroll);
        }


        // POST /Payroll/Approve/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return Json(new { success = false, message = "Admin access is required." });

            var payroll = await _db.Set<Payroll>()
                .Include(p => p.Project)
                .Include(p => p.Employee)
                .FirstOrDefaultAsync(p => p.PayrollId == id);
            if (payroll == null)
                return Json(new { success = false, message = "Payroll record not found." });

            if (!PayrollStatusOptions.IsSubmitted(payroll.Status))
                return Json(new { success = false, message = "Only submitted payroll can be approved." });

            payroll.Status = PayrollStatusOptions.Approved;
            payroll.CorrectionReason = null;
            await _db.SaveChangesAsync();

            await _notifications.NotifyPayrollApprovedAsync(payroll, payroll.Project, HttpContext.RequestAborted);
            await _logs.LogAsync(
                ActivityTypes.ApprovePayroll,
                ActivityModules.Payroll,
                $"Approved payroll for {payroll.Employee?.FullName ?? "an employee"} on {payroll.Project?.ProjectName ?? "the project"}.",
                payroll.ProjectId,
                payroll.PayrollId);

            var remaining = await _db.Set<Payroll>().CountAsync(p =>
                p.ProjectId == payroll.ProjectId && p.Status == PayrollStatusOptions.Submitted);

            return Json(new
            {
                success = true,
                message = "Payroll has been approved.",
                remaining,
                projectId = payroll.ProjectId
            });
        }

        // POST /Payroll/ReturnForCorrection/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnForCorrection(int id, string reason)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return Json(new { success = false, message = "Admin access is required." });

            if (string.IsNullOrWhiteSpace(reason))
                return Json(new { success = false, message = "Please provide a reason for correction." });

            var payroll = await _db.Set<Payroll>()
                .Include(p => p.Project)
                .Include(p => p.Employee)
                .FirstOrDefaultAsync(p => p.PayrollId == id);
            if (payroll == null)
                return Json(new { success = false, message = "Payroll record not found." });

            if (!PayrollStatusOptions.IsSubmitted(payroll.Status))
                return Json(new { success = false, message = "Only submitted payroll can be returned for correction." });

            payroll.Status = PayrollStatusOptions.Correction;
            payroll.CorrectionReason = reason.Trim();
            await _db.SaveChangesAsync();

            await _notifications.NotifyPayrollReturnedAsync(payroll, payroll.Project, reason.Trim(), HttpContext.RequestAborted);
            await _logs.LogAsync(
                ActivityTypes.ReturnPayroll,
                ActivityModules.Payroll,
                $"Returned payroll for {payroll.Employee?.FullName ?? "an employee"} on {payroll.Project?.ProjectName ?? "the project"}.",
                payroll.ProjectId,
                payroll.PayrollId);

            var remaining = await _db.Set<Payroll>().CountAsync(p =>
                p.ProjectId == payroll.ProjectId && p.Status == PayrollStatusOptions.Submitted);

            return Json(new
            {
                success = true,
                message = "Payroll has been returned for correction.",
                remaining,
                projectId = payroll.ProjectId
            });
        }


        // POST /Payroll/AddSchedule
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSchedule(int ProjectId, string? TypeOfService,
            DateTime StartingDate, DateTime EndDate)
        {
            var projectType = await ProjectTypeAsync(ProjectId);
            var error = await ValidateScheduleAsync(ProjectId, projectType, StartingDate, EndDate);
            if (error != null)
            {
                TempData["Error"] = error;
                return RedirectToAction("Index", "Home");
            }

            var schedule = new PayrollSchedule
            {
                ProjectId = ProjectId,
                TypeOfService = projectType,
                StartingDate = StartingDate.Date,
                EndDate = EndDate.Date
            };

            _db.Set<PayrollSchedule>().Add(schedule);
            await _db.SaveChangesAsync();

            var assigned = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == ProjectId);
            if (assigned != null)
                await _notifications.NotifyNewTaskAsync(assigned, StartingDate.Date, EndDate.Date, HttpContext.RequestAborted);

            TempData["Success"] = "Schedule added. Payroll staff can generate payroll for this period.";
            return RedirectToAction("Index", "Home");
        }

        // POST /Payroll/EditSchedule
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSchedule(int PayrollScheduleId, int ProjectId,
            string? TypeOfService, DateTime StartingDate, DateTime EndDate)
        {
            var existing = await _db.Set<PayrollSchedule>().FindAsync(PayrollScheduleId);
            if (existing == null) return NotFound();

            var projectType = await ProjectTypeAsync(ProjectId);
            var error = await ValidateScheduleAsync(ProjectId, projectType, StartingDate, EndDate, PayrollScheduleId);
            if (error != null)
            {
                TempData["Error"] = error;
                return RedirectToAction("Index", "Home");
            }

            existing.ProjectId = ProjectId;
            existing.TypeOfService = projectType;
            existing.StartingDate = StartingDate.Date;
            existing.EndDate = EndDate.Date;

            await _db.SaveChangesAsync();
            TempData["Success"] = "Schedule updated.";
            return RedirectToAction("Index", "Home");
        }

        private async Task<string?> ValidateScheduleAsync(
            int projectId,
            string? typeOfService,
            DateTime startingDate,
            DateTime endDate,
            int? excludeScheduleId = null)
        {
            if (projectId <= 0)
                return "Please select a project.";

            if (string.IsNullOrWhiteSpace(typeOfService))
                return "Type of project is required.";

            var rangeErrors = DateRules.ValidateDateRange(
                startingDate, endDate,
                nameof(PayrollSchedule.StartingDate), nameof(PayrollSchedule.EndDate),
                "Starting date", "End date").ToList();
            if (rangeErrors.Count > 0)
                return rangeErrors[0].ErrorMessage;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null)
                return "Selected project was not found.";

            if (DateRules.IsUsableDate(project.StartingDate) && startingDate.Date < project.StartingDate!.Value.Date)
                return "Starting date cannot be before the project starting date.";

            if (DateRules.IsUsableDate(project.EstimateEndDate) && startingDate.Date > project.EstimateEndDate!.Value.Date)
                return "Starting date cannot be after the project estimate end date.";

            if (DateRules.IsUsableDate(project.StartingDate) && endDate.Date < project.StartingDate!.Value.Date)
                return "End date cannot be before the project starting date.";

            if (DateRules.IsUsableDate(project.EstimateEndDate) && endDate.Date > project.EstimateEndDate!.Value.Date)
                return "End date cannot be after the project estimate end date.";

            var overlaps = await _db.Set<PayrollSchedule>().AnyAsync(s =>
                s.ProjectId == projectId &&
                (!excludeScheduleId.HasValue || s.PayrollScheduleId != excludeScheduleId.Value) &&
                s.StartingDate.Date <= endDate.Date &&
                startingDate.Date <= s.EndDate.Date);

            if (overlaps)
                return "This date range overlaps an existing payroll schedule for the same project.";

            return null;
        }

        private async Task<string> ProjectTypeAsync(int projectId)
        {
            var type = await _db.Projects.AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .Select(p => p.TypeOfService)
                .FirstOrDefaultAsync();
            return string.IsNullOrWhiteSpace(type) ? "" : type.Trim();
        }

        // POST /Payroll/DeleteSchedule/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteSchedule(int id)
        {
            TempData["Error"] = "Payroll schedules cannot be deleted. Edit the dates instead, or wait until payroll staff mark the task as done.";
            return RedirectToAction("Index", "Home");
        }
    }
}