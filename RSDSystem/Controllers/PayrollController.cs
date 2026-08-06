using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

        public IActionResult Index()
        {
            return View();
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