using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    public class CashAdvanceController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly CashAdvanceService _advances;
        private readonly NotificationService _notifications;
        private readonly ActivityLogService _logs;

        public CashAdvanceController(
            PayrollDbContext db,
            CashAdvanceService advances,
            NotificationService notifications,
            ActivityLogService logs)
        {
            _db = db;
            _advances = advances;
            _notifications = notifications;
            _logs = logs;
        }

        public async Task<IActionResult> Index(int? projectId, string? search, string? status, int page = 1)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            ViewBag.PageTitle = "Cash Advance";
            ViewBag.Projects = await _db.Projects.AsNoTracking()
                .OrderBy(p => p.ProjectName)
                .ToListAsync();
            ViewBag.SelectedProjectId = projectId;
            ViewBag.Search = search ?? "";
            ViewBag.Status = string.IsNullOrWhiteSpace(status) ? "all" : status;

            if (projectId is null or <= 0)
            {
                ViewBag.NeedsProject = true;
                ViewBag.Totals = new CashAdvanceTotals();
                ViewBag.CurrentPage = 1;
                ViewBag.TotalPages = 1;
                return View(new List<CashAdvanceEmployeeRow>());
            }

            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);
            if (project == null)
                return NotFound();

            ViewBag.NeedsProject = false;
            ViewBag.ProjectName = project.ProjectName;
            ViewBag.IsFinished = ProjectStatusOptions.IsFinished(project.Status);
            ViewBag.Totals = await _advances.ProjectTotalsAsync(project.ProjectId);

            var rows = await _advances.EmployeeRowsAsync(project.ProjectId, search, status);
            const int pageSize = 10;
            var totalPages = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            return View(rows.Skip((page - 1) * pageSize).Take(pageSize).ToList());
        }

        public async Task<IActionResult> Employee(int projectId, int employeeId, string? status, int page = 1)
        {
            var blocked = RequireAdmin();
            if (blocked != null) return blocked;

            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);
            var employee = await _db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
            if (project == null || employee == null)
                return NotFound();

            var rows = await _db.CashAdvances.AsNoTracking()
                .Where(c => c.ProjectId == projectId && c.EmployeeId == employeeId)
                .OrderByDescending(c => c.AdvanceDate)
                .ThenByDescending(c => c.CashAdvanceId)
                .ToListAsync();

            var filter = string.IsNullOrWhiteSpace(status) ? "all" : status.Trim().ToLowerInvariant();
            var visible = filter switch
            {
                "unpaid" => rows.Where(r => CashAdvanceStatuses.IsUnpaid(r.Status)).ToList(),
                "paid" => rows.Where(r => CashAdvanceStatuses.IsPaid(r.Status)).ToList(),
                _ => rows
            };

            const int pageSize = 10;
            var totalPages = Math.Max(1, (int)Math.Ceiling(visible.Count / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            ViewBag.PageTitle = "Employee Cash Advance";
            ViewBag.Project = project;
            ViewBag.Employee = employee;
            ViewBag.DisplayId = EmployeeIds.Format(employee.EmployeeCode);
            ViewBag.IsFinished = ProjectStatusOptions.IsFinished(project.Status);
            ViewBag.Status = filter;
            ViewBag.Total = rows.Sum(r => r.Amount);
            ViewBag.Outstanding = rows.Where(r => r.Status == CashAdvanceStatuses.Outstanding).Sum(r => r.Amount);
            ViewBag.Unpaid = rows.Where(r => CashAdvanceStatuses.IsUnpaid(r.Status)).Sum(r => r.Amount);
            ViewBag.Paid = rows.Where(r => r.Status == CashAdvanceStatuses.Deducted).Sum(r => r.Amount);
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            return View(visible.Skip((page - 1) * pageSize).Take(pageSize).ToList());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(int projectId, int employeeId, string? advanceDate, decimal amount, string? reason)
        {
            var blocked = RequireAdminJson();
            if (blocked != null) return blocked;

            DateTime? date = null;
            if (DateTime.TryParse(advanceDate, out var parsed))
                date = parsed.Date;

            var (error, entry) = await _advances.AddAsync(
                projectId, employeeId, date, amount, reason, ActorName());
            if (error != null)
                return Json(new { success = false, message = error });

            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);
            var employee = await _db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmployeeId == employeeId);
            if (project != null)
            {
                await _notifications.NotifyCashAdvanceAddedAsync(
                    project, employee?.FullName ?? "an employee", entry!.Amount, HttpContext.RequestAborted);
            }

            await _logs.LogAsync(
                ActivityTypes.AddCashAdvance,
                ActivityModules.Payroll,
                $"Added a ₱{entry!.Amount:N2} cash advance for {employee?.FullName ?? "an employee"} on {project?.ProjectName ?? "the project"}.",
                projectId,
                entry.CashAdvanceId);

            return Json(new { success = true, message = "Cash advance added to the outstanding balance." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deduct(int id)
        {
            var blocked = RequireAdminJson();
            if (blocked != null) return blocked;

            var (error, entry) = await _advances.MarkOneAsync(id, ActorName());
            if (error != null)
                return Json(new { success = false, message = error });

            if (entry?.Project != null)
            {
                await _notifications.NotifyCashAdvanceDeductionAsync(
                    entry.Project,
                    entry.Employee?.FullName ?? "an employee",
                    entry.Amount,
                    wholeBalance: false,
                    HttpContext.RequestAborted);
            }

            await _logs.LogAsync(
                ActivityTypes.DeductCashAdvance,
                ActivityModules.Payroll,
                $"Marked a ₱{entry!.Amount:N2} cash advance for deduction on the next payroll.",
                entry.ProjectId,
                entry.CashAdvanceId);

            return Json(new
            {
                success = true,
                amount = entry.Amount,
                employeeName = entry.Employee?.FullName ?? "the employee",
                whole = false
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeductTotal(int projectId, int employeeId)
        {
            var blocked = RequireAdminJson();
            if (blocked != null) return blocked;

            var (error, amount, count, name) = await _advances.MarkOutstandingAsync(
                projectId, employeeId, ActorName());
            if (error != null)
                return Json(new { success = false, message = error });

            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);
            if (project != null)
            {
                await _notifications.NotifyCashAdvanceDeductionAsync(
                    project, name, amount, wholeBalance: true, HttpContext.RequestAborted);
            }

            await _logs.LogAsync(
                ActivityTypes.DeductCashAdvance,
                ActivityModules.Payroll,
                $"Marked the ₱{amount:N2} outstanding cash advance ({count} row(s)) for {name} for the next payroll.",
                projectId,
                employeeId);

            return Json(new { success = true, amount, employeeName = name, whole = true });
        }

        private IActionResult? RequireAdmin()
        {
            if (!IsAdmin)
                return RedirectToAction("Index", "PayrollStaff");
            return null;
        }

        private IActionResult? RequireAdminJson()
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });
            return null;
        }

        private bool IsAdmin =>
            string.Equals(HttpContext.Session.GetString("Role"), "Admin", StringComparison.OrdinalIgnoreCase);

        private string ActorName() =>
            HttpContext.Session.GetString("FullName") ?? "Admin";
    }
}
