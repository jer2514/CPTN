using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;

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
            if (ProjectId == 0)
            {
                TempData["Error"] = "Please select a project.";
                return RedirectToAction("Index", "Home");
            }

            if (StartingDate > EndDate)
            {
                TempData["Error"] = "Starting Date must be before End Date.";
                return RedirectToAction("Index", "Home");
            }

            var schedule = new PayrollSchedule
            {
                ProjectId = ProjectId,
                TypeOfService = TypeOfService,
                StartingDate = StartingDate,
                EndDate = EndDate
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

            if (StartingDate > EndDate)
            {
                TempData["Error"] = "Starting Date must be before End Date.";
                return RedirectToAction("Index", "Home");
            }

            existing.ProjectId = ProjectId;
            existing.TypeOfService = TypeOfService;
            existing.StartingDate = StartingDate;
            existing.EndDate = EndDate;

            await _db.SaveChangesAsync();
            TempData["Success"] = "Schedule updated.";
            return RedirectToAction("Index", "Home");
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