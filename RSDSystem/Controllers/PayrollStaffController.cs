using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;
using RSDSystem.Helpers;
using RSDSystem.Services;
using RSDSystem.Validation;

namespace RSDSystem.Controllers
{
    public class PayrollStaffController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly AttendanceImportService _attendance;
        private readonly NotificationService _notifications;

        // TODO: replace with the signed-in user's FullName once auth/session is wired up
        private const string CurrentStaffName = "Patrick Bateman";

        public PayrollStaffController(PayrollDbContext db, AttendanceImportService attendance, NotificationService notifications)
        {
            _db = db;
            _attendance = attendance;
            _notifications = notifications;
        }

        // GET /PayrollStaff  → "To do task" dashboard
        public async Task<IActionResult> Index()
        {
            var staffName = StaffName();
            if (string.IsNullOrWhiteSpace(staffName))
            {
                ViewBag.PageTitle = "To do task";
                return View(new List<PayrollSchedule>());
            }

            var tasks = await _db.PayrollSchedules
                .Include(s => s.Project)
                .Where(s => s.Project != null
                    && s.Project.AssignedPayrollStaff != null
                    && s.Project.AssignedPayrollStaff.Trim() == staffName)
                .Where(s => s.Project!.Status == ProjectStatusOptions.OnGoing
                    || s.Project.Status == "Active"
                    || s.Project.Status == null
                    || s.Project.Status == "")
                .OrderBy(s => s.StartingDate)
                .ThenBy(s => s.Project!.ProjectName)
                .ToListAsync();

            ViewBag.PageTitle = "To do task";
            return View(tasks);
        }

