using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly PayrollDbContext _db;

        public HomeController(ILogger<HomeController> logger, PayrollDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.ActiveProjects = await _db.Projects.CountAsync(p => p.Status == "Active");
            ViewBag.ActiveEmployees = await _db.Employees.CountAsync(e => e.IsActive);
            ViewBag.ActivePayrollStaff = await _db.Users
                                                   .CountAsync(u => u.Role == "PayrollStaff" && u.IsActive);

            // TODO: replace with real query once Payroll Approval module exists
            ViewBag.PendingApprovals = new List<object>();

            ViewBag.PayrollSchedules = await _db.PayrollSchedules
                                                .Include(s => s.Project)
                                                .OrderBy(s => s.StartingDate)
                                                .ToListAsync();

            var activeProjectsList = await _db.Projects
                                              .Where(p => p.Status == "Active")
                                              .OrderBy(p => p.ProjectName)
                                              .ToListAsync();

            ViewBag.ActiveProjectsList = activeProjectsList;
            ViewBag.TypeOfServiceOptions = TypeOfServiceOptions.All;
            ViewBag.ProjectTypeMap = activeProjectsList
                .ToDictionary(p => p.ProjectId, p => p.TypeOfService ?? "");

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}