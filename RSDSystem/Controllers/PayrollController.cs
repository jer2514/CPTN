using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Validation;

namespace RSDSystem.Controllers
{
    public class PayrollController : Controller
    {
        private readonly PayrollDbContext _db;
        private static readonly CultureInfo DateCulture = CultureInfo.InvariantCulture;

        public PayrollController(PayrollDbContext db)
        {
            _db = db;
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
            var periods = await LoadPeriodsAsync(projectName, month, projectId);
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
            return View(await LoadPeriodEmployeesAsync(projectId, start.Date, end.Date));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePayslips(int projectId, DateTime start, DateTime end)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            var employees = await _db.Employees
                .Where(e => e.ProjectId == projectId && e.IsActive)
                .ToListAsync();

            if (employees.Count == 0)
            {
                TempData["Error"] = "No active employees are assigned to this project.";
                return RedirectToAction(nameof(Period), new { projectId, start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") });
            }

            var existing = await _db.Payrolls
                .Where(p => p.ProjectId == projectId
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date)
                .ToListAsync();

            var daysWorked = Math.Max(1, InputRules.CountWeekdays(start.Date, end.Date));
            var generatedBy = HttpContext.Session.GetString("FullName") ?? "Admin";
            var created = 0;

            foreach (var emp in employees)
            {
                if (existing.Any(p => p.EmployeeId == emp.EmployeeId))
                    continue;

                var regularPay = emp.DailyRate * daysWorked;
                _db.Payrolls.Add(new Payroll
                {
                    EmployeeId = emp.EmployeeId,
                    ProjectId = projectId,
                    PayPeriodStart = start.Date,
                    PayPeriodEnd = end.Date,
                    RegularDaysWorked = daysWorked,
                    OvertimeHours = 0,
                    AbsentDays = 0,
                    RegularPay = regularPay,
                    OvertimePay = 0,
                    GrossPay = regularPay,
                    CashAdvance = 0,
                    NetPay = regularPay,
                    Status = PayrollStatusOptions.Draft,
                    GeneratedBy = generatedBy,
                    GeneratedDate = DateTime.Now
                });
                created++;
            }

            if (created > 0)
                await _db.SaveChangesAsync();

            TempData["Success"] = created > 0
                ? $"Generated {created} payslip(s) for {project.ProjectName}."
                : "Payslips for this period already exist.";

            return RedirectToAction(nameof(Prediction), new
            {
                projectId,
                start = start.ToString("yyyy-MM-dd"),
                end = end.ToString("yyyy-MM-dd")
            });
        }

        public async Task<IActionResult> Prediction(int? projectId, DateTime? start, DateTime? end,
            string? projectName, int? month, int page = 1)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            ViewBag.PageTitle = "Payroll Prediction";
            ViewBag.ProjectNameFilter = projectName ?? "";
            ViewBag.SelectedProjectId = !start.HasValue || !end.HasValue ? projectId : null;
            ViewBag.Month = month;
            ViewBag.Months = MonthOptions();

            if (!projectId.HasValue || !start.HasValue || !end.HasValue)
            {
                await BindProjectSuggestionsAsync();
                ViewBag.HasPeriod = false;
                return View("PredictionList", await LoadPeriodsAsync(projectName, month, projectId));
            }

            var project = await _db.Projects.FindAsync(projectId.Value);
            if (project == null) return NotFound();

            const int pageSize = 4;
            var slips = await _db.Payrolls
                .Include(p => p.Employee)
                .Where(p => p.ProjectId == projectId.Value
                    && p.PayPeriodStart.Date == start.Value.Date
                    && p.PayPeriodEnd.Date == end.Value.Date)
                .OrderByDescending(p => p.GeneratedDate)
                .ThenByDescending(p => p.PayrollId)
                .ToListAsync();

            var totalPages = Math.Max(1, (int)Math.Ceiling(slips.Count / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            ViewBag.HasPeriod = true;
            ViewBag.ProjectId = projectId.Value;
            ViewBag.ProjectName = project.ProjectName;
            ViewBag.Start = start.Value.Date;
            ViewBag.End = end.Value.Date;
            ViewBag.StartLabel = start.Value.ToString("MMMM dd, yyyy", DateCulture);
            ViewBag.EndLabel = end.Value.ToString("MMMM dd, yyyy", DateCulture);
            ViewBag.FileName = PayslipFileName(project.ProjectName, start.Value, end.Value);
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalSlips = slips.Count;

            return View(slips.Skip((page - 1) * pageSize).Take(pageSize).ToList());
        }

        public async Task<IActionResult> PrintPayslips(int projectId, DateTime start, DateTime end)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            var slips = await _db.Payrolls
                .Include(p => p.Employee)
                .Include(p => p.Project)
                .Where(p => p.ProjectId == projectId
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date)
                .OrderBy(p => p.Employee!.LastName)
                .ThenBy(p => p.Employee!.FirstName)
                .ToListAsync();

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

            var count = await _db.Payrolls.CountAsync(p =>
                p.ProjectId == projectId
                && p.PayPeriodStart.Date == start.Date
                && p.PayPeriodEnd.Date == end.Date);

            if (count == 0)
            {
                TempData["Error"] = "Generate payslips before sending them to payroll staff.";
                return RedirectToAction(nameof(Period), new { projectId, start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd") });
            }

            var staff = string.IsNullOrWhiteSpace(project.AssignedPayrollStaff)
                ? "payroll staff"
                : project.AssignedPayrollStaff;

            TempData["Success"] = $"{PayslipFileName(project.ProjectName, start, end)} was sent to {staff}.";
            return RedirectToAction(nameof(Prediction), new
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

        private async Task<List<PayrollPeriodRow>> LoadPeriodsAsync(string? projectName, int? month, int? projectId = null)
        {
            var schedules = await _db.PayrollSchedules.Include(s => s.Project).ToListAsync();
            var payrolls = await _db.Payrolls.Include(p => p.Project).ToListAsync();
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
                list = list.Where(r => r.StartDate.Month == month || r.EndDate.Month == month);

            return list
                .OrderByDescending(r => r.StartDate)
                .ThenByDescending(r => r.EndDate)
                .ThenBy(r => r.ProjectName)
                .ToList();
        }

        private async Task<List<PayrollPeriodEmployeeRow>> LoadPeriodEmployeesAsync(
            int projectId, DateTime start, DateTime end)
        {
            var employees = await _db.Employees
                .Where(e => e.ProjectId == projectId && e.IsActive)
                .OrderBy(e => e.LastName)
                .ThenBy(e => e.FirstName)
                .ToListAsync();

            var payrolls = await _db.Payrolls
                .Where(p => p.ProjectId == projectId
                    && p.PayPeriodStart.Date == start
                    && p.PayPeriodEnd.Date == end)
                .ToListAsync();

            return employees.Select(emp =>
            {
                var slip = payrolls.FirstOrDefault(p => p.EmployeeId == emp.EmployeeId);
                return new PayrollPeriodEmployeeRow
                {
                    PayrollId = slip?.PayrollId,
                    EmployeeName = emp.FullName,
                    Job = emp.JobClassification,
                    RegularHours = slip == null ? 0 : slip.RegularDaysWorked * 8,
                    OtHours = slip?.OvertimeHours ?? 0,
                    NetPay = slip?.NetPay ?? 0,
                    Status = slip?.Status ?? "—"
                };
            }).ToList();
        }

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
            var payroll = await _db.Payrolls
                                   .Include(p => p.Employee)
                                   .Include(p => p.Project)
                                   .FirstOrDefaultAsync(p => p.PayrollId == id);

            if (payroll == null) return NotFound();

            ViewBag.DisplayId = IdFormatter.Format(payroll.Employee?.EmployeeCode);
            return View(payroll);
        }

        [HttpGet]
        public async Task<IActionResult> ViewPartial(int id)
        {
            var payroll = await _db.Payrolls
                                   .Include(p => p.Employee)
                                   .Include(p => p.Project)
                                   .FirstOrDefaultAsync(p => p.PayrollId == id);

            if (payroll == null) return NotFound();

            ViewBag.DisplayId = IdFormatter.Format(payroll.Employee?.EmployeeCode);
            return PartialView("_PayrollPartial", payroll);
        }


        // POST /Payroll/Approve/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id)
        {
            var payroll = await _db.Payrolls.FindAsync(id);
            if (payroll == null)
                return Json(new { success = false, message = "Payroll record not found." });

            payroll.Status = PayrollStatusOptions.Approved;
            payroll.CorrectionReason = null;
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = "Payroll has been approved." });
        }

        // POST /Payroll/ReturnForCorrection/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnForCorrection(int id, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return Json(new { success = false, message = "Please provide a reason for correction." });

            var payroll = await _db.Payrolls.FindAsync(id);
            if (payroll == null)
                return Json(new { success = false, message = "Payroll record not found." });

            payroll.Status = PayrollStatusOptions.Correction;
            payroll.CorrectionReason = reason.Trim();
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = "Payroll has been returned for correction." });
        }


