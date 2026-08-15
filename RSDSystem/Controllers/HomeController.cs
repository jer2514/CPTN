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
            // Stat cards
            ViewBag.ActiveProjects = await _db.Projects.CountAsync(p => p.Status == "Active");
            ViewBag.ActiveEmployees = await _db.Employees.CountAsync(e => e.IsActive);
            ViewBag.ActivePayrollStaff = await _db.Users.CountAsync(u => u.Role == "PayrollStaff" && u.IsActive);

            // For the Add/Edit Schedule modal dropdowns
            var activeProjects = await _db.Projects
                                          .Where(p => p.Status == "Active")
                                          .OrderBy(p => p.ProjectName)
                                          .ToListAsync();
            ViewBag.ActiveProjectsList = activeProjects;
            ViewBag.TypeOfServiceOptions = TypeOfServiceOptions.All;
            ViewBag.ProjectTypeMap = activeProjects.ToDictionary(p => p.ProjectId, p => p.TypeOfService ?? "");
            ViewBag.ProjectDateMap = activeProjects.ToDictionary(
                p => p.ProjectId,
                p => new
                {
                    start = p.StartingDate.HasValue && p.StartingDate.Value.Year > 1900
                        ? p.StartingDate.Value.ToString("yyyy-MM-dd")
                        : "",
                    end = p.EstimateEndDate.HasValue && p.EstimateEndDate.Value.Year > 1900
                        ? p.EstimateEndDate.Value.ToString("yyyy-MM-dd")
                        : ""
                });

            // Payroll schedules list
            ViewBag.PayrollSchedules = await _db.PayrollSchedules
                                                .Include(s => s.Project)
                                                .OrderBy(s => s.StartingDate)
                                                .ToListAsync();

            // Pending Payroll Approval table
            ViewBag.PendingApprovals = await _db.Payrolls
                                                .Include(p => p.Project)
                                                .Where(p => p.Status == PayrollStatusOptions.Submitted)
                                                .OrderByDescending(p => p.GeneratedDate)
                                                .Select(p => new
                                                {
                                                    p.PayrollId,
                                                    StaffName = p.GeneratedBy,
                                                    ProjectName = p.Project != null ? p.Project.ProjectName : "—",
                                                    Date = p.GeneratedDate
                                                })
                                                .ToListAsync();

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