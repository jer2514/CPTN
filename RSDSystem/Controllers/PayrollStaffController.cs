using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Controllers
{
    public class PayrollStaffController : Controller
    {
        private readonly PayrollDbContext _db;

        // TODO: replace with the signed-in user's FullName once auth/session is wired up
        private const string CurrentStaffName = "Patrick Bateman";

        public PayrollStaffController(PayrollDbContext db)
        {
            _db = db;
        }

        // GET /PayrollStaff  → "To do task" dashboard
        public async Task<IActionResult> Index()
        {
            var tasks = await _db.Projects
                                 .Where(p => p.AssignedPayrollStaff == CurrentStaffName
                                          && p.Status == "Active")
                                 .OrderBy(p => p.StartingDate)
                                 .ToListAsync();

            ViewBag.PageTitle = "To do task";
            return View(tasks);
        }

        // POST /PayrollStaff/ToggleTask/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTask(int id)
        {
            var project = await _db.Projects.FindAsync(id);
            if (project != null)
            {
                project.TaskCompleted = !project.TaskCompleted;
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}