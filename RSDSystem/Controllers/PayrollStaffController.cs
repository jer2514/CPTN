using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;
using RSDSystem.Helpers;
using RSDSystem.Validation;

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



        // GET /PayrollStaff/GeneratePayroll
        public async Task<IActionResult> GeneratePayroll(int? projectId)
        {
            var staffName = HttpContext.Session.GetString("FullName") ?? CurrentStaffName;

            var projectsQuery = _db.Projects.Where(p => p.Status == "Active");

            List<Project> projects;

            if (!string.IsNullOrWhiteSpace(staffName))
            {
                var trimmedStaff = staffName.Trim();
                projects = await projectsQuery
                   .Where(p => p.AssignedPayrollStaff != null &&
                          p.AssignedPayrollStaff.Trim() == trimmedStaff)
                   .OrderBy(p => p.ProjectName)
                   .ToListAsync();
            }
            else
            {
                projects = new List<Project>();
            }

            if (!projects.Any())
            {
                projects = await projectsQuery
                   .OrderBy(p => p.ProjectName)
                   .ToListAsync();
                ViewBag.ShowingAllProjects = true;
            }

            ViewBag.PageTitle = "Generate Payroll";
            ViewBag.PreselectProjectId = projectId; // NEW
            return View(projects);
        }

        // GET /PayrollStaff/GetProjectEmployees?projectId=5
        [HttpGet]
        public async Task<IActionResult> GetProjectEmployees(int projectId)
        {
            var project = await _db.Projects.FindAsync(projectId);
            if (project == null)
                return Json(new { success = false, message = "Project not found." });

            var employees = await _db.Employees
                  .Where(e => e.ProjectId == projectId)
                  .OrderBy(e => e.LastName)
                  .ToListAsync();

            var result = employees.Select(e => new
            {
                e.EmployeeId,
                DisplayId = IdFormatter.Format(e.EmployeeCode),
                Name = e.FullName,
                e.JobClassification,
                e.DailyRate,
                e.RatePerHour,
                e.IsActive
            });

            return Json(new { success = true, projectName = project.ProjectName, employees = result });
        }

        // GET /PayrollStaff/PayrollSlip?employeeId=1&projectId=5
        public async Task<IActionResult> PayrollSlip(int employeeId, int projectId)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            var project = await _db.Projects.FindAsync(projectId);
            if (emp == null || project == null) return NotFound();

            var schedules = await _db.PayrollSchedules
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.StartingDate)
                .ToListAsync();

            var projectStart = InputRules.IsUsableDate(project.StartingDate)
                ? project.StartingDate!.Value.Date : (DateTime?)null;
            var projectEnd = InputRules.IsUsableDate(project.EstimateEndDate)
                ? project.EstimateEndDate!.Value.Date : (DateTime?)null;

            var defaultStart = projectStart ?? DateTime.Today.AddDays(-6);
            var defaultEnd = projectEnd ?? DateTime.Today;
            if (defaultEnd < defaultStart)
                defaultEnd = defaultStart;

            ViewBag.PageTitle = "Generate Payroll Slip";
            ViewBag.DisplayId = IdFormatter.Format(emp.EmployeeCode);
            ViewBag.Project = project;
            ViewBag.Schedules = schedules;
            ViewBag.ProjectStart = projectStart?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.ProjectEnd = projectEnd?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.DefaultStart = defaultStart.ToString("yyyy-MM-dd");
            ViewBag.DefaultEnd = defaultEnd.ToString("yyyy-MM-dd");
            ViewBag.DefaultDaysWorked = Math.Max(1, InputRules.CountWeekdays(defaultStart, defaultEnd));
            ViewBag.MinDate = projectStart?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.MaxDate = projectEnd?.ToString("yyyy-MM-dd") ?? "";

            return View(emp);
        }

        // POST /PayrollStaff/GeneratePayrollSlip
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePayrollSlip(int employeeId, int projectId,
            DateTime payPeriodStart, DateTime payPeriodEnd, int regularDaysWorked, decimal overtimeHours, int absentDays, decimal cashAdvance)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            if (emp == null)
                return Json(new { success = false, message = "Employee not found." });

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null)
                return Json(new { success = false, message = "Project not found." });

            var errors = new Dictionary<string, string>();

            foreach (var result in InputRules.ValidateDateRange(
                payPeriodStart, payPeriodEnd,
                "payPeriodStart", "payPeriodEnd",
                "Pay period starting date", "Pay period ending date"))
            {
                var key = result.MemberNames.FirstOrDefault() ?? "";
                if (!errors.ContainsKey(key) && !string.IsNullOrEmpty(result.ErrorMessage))
                    errors[key] = result.ErrorMessage;
            }

            if (regularDaysWorked < 1)
                errors["regularDaysWorked"] = "Regular days worked must be at least 1.";

            if (absentDays < 0)
                errors["absentDays"] = "Absent days cannot be negative.";

            if (overtimeHours < 0)
                errors["overtimeHours"] = "Overtime hours cannot be negative.";

            if (cashAdvance < 0)
                errors["cashAdvance"] = "Cash advance cannot be negative.";

            if (InputRules.IsUsableDate(payPeriodStart) && InputRules.IsUsableDate(payPeriodEnd)
                && payPeriodEnd.Date >= payPeriodStart.Date)
            {
                var periodDays = InputRules.InclusiveDays(payPeriodStart, payPeriodEnd);

                if (regularDaysWorked + absentDays > periodDays)
                    errors["regularDaysWorked"] = "Days worked plus absences cannot exceed the pay period.";

                if (overtimeHours > regularDaysWorked * 24)
                    errors["overtimeHours"] = "Overtime hours cannot exceed 24 hours per day worked.";

                if (InputRules.IsUsableDate(project.StartingDate) && payPeriodStart.Date < project.StartingDate!.Value.Date)
                    errors["payPeriodStart"] = "Pay period cannot start before the project starting date.";

                if (InputRules.IsUsableDate(project.EstimateEndDate) && payPeriodEnd.Date > project.EstimateEndDate!.Value.Date)
                    errors["payPeriodEnd"] = "Pay period cannot end after the project estimate end date.";

                if (InputRules.IsUsableDate(project.StartingDate) && payPeriodEnd.Date < project.StartingDate!.Value.Date)
                    errors["payPeriodEnd"] = "Pay period cannot end before the project starting date.";

                if (InputRules.IsUsableDate(project.EstimateEndDate) && payPeriodStart.Date > project.EstimateEndDate!.Value.Date)
                    errors["payPeriodStart"] = "Pay period cannot start after the project estimate end date.";
            }

            decimal regularPay = emp.DailyRate * regularDaysWorked;
            decimal overtimePay = overtimeHours * emp.RatePerHour;
            decimal gross = regularPay + overtimePay;

            if (cashAdvance > gross)
                errors["cashAdvance"] = "Cash advance cannot be greater than gross pay.";

            if (errors.Count > 0)
            {
                return Json(new
                {
                    success = false,
                    message = errors.Values.First(),
                    errors
                });
            }

            decimal net = gross - cashAdvance;
            if (net < 0) net = 0;

            var payroll = new Payroll
            {
                EmployeeId = employeeId,
                ProjectId = projectId,
                PayPeriodStart = payPeriodStart.Date,
                PayPeriodEnd = payPeriodEnd.Date,
                RegularDaysWorked = regularDaysWorked,
                OvertimeHours = overtimeHours,
                AbsentDays = absentDays,
                RegularPay = regularPay,
                OvertimePay = overtimePay,
                GrossPay = gross,
                CashAdvance = cashAdvance,
                NetPay = net,
                Status = PayrollStatusOptions.Draft,
                GeneratedBy = HttpContext.Session.GetString("FullName") ?? CurrentStaffName,
                GeneratedDate = DateTime.Now
            };

            _db.Payrolls.Add(payroll);
            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = $"Payroll for {emp.FullName} has been saved.",
                projectId
            });
        }

        // POST /PayrollStaff/GeneratePayrollForEmployee
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePayrollForEmployee(int employeeId, int projectId)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            if (emp == null)

            return Json(new { success = false, message = "Employee not found." });

            return Json(new { success = true, message = $"Payroll generated for {emp.FullName}." });
        }



        // GET /PayrollStaff/PendingPayroll?projectId=5
        public async Task<IActionResult> PendingPayroll(int? projectId)
        {
            var staffName = HttpContext.Session.GetString("FullName") ?? CurrentStaffName;

            var projectsQuery = _db.Projects.Where(p => p.Status == "Active");
            List<Project> projects;

            if (!string.IsNullOrWhiteSpace(staffName))
            {
                var trimmedStaff = staffName.Trim();
                projects = await projectsQuery
                                .Where(p => p.AssignedPayrollStaff != null &&
                                            p.AssignedPayrollStaff.Trim() == trimmedStaff)
                                .OrderBy(p => p.ProjectName)
                                .ToListAsync();
            }
            else
            {
                projects = new List<Project>();
            }

            if (!projects.Any())
            {
                projects = await projectsQuery.OrderBy(p => p.ProjectName).ToListAsync();
                ViewBag.ShowingAllProjects = true;
            }

            ViewBag.PageTitle = "Pending Payroll";
            ViewBag.PreselectProjectId = projectId;
            return View(projects);
        }

        // GET /PayrollStaff/GetProjectPayrolls?projectId=5
        [HttpGet]
        public async Task<IActionResult> GetProjectPayrolls(int projectId)
        {
            var project = await _db.Projects.FindAsync(projectId);
            if (project == null)
                return Json(new { success = false, message = "Project not found." });

            var payrolls = await _db.Payrolls
                                    .Include(p => p.Employee)
                                    .Where(p => p.ProjectId == projectId)
                                    .ToListAsync();

            var ordered = payrolls
                .OrderBy(p => PayrollStatusOptions.SortRank(p.Status))
                .ThenByDescending(p => p.GeneratedDate)
                .Select(p => new
                {
                    p.PayrollId,
                    DisplayId = IdFormatter.Format(p.Employee?.EmployeeCode),
                    EmployeeName = p.Employee?.FullName,
                    Job = p.Employee?.JobClassification,
                    p.Status,
                    p.NetPay
                });

            return Json(new { success = true, projectName = project.ProjectName, payrolls = ordered });
        }

        // GET /PayrollStaff/ViewPayroll/{id}
        public async Task<IActionResult> ViewPayroll(int id)
        {
            var payroll = await _db.Payrolls
                                   .Include(p => p.Employee)
                                   .Include(p => p.Project)
                                   .FirstOrDefaultAsync(p => p.PayrollId == id);

            if (payroll == null) return NotFound();

            ViewBag.PageTitle = "View Payroll";
            ViewBag.DisplayId = IdFormatter.Format(payroll.Employee?.EmployeeCode);
            return View(payroll);
        }

        // POST /PayrollStaff/SubmitPayroll/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitPayroll(int id)
        {
             var payroll = await _db.Payrolls.FindAsync(id);
             if (payroll == null)
             return Json(new { success = false, message = "Payroll record not found." });

             payroll.Status = PayrollStatusOptions.Submitted;
             await _db.SaveChangesAsync();

             return Json(new { success = true, message = "Payroll has been submitted for admin review." });
        }

        // POST /PayrollStaff/DeletePayroll/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePayroll(int id)
        {
            var payroll = await _db.Payrolls.FindAsync(id);
            if (payroll == null)
                return Json(new { success = false, message = "Payroll record not found." });

            if (payroll.Status != PayrollStatusOptions.Draft)
                return Json(new { success = false, message = "Only draft payroll records can be deleted." });

            _db.Payrolls.Remove(payroll);
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = "Draft payroll deleted." });
        }

        public IActionResult Logout()
        {
             // TODO: clear auth/session once login is implemented
             return RedirectToAction(nameof(Index));
        }
    }
}