        // POST /Payroll/AddSchedule
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSchedule(int ProjectId, string? TypeOfService,
            DateTime StartingDate, DateTime EndDate)
        {
            var error = await ValidateScheduleAsync(ProjectId, TypeOfService, StartingDate, EndDate);
            if (error != null)
            {
                TempData["Error"] = error;
                return RedirectToAction("Index", "Home");
            }

            var schedule = new PayrollSchedule
            {
                ProjectId = ProjectId,
                TypeOfService = TypeOfService?.Trim(),
                StartingDate = StartingDate.Date,
                EndDate = EndDate.Date
            };

            _db.PayrollSchedules.Add(schedule);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Schedule added.";
            return RedirectToAction("Index", "Home");
        }

        // POST /Payroll/EditSchedule
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditSchedule(int PayrollScheduleId, int ProjectId,
            string? TypeOfService, DateTime StartingDate, DateTime EndDate)
        {
            var existing = await _db.PayrollSchedules.FindAsync(PayrollScheduleId);
            if (existing == null) return NotFound();

            var error = await ValidateScheduleAsync(ProjectId, TypeOfService, StartingDate, EndDate, PayrollScheduleId);
            if (error != null)
            {
                TempData["Error"] = error;
                return RedirectToAction("Index", "Home");
            }

            existing.ProjectId = ProjectId;
            existing.TypeOfService = TypeOfService?.Trim();
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

            var rangeErrors = InputRules.ValidateDateRange(
                startingDate, endDate,
                nameof(PayrollSchedule.StartingDate), nameof(PayrollSchedule.EndDate),
                "Starting date", "End date").ToList();
            if (rangeErrors.Count > 0)
                return rangeErrors[0].ErrorMessage;

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null)
                return "Selected project was not found.";