        // POST /PayrollStaff/ToggleTask/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTask(int id)
        {
            var staffName = StaffName();
            var schedule = await _db.PayrollSchedules
                .Include(s => s.Project)
                .FirstOrDefaultAsync(s => s.PayrollScheduleId == id);

            if (schedule?.Project != null
                && (string.IsNullOrWhiteSpace(staffName)
                    || string.Equals(schedule.Project.AssignedPayrollStaff?.Trim(), staffName, StringComparison.Ordinal)))
            {
                schedule.TaskCompleted = !schedule.TaskCompleted;
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        private string StaffName() =>
            (HttpContext.Session.GetString("FullName") ?? CurrentStaffName).Trim();



        // GET /PayrollStaff/GeneratePayroll
        public async Task<IActionResult> GeneratePayroll(int? projectId)
        {
            var staffName = HttpContext.Session.GetString("FullName") ?? CurrentStaffName;

            var projectsQuery = _db.Projects.Ongoing();

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
            try
            {
                var project = await _db.Projects.FindAsync(projectId);
                if (project == null)
                    return Json(new { success = false, message = "Project not found." });

                var employees = await _db.Employees
                      .Where(e => e.ProjectId == projectId)
                      .OrderBy(e => e.LastName)
                      .ToListAsync();

                var schedules = await _db.Set<PayrollSchedule>()
                    .Where(s => s.ProjectId == projectId)
                    .OrderBy(s => s.StartingDate)
                    .ToListAsync();
                var openSchedule = PayrollPeriods.Open(schedules);

                var generatedEmployeeIds = new HashSet<int>();
                try
                {
                    generatedEmployeeIds = await GeneratedEmployeeIdsAsync(projectId, openSchedule, includeScheduleId: true);
                }
                catch
                {
                    try
                    {
                        generatedEmployeeIds = await GeneratedEmployeeIdsAsync(projectId, openSchedule, includeScheduleId: false);
                    }
                    catch
                    {
                        generatedEmployeeIds = new HashSet<int>();
                    }
                }

                var result = employees.Select(e => new
                {
                    e.EmployeeId,
                    DisplayId = EmployeeIds.Format(e.EmployeeCode),
                    Name = e.FullName,
                    e.JobClassification,
                    e.DailyRate,
                    e.RatePerHour,
                    e.IsActive,
                    AlreadyGenerated = generatedEmployeeIds.Contains(e.EmployeeId)
                });

                return Json(new
                {
                    success = true,
                    projectName = project.ProjectName,
                    hasSchedule = openSchedule != null,
                    scheduleId = openSchedule?.PayrollScheduleId,
                    scheduleLabel = openSchedule != null ? PayrollPeriods.Label(openSchedule) : null,
                    message = openSchedule == null
                        ? (schedules.Count == 0
                            ? "Ask the admin to add a payroll schedule before generating payroll."
                            : "All payroll schedules for this project are marked done. Ask the admin to add the next schedule.")
                        : null,
                    employees = result
                });
            }
            catch
            {
                return Json(new { success = false, message = "Could not load employees for this project." });
            }
        }

        private async Task<HashSet<int>> GeneratedEmployeeIdsAsync(
            int projectId, PayrollSchedule? openSchedule, bool includeScheduleId)
        {
            if (openSchedule == null)
                return new HashSet<int>();

            if (includeScheduleId)
            {
                var payrolls = await _db.Set<Payroll>()
                    .AsNoTracking()
                    .Where(p => p.ProjectId == projectId)
                    .Select(p => new { p.EmployeeId, p.PayrollScheduleId, p.PayPeriodStart, p.PayPeriodEnd })
                    .ToListAsync();
                return payrolls
                    .Where(p => PayrollPeriods.BelongsTo(
                        p.PayrollScheduleId, p.PayPeriodStart, p.PayPeriodEnd, openSchedule))
                    .Select(p => p.EmployeeId)
                    .ToHashSet();
            }

            var dated = await _db.Set<Payroll>()
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .Select(p => new { p.EmployeeId, p.PayPeriodStart, p.PayPeriodEnd })
                .ToListAsync();
            return dated
                .Where(p => PayrollPeriods.BelongsTo(
                    null, p.PayPeriodStart, p.PayPeriodEnd, openSchedule))
                .Select(p => p.EmployeeId)
                .ToHashSet();
        }

        // GET /PayrollStaff/PayrollSlip?employeeId=1&projectId=5&payrollId=9
        public async Task<IActionResult> PayrollSlip(int employeeId, int projectId, int? payrollId = null,
            int? scheduleId = null, string? returnUrl = null,
            int? daysWorked = null, int? absentDays = null, decimal? overtimeHours = null)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            var project = await _db.Projects.FindAsync(projectId);
            if (emp == null || project == null) return NotFound();

            var schedules = await _db.Set<PayrollSchedule>()
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.StartingDate)
                .ToListAsync();

            Payroll? existing = null;
            if (payrollId.HasValue && payrollId.Value > 0)
            {
                existing = await _db.Set<Payroll>().FirstOrDefaultAsync(p =>
                    p.PayrollId == payrollId.Value &&
                    p.EmployeeId == employeeId &&
                    p.ProjectId == projectId);

                if (existing == null) return NotFound();
                if (existing.Status == PayrollStatusOptions.Submitted ||
                    existing.Status == PayrollStatusOptions.Approved)
                {
                    return RedirectToAction(nameof(ViewPayroll), new { id = existing.PayrollId });
                }
            }
            else
            {
                var targetSchedule = scheduleId.HasValue
                    ? schedules.FirstOrDefault(s => s.PayrollScheduleId == scheduleId.Value)
                    : PayrollPeriods.Open(schedules);

                if (targetSchedule != null)
                {
                    var matches = await _db.Set<Payroll>()
                        .Where(p => p.EmployeeId == employeeId && p.ProjectId == projectId)
                        .OrderByDescending(p => p.GeneratedDate)
                        .ToListAsync();
                    existing = matches.FirstOrDefault(p => PayrollPeriods.BelongsTo(p, targetSchedule));
                }

                if (existing != null)
                {
                    if (existing.Status == PayrollStatusOptions.Submitted ||
                        existing.Status == PayrollStatusOptions.Approved)
                    {
                        return RedirectToAction(nameof(ViewPayroll), new { id = existing.PayrollId });
                    }

                    return RedirectToAction(nameof(PayrollSlip), new
                    {
                        employeeId,
                        projectId,
                        payrollId = existing.PayrollId,
                        returnUrl,
                        daysWorked,
                        absentDays,
                        overtimeHours
                    });
                }
            }

            var activeSchedule = existing != null
                ? PayrollPeriods.ForPayroll(schedules, existing)
                : (scheduleId.HasValue
                    ? schedules.FirstOrDefault(s => s.PayrollScheduleId == scheduleId.Value)
                    : PayrollPeriods.Open(schedules));

            DateTime defaultStart;
            DateTime defaultEnd;
            if (existing != null)
            {
                defaultStart = existing.PayPeriodStart.Date;
                defaultEnd = existing.PayPeriodEnd.Date;
            }
            else if (activeSchedule != null)
            {
                defaultStart = activeSchedule.StartingDate.Date;
                defaultEnd = activeSchedule.EndDate.Date;
            }
            else
            {
                defaultStart = DateTime.Today;
                defaultEnd = DateTime.Today;
            }

            if (defaultEnd < defaultStart)
                defaultEnd = defaultStart;

            var attendance = await _attendance.GetEmployeePeriodTotalsAsync(
                projectId, employeeId, defaultStart, defaultEnd, HttpContext.RequestAborted);

            ViewBag.PageTitle = existing != null ? "Edit Payroll Slip" : "Generate Payroll Slip";
            ViewBag.IsEdit = existing != null;
            ViewBag.PayrollId = existing?.PayrollId ?? 0;
            ViewBag.ReturnUrl = SafeReturnUrl(returnUrl)
                ?? (existing != null
                    ? Url.Action(nameof(ViewPayroll), new { id = existing.PayrollId })
                    : Url.Action(nameof(GeneratePayroll), new { projectId }));
            ViewBag.DisplayId = EmployeeIds.Format(emp.EmployeeCode);
            ViewBag.Project = project;
            ViewBag.Schedules = schedules;
            ViewBag.HasPayrollSchedule = activeSchedule != null;
            ViewBag.PayrollScheduleId = activeSchedule?.PayrollScheduleId ?? 0;
            ViewBag.DefaultStart = defaultStart.ToString("yyyy-MM-dd");
            ViewBag.DefaultEnd = defaultEnd.ToString("yyyy-MM-dd");
            ViewBag.DefaultDaysWorked = daysWorked
                ?? existing?.RegularDaysWorked
                ?? attendance?.DaysWorked
                ?? Math.Max(1, DateRules.CountWeekdays(defaultStart, defaultEnd));
            ViewBag.AbsentDays = absentDays
                ?? existing?.AbsentDays
                ?? attendance?.DaysAbsent
                ?? 0;
            ViewBag.OvertimeHours = overtimeHours
                ?? existing?.OvertimeHours
                ?? attendance?.OvertimeHours
                ?? 0;
            ViewBag.CashAdvance = existing?.CashAdvance ?? 0;
            ViewBag.MinDate = activeSchedule != null
                ? activeSchedule.StartingDate.ToString("yyyy-MM-dd")
                : "";
            ViewBag.MaxDate = activeSchedule != null
                ? activeSchedule.EndDate.ToString("yyyy-MM-dd")
                : "";
            ViewBag.AttendanceFound = attendance != null;

            return View(emp);
        }

