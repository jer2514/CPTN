using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;
using RSDSystem.Validation;

namespace RSDSystem.Controllers
{
    /// <summary>
    /// Admin dashboard after login.
    /// Loads: counts, ongoing projects, payroll schedules (add/edit/delete live here
    /// via PayrollController), and Submitted payroll waiting for review.
    /// </summary>
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly PayrollDbContext _db;

        /// <summary>
        /// Receives logging and the payroll database used to fill the admin dashboard.
        /// </summary>
        public HomeController(ILogger<HomeController> logger, PayrollDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        /// <summary>
        /// Admin home: project list + pending payroll approvals.
        /// GET /Home (after Admin login). Fills counts, ongoing projects, payroll schedules, and Submitted slips.
        /// The dashboard Add/Edit/Delete schedule buttons post to PayrollController, not here.
        /// </summary>
        /// <returns>Views/Home/Index.cshtml with ViewBag data for the dashboard cards and tables.</returns>
        public async Task<IActionResult> Index()
        {
            ViewBag.ActiveProjects = await _db.Projects.Ongoing().CountAsync();
            ViewBag.ActiveEmployees = await _db.Employees.CountAsync(e => e.IsActive);
            ViewBag.ActivePayrollStaff = await _db.Users.CountAsync(u => u.Role == "PayrollStaff" && u.IsActive);

            var activeProjectRows = await _db.Projects
                .AsNoTracking()
                .Ongoing()
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
                    Status = p.Status ?? "On Going",
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
                    Start = DateRules.IsUsableDate(p.StartingDate)
                        ? p.StartingDate!.Value.ToString("yyyy-MM-dd")
                        : "",
                    End = DateRules.IsUsableDate(p.EstimateEndDate)
                        ? p.EstimateEndDate!.Value.ToString("yyyy-MM-dd")
                        : ""
                });

            var scheduleRows = await _db.Set<PayrollSchedule>()
                .AsNoTracking()
                .Where(s => !s.TaskCompleted)
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

            var submitted = await _db.Set<Payroll>()
                .AsNoTracking()
                .Where(p => p.Status == PayrollStatusOptions.Submitted)
                .Select(p => new
                {
                    p.ProjectId,
                    p.GeneratedBy,
                    p.GeneratedDate,
                    p.SubmittedAt,
                    ProjectName = p.Project != null ? p.Project.ProjectName ?? "" : "",
                    AssignedStaff = p.Project != null ? p.Project.AssignedPayrollStaff ?? "" : ""
                })
                .ToListAsync();

            // One pending-approval card per project: the latest Submitted slip represents the batch.
            ViewBag.PendingApprovals = submitted
                .GroupBy(p => p.ProjectId)
                .Select(g =>
                {
                    var latest = g.OrderByDescending(x => x.SubmittedAt ?? x.GeneratedDate).First();
                    var staffName = !string.IsNullOrWhiteSpace(latest.GeneratedBy)
                        ? latest.GeneratedBy
                        : latest.AssignedStaff;
                    return new PendingApprovalRow
                    {
                        ProjectId = g.Key,
                        StaffName = string.IsNullOrWhiteSpace(staffName) ? "Payroll Staff" : staffName,
                        ProjectName = string.IsNullOrWhiteSpace(latest.ProjectName) ? "—" : latest.ProjectName,
                        Date = latest.SubmittedAt ?? latest.GeneratedDate
                    };
                })
                .OrderByDescending(r => r.Date)
                .ToList();

            return View();
        }

        /// <summary>
        /// GET /Home/Privacy. Shows the privacy policy page from the footer link.
        /// </summary>
        public IActionResult Privacy()
        {
            return View();
        }

        /// <summary>
        /// GET /Home/Error. ASP.NET sends failed requests here. Shows a request id, nothing from the database.
        /// </summary>
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
