using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;
using RSDSystem.Helpers;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    public class PayrollStaffController : Controller
    {
        private const string AttendanceRequiredMessage =
            "Import attendance for this payroll schedule before generating payroll.";
        private const string EmployeeAttendanceRequiredMessage =
            "No attendance records for this employee in this period. Import must match the employee ID.";
        private const string NotEmployedMessage =
            "This employee is not employed on this project.";

        private readonly PayrollDbContext _db;
        private readonly AttendanceImportService _attendance;
        private readonly NotificationService _notifications;
        private readonly ActivityLogService _logs;

        public PayrollStaffController(
            PayrollDbContext db,
            AttendanceImportService attendance,
            NotificationService notifications,
            ActivityLogService logs)
        {
            _db = db;
            _attendance = attendance;
            _notifications = notifications;
            _logs = logs;
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
                .Where(s => s.Project != null)
                .Where(s => s.Project!.Status == ProjectStatusOptions.OnGoing
                    || s.Project.Status == "Active"
                    || s.Project.Status == null
                    || s.Project.Status == "")
                .Where(s => !s.TaskApproved)
                .OrderBy(s => s.StartingDate)
                .ThenBy(s => s.Project!.ProjectName)
                .ToListAsync();

            tasks = tasks
                .Where(s => StaffNames.IsAssigned(s.Project?.AssignedPayrollStaff, staffName))
                .ToList();

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

            if (schedule?.Project != null && StaffNames.IsAssigned(schedule.Project.AssignedPayrollStaff, staffName))
            {
                if (!schedule.TaskApproved && !schedule.TaskCompleted)
                {
                    schedule.TaskCompleted = true;
                    await _db.SaveChangesAsync();
                    await _notifications.NotifyTaskCompletionRequestedAsync(
                        schedule, staffName, HttpContext.RequestAborted);
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private string? StaffName() => StaffNames.FromSession(HttpContext.Session);

        private async Task<List<Project>> AssignedOngoingProjectsAsync()
        {
            var staffName = StaffName();
            if (string.IsNullOrWhiteSpace(staffName))
                return new List<Project>();

            var projects = await _db.Projects.Ongoing()
                .OrderBy(p => p.ProjectName)
                .ToListAsync();
            return projects
                .Where(p => StaffNames.IsAssigned(p.AssignedPayrollStaff, staffName))
                .ToList();
        }



        // GET /PayrollStaff/GeneratePayroll
        public async Task<IActionResult> GeneratePayroll(int? projectId)
        {
            var projects = await AssignedOngoingProjectsAsync();

            ViewBag.PageTitle = "Generate Payroll";
            ViewBag.PreselectProjectId = projectId;
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
                      .Where(e => e.ProjectId == projectId && e.IsActive)
                      .OrderBy(e => e.LastName)
                      .ToListAsync();

                var pendingCorrectionIds = await PendingCorrectionEmployeeIdsAsync(projectId);
                var attendanceEmployeeIds = new HashSet<int>();

                var schedules = await _db.Set<PayrollSchedule>()
                    .Where(s => s.ProjectId == projectId)
                    .OrderBy(s => s.StartingDate)
                    .ToListAsync();
                var openSchedule = PayrollPeriods.Open(schedules);
                var hasAttendance = openSchedule != null
                    && await _attendance.HasImportedAttendanceAsync(
                        projectId, openSchedule.StartingDate, openSchedule.EndDate, HttpContext.RequestAborted);
                if (openSchedule != null && hasAttendance)
                {
                    attendanceEmployeeIds = await _attendance.EmployeeIdsWithAttendanceAsync(
                        projectId, openSchedule.StartingDate, openSchedule.EndDate, HttpContext.RequestAborted);
                }

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
                    RatePerHour = EmployeeRates.HourlyFromDaily(e.DailyRate),
                    e.IsActive,
                    HasAttendance = attendanceEmployeeIds.Contains(e.EmployeeId),
                    AlreadyGenerated = generatedEmployeeIds.Contains(e.EmployeeId),
                    PendingCorrection = pendingCorrectionIds.Contains(e.EmployeeId)
                });

                string? message = null;
                if (openSchedule == null)
                {
                    message = schedules.Count == 0
                        ? "Ask the admin to add a payroll schedule before generating payroll."
                        : "All payroll schedules for this project are marked done. Ask the admin to add the next schedule.";
                }
                else if (!hasAttendance)
                {
                    message = AttendanceRequiredMessage;
                }
                else if (pendingCorrectionIds.Count > 0)
                {
                    message = "Employees with a pending attendance correction cannot generate payroll until admin approves the request.";
                }

                return Json(new
                {
                    success = true,
                    projectName = project.ProjectName,
                    hasSchedule = openSchedule != null,
                    hasAttendance,
                    scheduleId = openSchedule?.PayrollScheduleId,
                    scheduleLabel = openSchedule != null ? PayrollPeriods.Label(openSchedule) : null,
                    message,
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

            if (ProjectStatusOptions.IsFinished(project.Status) && payrollId.GetValueOrDefault() <= 0)
            {
                TempData["Error"] = "Finished projects cannot generate payroll or payslips.";
                return RedirectToAction(nameof(GeneratePayroll), new { projectId });
            }

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

            if (!emp.IsActive || emp.ProjectId != projectId)
            {
                TempData["Error"] = NotEmployedMessage;
                return RedirectToAction(nameof(GeneratePayroll), new { projectId });
            }

            if (existing == null)
            {
                if (await HasPendingCorrectionAsync(projectId, employeeId))
                {
                    TempData["Error"] = "This employee has a pending attendance correction. Wait for admin approval before generating payroll.";
                    return RedirectToAction(nameof(GeneratePayroll), new { projectId });
                }
            }

            var attendance = await _attendance.GetEmployeePeriodTotalsAsync(
                projectId, employeeId, defaultStart, defaultEnd, HttpContext.RequestAborted);
            if (attendance == null)
            {
                TempData["Error"] = EmployeeAttendanceRequiredMessage;
                return RedirectToAction(nameof(GeneratePayroll), new { projectId });
            }

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
            ViewBag.DefaultDaysWorked = attendance.DaysPresent > 0
                ? attendance.DaysPresent
                : attendance.DaysWorked;
            ViewBag.AbsentDays = attendance.DaysAbsent;
            ViewBag.OvertimeHours = attendance.OvertimeHours;
            ViewBag.RegularHours = attendance.RegularHours;
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
                    daysWorked = 0,
                    daysAbsent = 0,
                    overtimeHours = 0m,
                    regularHours = 0m
                });
            }

            return Json(new
            {
                success = true,
                found = true,
                daysWorked = attendance.DaysPresent > 0 ? attendance.DaysPresent : attendance.DaysWorked,
                daysAbsent = attendance.DaysAbsent,
                overtimeHours = attendance.OvertimeHours,
                regularHours = attendance.RegularHours
            });
        }

        // POST /PayrollStaff/GeneratePayrollSlip
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> GeneratePayrollSlip(int employeeId, int projectId,
            DateTime payPeriodStart, DateTime payPeriodEnd, decimal cashAdvance,
            int payrollId = 0)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            if (emp == null)
                return Json(new { success = false, message = "Employee not found." });

            var project = await _db.Projects.FindAsync(projectId);
            if (project == null)
                return Json(new { success = false, message = "Project not found." });

            if (ProjectStatusOptions.IsFinished(project.Status))
                return Json(new { success = false, message = "Finished projects cannot generate payroll or payslips." });

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
            if (!emp.IsActive || emp.ProjectId != projectId)
                return Json(new { success = false, message = NotEmployedMessage });

            if (payrollId <= 0 && await HasPendingCorrectionAsync(projectId, employeeId))
            {
                return Json(new
                {
                    success = false,
                    message = "This employee has a pending attendance correction. Wait for admin approval before generating payroll."
                });
            }

            var schedules = await _db.Set<PayrollSchedule>()
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.StartingDate)
                .ToListAsync();
            if (schedules.Count == 0)
            {
                return Json(new
                {
                    success = false,
                    message = "A payroll schedule must be added by the admin before generating payroll.",
                    errors = new Dictionary<string, string>
                    {
                        ["payPeriodStart"] = "A payroll schedule must be added by the admin before generating payroll."
                    }
                });
            }

            var coveringSchedule = payroll != null
                ? PayrollPeriods.ForPayroll(schedules, payroll) ?? PayrollPeriods.Covering(schedules, payroll.PayPeriodStart, payroll.PayPeriodEnd)
                : PayrollPeriods.Covering(schedules, payPeriodStart, payPeriodEnd) ?? PayrollPeriods.Open(schedules);

            if (coveringSchedule == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Pay period must fall within the payroll schedule set by the admin.",
                    errors = new Dictionary<string, string>
                    {
                        ["payPeriodStart"] = "Pay period must fall within the payroll schedule set by the admin."
                    }
                });
            }

            var periodStart = coveringSchedule.StartingDate.Date;
            var periodEnd = coveringSchedule.EndDate.Date;

            var attendance = await _attendance.GetEmployeePeriodTotalsAsync(
                projectId, employeeId, periodStart, periodEnd, HttpContext.RequestAborted);
            if (attendance == null)
                return Json(new { success = false, message = EmployeeAttendanceRequiredMessage });

            var regularDaysWorked = attendance.DaysPresent > 0
                ? attendance.DaysPresent
                : attendance.DaysWorked;
            var absentDays = attendance.DaysAbsent;
            var overtimeHours = attendance.OvertimeHours;
            var regularHours = attendance.RegularHours;

            var errors = new Dictionary<string, string>();
            if (cashAdvance < 0)
                errors["cashAdvance"] = "Cash advance cannot be negative.";

            var (regularPay, overtimePay, gross, net) = PayrollComputation.Compute(
                EmployeeRates.HourlyFromDaily(emp.DailyRate), regularHours, overtimeHours, cashAdvance);

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

            if (payroll != null)
            {
                payroll.PayPeriodStart = periodStart;
                payroll.PayPeriodEnd = periodEnd;
                payroll.PayrollScheduleId = coveringSchedule.PayrollScheduleId;
                payroll.RegularDaysWorked = regularDaysWorked;
                payroll.RegularHours = regularHours;
                payroll.OvertimeHours = overtimeHours;
                payroll.AbsentDays = absentDays;
                payroll.RegularPay = regularPay;
                payroll.OvertimePay = overtimePay;
                payroll.GrossPay = gross;
                payroll.CashAdvance = cashAdvance;
                payroll.NetPay = net;
                payroll.GeneratedBy = StaffName() ?? "Staff";
                payroll.GeneratedDate = PhilippinesTime.Now;
            }
            else
            {
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
                    PayPeriodStart = periodStart,
                    PayPeriodEnd = periodEnd,
                    RegularDaysWorked = regularDaysWorked,
                    RegularHours = regularHours,
                    OvertimeHours = overtimeHours,
                    AbsentDays = absentDays,
                    RegularPay = regularPay,
                    OvertimePay = overtimePay,
                    GrossPay = gross,
                    CashAdvance = cashAdvance,
                    NetPay = net,
                    Status = PayrollStatusOptions.Draft,
                    GeneratedBy = StaffName() ?? "Staff",
                    GeneratedDate = PhilippinesTime.Now
                };
                _db.Set<Payroll>().Add(payroll);
            }

            await _db.SaveChangesAsync();
            await _logs.LogAsync(
                ActivityTypes.GeneratePayroll,
                ActivityModules.Payroll,
                $"Generated payroll for {emp.FullName} on {project?.ProjectName ?? "the project"} ({PayrollPeriods.Label(periodStart, periodEnd)}).",
                payroll.ProjectId,
                payroll.PayrollId);

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
            var projects = await AssignedOngoingProjectsAsync();

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
                    LastName = p.LastName ?? "",
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

        // GET /PayrollStaff/DownloadPayslips
        public async Task<IActionResult> DownloadPayslips(int projectId, DateTime start, DateTime end)
        {
            var staffName = StaffName();
            var project = await _db.Projects.FindAsync(projectId);
            if (project == null) return NotFound();

            if (!StaffNames.IsAssigned(project.AssignedPayrollStaff, staffName))
            {
                TempData["Error"] = "You can only download payslips for projects assigned to you.";
                return RedirectToAction(nameof(PendingPayroll), new { projectId });
            }

            var slips = (await _db.Set<Payroll>()
                .Include(p => p.Employee)
                .Include(p => p.Project)
                .Where(p => p.ProjectId == projectId
                    && p.PayPeriodStart.Date == start.Date
                    && p.PayPeriodEnd.Date == end.Date
                    && p.Status == PayrollStatusOptions.Approved)
                .ToListAsync())
                .OrderBy(p => p.Employee?.LastName)
                .ThenBy(p => p.Employee?.FirstName)
                .ToList();

            if (slips.Count == 0)
            {
                TempData["Error"] = "Payslips are available after admin approval.";
                return RedirectToAction(nameof(PendingPayroll), new { projectId });
            }

            var dateCulture = System.Globalization.CultureInfo.InvariantCulture;
            var safe = new string((project.ProjectName ?? "Project").Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(safe)) safe = "Project";

            ViewBag.ProjectName = project.ProjectName;
            ViewBag.FileName = safe + "_Payslip_"
                + start.ToString("MMMdd", dateCulture)
                + "-"
                + end.ToString("MMMdd", dateCulture)
                + ".pdf";
            ViewBag.StartLabel = start.ToString("MMMM dd, yyyy", dateCulture);
            ViewBag.EndLabel = end.ToString("MMMM dd, yyyy", dateCulture);
            return View("~/Views/payroll/PrintPayslips.cshtml", slips);
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

             var blocked = PayrollSubmitRules.BlockReason(
                 payroll.Status,
                 await HasPendingCorrectionAsync(payroll.ProjectId, payroll.EmployeeId));
             if (blocked != null)
                 return Json(new { success = false, message = blocked });

             var resubmitted = payroll.Status == PayrollStatusOptions.Correction;
             payroll.Status = PayrollStatusOptions.Submitted;
             payroll.SubmittedAt = PhilippinesTime.Now;
             await _db.SaveChangesAsync();

             if (payroll.Project != null)
                 await _notifications.NotifyPayrollSubmittedAsync(
                     payroll.Project, payroll.GeneratedBy, resubmitted, HttpContext.RequestAborted);

             await _logs.LogAsync(
                 ActivityTypes.SubmitPayroll,
                 ActivityModules.Payroll,
                 $"Submitted payroll for {payroll.Project?.ProjectName ?? "the project"} ({PayrollPeriods.Label(payroll.PayPeriodStart, payroll.PayPeriodEnd)}).",
                 payroll.ProjectId,
                 payroll.PayrollId);

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

        private async Task<HashSet<int>> PendingCorrectionEmployeeIdsAsync(int projectId)
        {
            var ids = await _db.AttendanceCorrectionRequests.AsNoTracking()
                .Where(c => c.ProjectId == projectId
                    && c.Status == CorrectionRequestStatuses.Pending
                    && c.EmployeeId != null)
                .Select(c => c.EmployeeId!.Value)
                .Distinct()
                .ToListAsync();
            return ids.ToHashSet();
        }

        private async Task<bool> HasPendingCorrectionAsync(int projectId, int employeeId) =>
            await _db.AttendanceCorrectionRequests.AsNoTracking()
                .AnyAsync(c => c.ProjectId == projectId
                    && c.EmployeeId == employeeId
                    && c.Status == CorrectionRequestStatuses.Pending);
    }
}