        [HttpGet]
        public async Task<IActionResult> GetAttendanceTotals(
            int employeeId, int projectId, string? periodStart, string? periodEnd)
        {
            if (!DateTime.TryParseExact(periodStart, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var start)
                || !DateTime.TryParseExact(periodEnd, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var end))
                return Json(new { success = false, found = false, message = "Select a pay period first." });

            start = AttendanceDisplay.UsableDate(start) ?? start.Date;
            end = AttendanceDisplay.UsableDate(end) ?? end.Date;
            if (end < start)
                end = start;

            var attendance = await _attendance.GetEmployeePeriodTotalsAsync(
                projectId, employeeId, start, end, HttpContext.RequestAborted);
            if (attendance == null)
            {
                return Json(new
                {
                    success = true,
                    found = false,
                    daysWorked = DateRules.CountWeekdays(start, end),
                    daysAbsent = 0,
                    overtimeHours = 0m
                });
            }

            return Json(new
            {
                success = true,
                found = true,
                daysWorked = attendance.DaysWorked,
                daysAbsent = attendance.DaysAbsent,
                overtimeHours = attendance.OvertimeHours
            });
        }

        // POST /PayrollStaff/GeneratePayrollSlip
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePayrollSlip(int employeeId, int projectId,
            DateTime payPeriodStart, DateTime payPeriodEnd, int regularDaysWorked, decimal overtimeHours, int absentDays, decimal cashAdvance,
            int payrollId = 0)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            if (emp == null)
                return Json(new { success = false, message = "Employee not found." });

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null)
                return Json(new { success = false, message = "Project not found." });

            var errors = new Dictionary<string, string>();
            PayrollSchedule? coveringSchedule = null;

            foreach (var result in DateRules.ValidateDateRange(
                payPeriodStart, payPeriodEnd,
                "payPeriodStart", "payPeriodEnd",
                "Pay period starting date", "Pay period ending date"))
            {
                var key = result.MemberNames.FirstOrDefault() ?? "";
                if (!errors.ContainsKey(key) && !string.IsNullOrEmpty(result.ErrorMessage))
                    errors[key] = result.ErrorMessage;
            }

            if (regularDaysWorked < 0)
                errors["regularDaysWorked"] = "Regular days worked cannot be negative.";

            if (absentDays < 0)
                errors["absentDays"] = "Absent days cannot be negative.";

            if (overtimeHours < 0)
                errors["overtimeHours"] = "Overtime hours cannot be negative.";

            if (cashAdvance < 0)
                errors["cashAdvance"] = "Cash advance cannot be negative.";

            if (DateRules.IsUsableDate(payPeriodStart) && DateRules.IsUsableDate(payPeriodEnd)
                && payPeriodEnd.Date >= payPeriodStart.Date)
            {
                var periodDays = DateRules.InclusiveDays(payPeriodStart, payPeriodEnd);

                if (regularDaysWorked + absentDays > periodDays)
                    errors["regularDaysWorked"] = "Days worked plus absences cannot exceed the pay period.";

                var otCapDays = Math.Max(regularDaysWorked, 1);
                if (overtimeHours > otCapDays * 24)
                    errors["overtimeHours"] = "Overtime hours cannot exceed 24 hours per day worked.";

                var schedules = await _db.Set<PayrollSchedule>()
                    .Where(s => s.ProjectId == projectId)
                    .OrderBy(s => s.StartingDate)
                    .ToListAsync();

                if (schedules.Count == 0)
                {
                    errors["payPeriodStart"] = "A payroll schedule must be added by the admin before generating payroll.";
                }
                else
                {
                    coveringSchedule = PayrollPeriods.Covering(schedules, payPeriodStart, payPeriodEnd);
                    if (coveringSchedule == null)
                    {
                        var open = PayrollPeriods.Open(schedules);
                        var rangeLabel = open != null
                            ? PayrollPeriods.Label(open)
                            : "the payroll schedule set by the admin";
                        errors["payPeriodStart"] = $"Pay period must fall within the payroll schedule: {rangeLabel}.";
                    }
                }
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

            Payroll? payroll = null;
            if (payrollId > 0)
            {
                payroll = await _db.Set<Payroll>().FirstOrDefaultAsync(p =>
                    p.PayrollId == payrollId &&
                    p.EmployeeId == employeeId &&
                    p.ProjectId == projectId);

                if (payroll == null)
                    return Json(new { success = false, message = "Payroll record not found." });

                if (payroll.Status == PayrollStatusOptions.Submitted ||
                    payroll.Status == PayrollStatusOptions.Approved)
                {
                    return Json(new { success = false, message = "Submitted or approved payroll cannot be edited." });
                }
            }

            if (payroll != null)
            {
                payroll.PayPeriodStart = payPeriodStart.Date;
                payroll.PayPeriodEnd = payPeriodEnd.Date;
                payroll.PayrollScheduleId = coveringSchedule?.PayrollScheduleId ?? payroll.PayrollScheduleId;
                payroll.RegularDaysWorked = regularDaysWorked;
                payroll.OvertimeHours = overtimeHours;
                payroll.AbsentDays = absentDays;
                payroll.RegularPay = regularPay;
                payroll.OvertimePay = overtimePay;
                payroll.GrossPay = gross;
                payroll.CashAdvance = cashAdvance;
                payroll.NetPay = net;
                payroll.GeneratedBy = HttpContext.Session.GetString("FullName") ?? CurrentStaffName;
                payroll.GeneratedDate = DateTime.Now;
            }
            else
            {
                if (coveringSchedule == null)
                {
                    return Json(new
                    {
                        success = false,
                        message = "A payroll schedule must be added by the admin before generating payroll."
                    });
                }

                var alreadyGenerated = await _db.Set<Payroll>()
                    .Where(p => p.EmployeeId == employeeId && p.ProjectId == projectId)
                    .ToListAsync();
                if (alreadyGenerated.Any(p => PayrollPeriods.BelongsTo(p, coveringSchedule)))
                {
                    return Json(new
                    {
                        success = false,
                        message = "Payroll has already been generated for this employee in this schedule."
                    });
                }

                payroll = new Payroll
                {
                    EmployeeId = employeeId,
                    ProjectId = projectId,
                    PayrollScheduleId = coveringSchedule.PayrollScheduleId,
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
                _db.Set<Payroll>().Add(payroll);
            }

            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                message = payrollId > 0
                    ? $"Payroll for {emp.FullName} has been updated."
                    : $"Payroll for {emp.FullName} has been saved.",
                projectId,
                payrollId = payroll.PayrollId,
                isEdit = payrollId > 0,
                returnUrl = payrollId > 0
                    ? Url.Action(nameof(ViewPayroll), new { id = payroll.PayrollId })
                    : Url.Action(nameof(GeneratePayroll), new { projectId })
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

            var projectsQuery = _db.Projects.Ongoing();
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

            var payrolls = await _db.Set<Payroll>()
                .AsNoTracking()
                .Where(p => p.ProjectId == projectId)
                .Select(p => new
                {
                    p.PayrollId,
                    p.Status,
                    p.NetPay,
                    p.PayPeriodStart,
                    p.PayPeriodEnd,
                    p.GeneratedDate,
                    EmployeeCode = p.Employee != null ? p.Employee.EmployeeCode : null,
                    FirstName = p.Employee != null ? p.Employee.FirstName : null,
                    LastName = p.Employee != null ? p.Employee.LastName : null,
                    Job = p.Employee != null ? p.Employee.JobClassification : null
                })
                .ToListAsync();

            var ordered = payrolls
                .OrderBy(p => PayrollStatusOptions.SortRank(p.Status))
                .ThenByDescending(p => p.PayPeriodStart)
                .ThenByDescending(p => p.GeneratedDate)
                .Select(p => new
                {
                    p.PayrollId,
                    DisplayId = EmployeeIds.Format(p.EmployeeCode),
                    EmployeeName = string.Join(" ", new[] { p.FirstName, p.LastName }.Where(n => !string.IsNullOrWhiteSpace(n))),
                    p.Job,
                    p.Status,
                    p.NetPay,
                    Period = PayrollPeriods.Label(p.PayPeriodStart, p.PayPeriodEnd)
                });

            return Json(new { success = true, projectName = project.ProjectName, payrolls = ordered });
        }

        // GET /PayrollStaff/ViewPayroll/{id}
        public async Task<IActionResult> ViewPayroll(int id)
        {
            var payroll = await _db.Set<Payroll>()
                                   .Include(p => p.Employee)
                                   .Include(p => p.Project)
                                   .FirstOrDefaultAsync(p => p.PayrollId == id);

            if (payroll == null) return NotFound();

            ViewBag.PageTitle = "View Payroll";
            ViewBag.DisplayId = EmployeeIds.Format(payroll.Employee?.EmployeeCode);
            return View(payroll);
        }

        // POST /PayrollStaff/SubmitPayroll/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitPayroll(int id)
        {
             var payroll = await _db.Set<Payroll>()
                 .Include(p => p.Project)
                 .FirstOrDefaultAsync(p => p.PayrollId == id);
             if (payroll == null)
             return Json(new { success = false, message = "Payroll record not found." });

             if (payroll.Status == PayrollStatusOptions.Submitted ||
                 payroll.Status == PayrollStatusOptions.Approved)
             {
                 return Json(new { success = false, message = "Submitted or approved payroll cannot be changed." });
             }

             payroll.Status = PayrollStatusOptions.Submitted;
             await _db.SaveChangesAsync();

             if (payroll.Project != null)
                 await _notifications.NotifyPayrollSubmittedAsync(payroll.Project, payroll.GeneratedBy, HttpContext.RequestAborted);

             return Json(new { success = true, message = "Payroll has been submitted for admin review." });
        }

        // POST /PayrollStaff/DeletePayroll/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePayroll(int id)
        {
            var payroll = await _db.Set<Payroll>().FindAsync(id);
            if (payroll == null)
                return Json(new { success = false, message = "Payroll record not found." });

            if (payroll.Status == PayrollStatusOptions.Submitted ||
                payroll.Status == PayrollStatusOptions.Approved ||
                payroll.Status == PayrollStatusOptions.Correction)
            {
                return Json(new { success = false, message = "Submitted, approved, or correction payroll cannot be deleted." });
            }

            _db.Set<Payroll>().Remove(payroll);
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = "Payroll record deleted." });
        }

        public IActionResult Logout()
        {
             // TODO: clear auth/session once login is implemented
             return RedirectToAction(nameof(Index));
        }

        private string? SafeReturnUrl(string? returnUrl)
        {
            if (string.IsNullOrWhiteSpace(returnUrl))
                return null;
            if (!Url.IsLocalUrl(returnUrl))
                return null;
            return returnUrl;
        }
    }
}