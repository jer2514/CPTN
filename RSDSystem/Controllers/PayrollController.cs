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

        public PayrollController(PayrollDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var submitted = await _db.Payrolls
                                     .Include(p => p.Employee)
                                     .Include(p => p.Project)
                                     .Where(p => p.Status == PayrollStatusOptions.Submitted)
                                     .OrderByDescending(p => p.GeneratedDate)
                                     .ToListAsync();

            ViewBag.PageTitle = "Payroll";
            return View(submitted);
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