using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;
using RSDSystem.Validation;

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
            ViewBag.ActivePayrollStaff = await _db.Users.CountAsync(u => u.Role == "PayrollStaff" && u.IsActive);

            var activeProjectRows = await _db.Projects
                .AsNoTracking()
                .Where(p => p.Status == "Active")
                .OrderBy(p => p.ProjectName)
                .Select(p => new
                {
                    p.ProjectId,
                    ProjectName = p.ProjectName ?? "",
                    Location = p.Location ?? "",
                    TypeOfService = p.TypeOfService ?? "",
                    p.StartingDate,
                    p.EstimateEndDate,
                    p.PayrollBudget,
                    PayrollDistribution = p.PayrollDistribution ?? "",
                    AssignedPayrollStaff = p.AssignedPayrollStaff ?? "",
                    Status = p.Status ?? "Active",
                    p.TaskCompleted
                })
                .ToListAsync();

            var activeProjects = activeProjectRows.Select(p => new Project
            {
                ProjectId = p.ProjectId,
                ProjectName = p.ProjectName,
                Location = p.Location,
                TypeOfService = p.TypeOfService,
                StartingDate = p.StartingDate,
                EstimateEndDate = p.EstimateEndDate,
                PayrollBudget = p.PayrollBudget,
                PayrollDistribution = p.PayrollDistribution,
                AssignedPayrollStaff = p.AssignedPayrollStaff,
                Status = p.Status,
                TaskCompleted = p.TaskCompleted
            }).ToList();

            ViewBag.ActiveProjectsList = activeProjects;
            ViewBag.TypeOfServiceOptions = TypeOfServiceOptions.All;
            ViewBag.ProjectTypeMap = activeProjects.ToDictionary(p => p.ProjectId, p => p.TypeOfService ?? "");
            ViewBag.ProjectDateMap = activeProjects.ToDictionary(
                p => p.ProjectId,
                p => new ProjectDateBounds
                {
                    Start = InputRules.IsUsableDate(p.StartingDate)
                        ? p.StartingDate!.Value.ToString("yyyy-MM-dd")
                        : "",
                    End = InputRules.IsUsableDate(p.EstimateEndDate)
                        ? p.EstimateEndDate!.Value.ToString("yyyy-MM-dd")
                        : ""
                });

            var scheduleRows = await _db.PayrollSchedules
                .AsNoTracking()
                .OrderBy(s => s.StartingDate)
                .Select(s => new
                {
                    s.PayrollScheduleId,
                    s.ProjectId,
                    TypeOfService = s.TypeOfService ?? "",
                    s.StartingDate,
                    s.EndDate,
                    ProjectName = s.Project != null ? s.Project.ProjectName ?? "" : ""
                })
                .ToListAsync();

            ViewBag.PayrollSchedules = scheduleRows.Select(s => new PayrollSchedule
            {
                PayrollScheduleId = s.PayrollScheduleId,
                ProjectId = s.ProjectId,
                TypeOfService = s.TypeOfService,
                StartingDate = s.StartingDate,
                EndDate = s.EndDate,
                Project = new Project
                {
                    ProjectId = s.ProjectId,
                    ProjectName = s.ProjectName
                }
            }).ToList();

            ViewBag.PendingApprovals = await _db.Payrolls
                .AsNoTracking()
                .Where(p => p.Status == PayrollStatusOptions.Submitted)
                .OrderByDescending(p => p.GeneratedDate)
                .Select(p => new PendingApprovalRow
                {
                    PayrollId = p.PayrollId,
                    StaffName = p.GeneratedBy ?? "",
                    ProjectName = p.Project != null && p.Project.ProjectName != null && p.Project.ProjectName != ""
                        ? p.Project.ProjectName
                        : "—",
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
