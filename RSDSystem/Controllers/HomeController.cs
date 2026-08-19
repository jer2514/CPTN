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
                    p.PayrollScheduleId,
                    p.PayPeriodStart,
                    p.PayPeriodEnd,
                    p.GeneratedBy,
                    p.GeneratedDate,
                    ProjectName = p.Project != null ? p.Project.ProjectName ?? "" : "",
                    AssignedStaff = p.Project != null ? p.Project.AssignedPayrollStaff ?? "" : ""
                })
                .ToListAsync();

            ViewBag.PendingApprovals = submitted
                .GroupBy(p => new
                {
                    p.ProjectId,
                    ScheduleId = p.PayrollScheduleId ?? 0,
                    Start = p.PayPeriodStart.Date,
                    End = p.PayPeriodEnd.Date
                })
                .Select(g =>
                {
                    var latest = g.OrderByDescending(x => x.GeneratedDate).First();
                    var staffName = !string.IsNullOrWhiteSpace(latest.GeneratedBy)
                        ? latest.GeneratedBy
                        : latest.AssignedStaff;
                    return new PendingApprovalRow
                    {
                        ProjectId = g.Key.ProjectId,
                        PayrollScheduleId = g.Key.ScheduleId > 0 ? g.Key.ScheduleId : null,
                        StaffName = string.IsNullOrWhiteSpace(staffName) ? "Payroll Staff" : staffName,
                        ProjectName = string.IsNullOrWhiteSpace(latest.ProjectName) ? "—" : latest.ProjectName,
                        Date = g.Key.Start,
                        PeriodStart = g.Key.Start,
                        PeriodEnd = g.Key.End
                    };
                })
                .OrderByDescending(r => r.PeriodStart)
                .ThenByDescending(r => r.PeriodEnd)
                .ToList();

            ViewBag.PendingCorrections = await _db.AttendanceCorrectionRequests
                .AsNoTracking()
                .Where(c => c.Status == CorrectionRequestStatuses.Pending)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new PendingCorrectionRow
                {
                    CorrectionId = c.AttendanceCorrectionRequestId,
                    StaffName = c.PayrollStaffName,
                    ProjectName = c.Project != null ? c.Project.ProjectName ?? "—" : "—",
                    EmployeeName = c.EmployeeName,
                    WorkDate = c.WorkDate
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