            if (InputRules.IsUsableDate(project.StartingDate) && startingDate.Date < project.StartingDate!.Value.Date)
                return "Starting date cannot be before the project starting date.";

            if (InputRules.IsUsableDate(project.EstimateEndDate) && startingDate.Date > project.EstimateEndDate!.Value.Date)
                return "Starting date cannot be after the project estimate end date.";

            if (InputRules.IsUsableDate(project.StartingDate) && endDate.Date < project.StartingDate!.Value.Date)
                return "End date cannot be before the project starting date.";

            if (InputRules.IsUsableDate(project.EstimateEndDate) && endDate.Date > project.EstimateEndDate!.Value.Date)
                return "End date cannot be after the project estimate end date.";

            var overlaps = await _db.PayrollSchedules.AnyAsync(s =>
                s.ProjectId == projectId &&
                (!excludeScheduleId.HasValue || s.PayrollScheduleId != excludeScheduleId.Value) &&
                s.StartingDate.Date <= endDate.Date &&
                startingDate.Date <= s.EndDate.Date);

            if (overlaps)
                return "This date range overlaps an existing payroll schedule for the same project.";

            return null;
        }

        // POST /Payroll/DeleteSchedule/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSchedule(int id)
        {
            var schedule = await _db.PayrollSchedules.FindAsync(id);
            if (schedule != null)
            {
                _db.PayrollSchedules.Remove(schedule);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Schedule deleted.";
            }
            return RedirectToAction("Index", "Home");
        }
    }
}