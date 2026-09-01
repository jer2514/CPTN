using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    public class AttendanceController : Controller
    {
        private const string FinishedAttendanceMessage =
            "Finished projects cannot import or change attendance records.";
        private static readonly string[] AllowedExtensions = { ".xls", ".xlsx", ".csv", ".txt" };

        private readonly PayrollDbContext _db;
        private readonly AttendanceImportService _imports;
        private readonly NotificationService _notifications;
        private readonly ActivityLogService _logs;

        public AttendanceController(
            PayrollDbContext db,
            AttendanceImportService imports,
            NotificationService notifications,
            ActivityLogService logs)
        {
            _db = db;
            _imports = imports;
            _notifications = notifications;
            _logs = logs;
        }

        public async Task<IActionResult> Import()
        {
            if (IsAdmin)
                return RedirectToAction(nameof(Records));

            ViewBag.PageTitle = "Import Attendance";
            return View(await LoadProjectsAsync(includeFinished: false));
        }

        public async Task<IActionResult> Records()
        {
            ViewBag.PageTitle = "Attendance Records";
            ViewBag.IsAdmin = IsAdmin;
            return View(await LoadProjectsAsync(includeFinished: true));
        }

        public async Task<IActionResult> Summary()
        {
            if (!IsAdmin)
                return RedirectToAction(nameof(Records));

            ViewBag.PageTitle = "Attendance Summary";
            ViewBag.IsAdmin = true;
            return View(await LoadProjectsAsync(includeFinished: true));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> Preview(int? projectId, string? projectName, IFormFile? file)
        {
            var check = ValidateUpload(file);
            if (check != null)
                return Json(new { success = false, message = check });

            await using var stream = file!.OpenReadStream();
            var preview = await _imports.PreviewAsync(
                projectId, projectName, stream, file.FileName, StaffScope(), HttpContext.RequestAborted);

            if (preview.Error != null)
                return Json(new { success = false, message = preview.Error });

            return Json(new
            {
                success = true,
                projectId = preview.Project?.ProjectId,
                projectName = preview.Project?.ProjectName,
                fileName = preview.FileName,
                format = preview.Format,
                periodStart = AttendanceDisplay.LongDate(preview.PeriodStart),
                periodEnd = AttendanceDisplay.LongDate(preview.PeriodEnd),
                matchedCount = preview.MatchedCount,
                unmatchedCount = preview.UnmatchedCount,
                // Candidate employees for the "Select employee" dropdown on unmatched rows.
                candidates = preview.CandidateEmployees
                    .OrderBy(e => e.FirstName)
                    .ThenBy(e => e.LastName)
                    .Select(e => new
                    {
                        id = e.EmployeeId,
                        name = e.FullName,
                        code = AttendanceDisplay.EmployeeId(e.EmployeeCode)
                    }),
                rows = preview.Rows.Select(r => ToRowJson(r))
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> ImportFile(
            int? projectId, string? projectName, IFormFile? file, string? overridesJson, string? manualMatchesJson)
        {
            try
            {
                var check = ValidateUpload(file);
                if (check != null)
                    return Json(new { success = false, message = check });

                await using var stream = file!.OpenReadStream();
                var result = await _imports.ImportAsync(
                    projectId,
                    projectName,
                    stream,
                    file.FileName,
                    ImportedBy(),
                    AttendanceImportSources.Manual,
                    StaffScope(),
                    overridesJson,
                    manualMatchesJson,
                    HttpContext.RequestAborted);

                if (result.Error != null)
                    return Json(new { success = false, message = result.Error });

                var importedProject = await _db.Projects.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.ProjectId == result.ProjectId, HttpContext.RequestAborted);
                if (importedProject != null)
                    await _notifications.NotifyAttendanceImportedAsync(importedProject, ImportedBy(), HttpContext.RequestAborted);

                await _logs.LogAsync(
                    ActivityTypes.ImportAttendance,
                    ActivityModules.Attendance,
                    $"Imported {result.RowCount} attendance row(s) for {result.ProjectName}.",
                    result.ProjectId,
                    result.ImportId);

                var message = result.ReplacedPrevious
                    ? $"Replaced previous attendance for these dates. Imported {result.RowCount} row(s) for {result.ProjectName}."
                    : $"Imported {result.RowCount} row(s) for {result.ProjectName}.";
                if (result.SkippedLockedCount > 0)
                    message += $" Skipped {result.SkippedLockedCount} row(s) because payroll is already approved.";

                return Json(new
                {
                    success = true,
                    message,
                    result.ImportId,
                    result.ProjectId,
                    result.RowCount,
                    result.MatchedCount,
                    result.UnmatchedCount
                });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    success = false,
                    message = "Could not import the attendance file. " + ex.GetBaseException().Message
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPeriods(int projectId)
        {
            var (project, blocked) = await RequireProjectAsync(projectId);
            if (blocked != null)
                return blocked;

            var periods = await _imports.ListPeriodsAsync(projectId, HttpContext.RequestAborted);
            return Json(new
            {
                success = true,
                projectName = project!.ProjectName,
                isFinished = ProjectStatusOptions.IsFinished(project.Status),
                status = ProjectStatusOptions.Normalize(project.Status),
                periods = periods.Select(p => new
                {
                    key = p.Key,
                    start = p.Start.ToString("yyyy-MM-dd"),
                    end = p.End.ToString("yyyy-MM-dd"),
                    label = p.Label,
                    importedBy = p.ImportedBy
                })
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetRecords(
            int projectId, string? search, string? status, int page = 1,
            string? periodStart = null, string? periodEnd = null)
        {
            var (project, blocked) = await RequireProjectAsync(projectId);
            if (blocked != null)
                return blocked;

            var start = ParseIsoDate(periodStart);
            var end = ParseIsoDate(periodEnd);
            const int pageSize = 10;
            var (rows, total) = await _imports.QueryRecordsAsync(
                projectId, search, status, page, pageSize, start, end, HttpContext.RequestAborted);

            var importedBy = rows.Select(r => r.Import?.ImportedBy)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

            var recordIds = rows.Select(r => r.AttendanceRecordId).ToList();
            var pendingIds = new HashSet<int>();
            var approvedIds = new HashSet<int>();
            var closedPayrolls = await PayrollAttendanceLock.LoadClosedAsync(
                _db, projectId, HttpContext.RequestAborted);
            if (recordIds.Count > 0)
            {
                var requests = await _db.AttendanceCorrectionRequests.AsNoTracking()
                    .Where(c => recordIds.Contains(c.AttendanceRecordId)
                        && (c.Status == CorrectionRequestStatuses.Pending
                            || c.Status == CorrectionRequestStatuses.Approved))
                    .Select(c => new { c.AttendanceRecordId, c.Status })
                    .ToListAsync(HttpContext.RequestAborted);
                pendingIds = requests
                    .Where(c => c.Status == CorrectionRequestStatuses.Pending)
                    .Select(c => c.AttendanceRecordId)
                    .ToHashSet();
                approvedIds = requests
                    .Where(c => c.Status == CorrectionRequestStatuses.Approved)
                    .Select(c => c.AttendanceRecordId)
                    .ToHashSet();
            }

            return Json(new
            {
                success = true,
                projectName = project!.ProjectName,
                isFinished = ProjectStatusOptions.IsFinished(project.Status),
                status = ProjectStatusOptions.Normalize(project.Status),
                periodLabel = start.HasValue && end.HasValue
                    ? AttendanceDisplay.LongDate(start) + " - " + AttendanceDisplay.LongDate(end)
                    : null,
                importedBy,
                total,
                page,
                pageSize,
                totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize)),
                rows = rows.Select(r => ToRowJson(new AttendancePreviewRow
                {
                    EmployeeId = r.EmployeeId,
                    DisplayId = AttendanceDisplay.EmployeeId(
                        r.Employee?.EmployeeCode ?? r.ExternalUserId),
                    ExternalUserId = r.ExternalUserId,
                    EmployeeName = r.Employee?.FullName ?? r.EmployeeName,
                    MatchedEmployeeName = r.Employee?.FullName,
                    WorkDate = r.WorkDate,
                    TimeIn1 = r.TimeIn1,
                    TimeOut1 = r.TimeOut1,
                    TimeIn2 = r.TimeIn2,
                    TimeOut2 = r.TimeOut2,
                    OvertimeIn = r.OvertimeIn,
                    OvertimeOut = r.OvertimeOut,
                    WorkHoursNormal = r.WorkHoursNormal,
                    WorkHoursActual = r.WorkHoursActual,
                    LateMinutes = r.LateMinutes,
                    EarlyMinutes = r.EarlyMinutes,
                    OvertimeHours = r.OvertimeHours,
                    AbsenceDays = r.AbsenceDays,
                    Status = r.Status,
                    Matched = r.Matched,
                    Note = r.Matched ? null : "No matching employee on this project."
                }, r.AttendanceRecordId, r.Import?.Format ?? AttendanceFormats.Daily, r.Import?.ImportedAt,
                    pendingIds.Contains(r.AttendanceRecordId),
                    approvedIds.Contains(r.AttendanceRecordId),
                    PayrollAttendanceLock.IsLocked(closedPayrolls, r.EmployeeId, r.WorkDate)))
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetSummary(
            int projectId, string? search, string? status, int page = 1,
            string? periodStart = null, string? periodEnd = null)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Attendance summary is available to admin only." });

            var (project, blocked) = await RequireProjectAsync(projectId);
            if (blocked != null)
                return blocked;

            var start = ParseIsoDate(periodStart);
            var end = ParseIsoDate(periodEnd);
            if (!start.HasValue || !end.HasValue)
                return Json(new { success = false, message = "Select a period first." });

            const int pageSize = 10;
            var summary = await _imports.QuerySummaryAsync(
                projectId, start, end, search, status, page, pageSize, HttpContext.RequestAborted);

            return Json(new
            {
                success = true,
                projectName = project!.ProjectName,
                isFinished = ProjectStatusOptions.IsFinished(project.Status),
                status = ProjectStatusOptions.Normalize(project.Status),
                periodLabel = AttendanceDisplay.LongDate(start) + " - " + AttendanceDisplay.LongDate(end),
                importedBy = summary.ImportedBy,
                total = summary.Total,
                page,
                pageSize,
                totalPages = Math.Max(1, (int)Math.Ceiling(summary.Total / (double)pageSize)),
                totals = new
                {
                    employees = summary.Total,
                    daysWorked = summary.DaysWorked,
                    daysAbsent = summary.DaysAbsent,
                    daysLate = summary.DaysLate,
                    daysIncomplete = summary.DaysIncomplete,
                    issueDays = summary.IssueDays,
                    regularHours = summary.RegularHours,
                    overtimeHours = summary.OvertimeHours,
                    unmatched = summary.UnmatchedCount
                },
                rows = summary.Rows.Select(r => new
                {
                    r.EmployeeId,
                    r.DisplayId,
                    r.EmployeeName,
                    r.Matched,
                    r.DaysWorked,
                    r.DaysAbsent,
                    r.DaysLate,
                    r.DaysIncomplete,
                    r.IssueDays,
                    regularHours = r.RegularHours.ToString("0.00"),
                    overtimeHours = r.OvertimeHours.ToString("0.00")
                })
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePeriod(int projectId, string? periodStart, string? periodEnd)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Only admin can delete imported attendance." });

            var (_, blocked) = await RequireOngoingProjectAsync(projectId);
            if (blocked != null)
                return blocked;

            var start = ParseIsoDate(periodStart);
            var end = ParseIsoDate(periodEnd);
            var (deleted, error) = await _imports.DeletePeriodAsync(
                projectId, start, end, HttpContext.RequestAborted);
            if (error != null)
                return Json(new { success = false, message = error });

            return Json(new
            {
                success = true,
                message = $"Deleted {deleted} attendance row(s) for this period. Payroll staff can import the file again if needed."
            });
        }

        public IActionResult Edit(int id)
        {
            return RedirectToAction(nameof(Records));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(AttendanceMonthEdit model)
        {
            return RedirectToAction(nameof(Records));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult UpdateRecord(
            int recordId,
            string? timeIn1,
            string? timeOut1,
            string? timeIn2,
            string? timeOut2,
            string? overtimeIn,
            string? overtimeOut,
            string? status)
        {
            return Json(new
            {
                success = false,
                message = "Attendance changes must be submitted as a correction request."
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestCorrection(
            int recordId,
            string? timeIn1,
            string? timeOut1,
            string? timeIn2,
            string? timeOut2,
            string? overtimeIn,
            string? overtimeOut,
            string? reason)
        {
            if (IsAdmin)
            {
                return Json(new
                {
                    success = false,
                    message = "Attendance records are view-only for admin. Approve staff correction requests from Notifications."
                });
            }

            var note = (reason ?? "").Trim();
            if (string.IsNullOrWhiteSpace(note))
                return Json(new { success = false, message = "Enter a reason for this correction request." });

            var record = await _db.AttendanceRecords
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.AttendanceRecordId == recordId);
            if (record == null)
                return Json(new { success = false, message = "Attendance row not found." });

            var (_, blocked) = await RequireOngoingProjectAsync(record.ProjectId);
            if (blocked != null)
                return blocked;

            if (await PayrollAttendanceLock.IsLockedAsync(
                    _db, record.ProjectId, record.EmployeeId, record.WorkDate, HttpContext.RequestAborted))
            {
                return Json(new
                {
                    success = false,
                    message = "Payroll for this employee is already approved. Attendance cannot be edited."
                });
            }

            var alreadyApproved = await _db.AttendanceCorrectionRequests
                .AnyAsync(c => c.AttendanceRecordId == recordId
                    && c.Status == CorrectionRequestStatuses.Approved);
            if (alreadyApproved)
                return Json(new { success = false, message = "This attendance row was already approved and cannot be edited again." });

            var pending = await _db.AttendanceCorrectionRequests
                .Where(c => c.AttendanceRecordId == recordId
                    && (c.Status == CorrectionRequestStatuses.Pending
                        || c.Status == CorrectionRequestStatuses.Returned))
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();
            var resubmitted = pending != null && pending.Status == CorrectionRequestStatuses.Returned;
            if (pending == null)
            {
                pending = new AttendanceCorrectionRequest
                {
                    AttendanceRecordId = record.AttendanceRecordId,
                    ProjectId = record.ProjectId,
                    EmployeeId = record.EmployeeId,
                    CreatedAt = PhilippinesTime.Now
                };
                _db.AttendanceCorrectionRequests.Add(pending);
            }

            pending.EmployeeName = record.Employee?.FullName ?? record.EmployeeName;
            pending.PayrollStaffName = ImportedBy();
            pending.WorkDate = record.WorkDate;
            pending.TimeIn1 = timeIn1;
            pending.TimeOut1 = timeOut1;
            pending.TimeIn2 = timeIn2;
            pending.TimeOut2 = timeOut2;
            pending.OvertimeIn = overtimeIn;
            pending.OvertimeOut = overtimeOut;
            pending.Reason = note;
            pending.Status = CorrectionRequestStatuses.Pending;
            pending.ReturnReason = null;
            pending.ReviewedAt = null;
            await _db.SaveChangesAsync();

            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == record.ProjectId);
            await _notifications.NotifyAttendanceCorrectionRequestedAsync(
                pending.PayrollStaffName,
                pending.EmployeeName,
                project?.ProjectName,
                pending.WorkDate,
                record.ProjectId,
                pending.AttendanceCorrectionRequestId,
                resubmitted,
                HttpContext.RequestAborted);

            await _logs.LogAsync(
                ActivityTypes.RequestCorrection,
                ActivityModules.Attendance,
                $"Requested an attendance correction for {pending.EmployeeName} on {AttendanceDisplay.LongDate(pending.WorkDate)}.",
                record.ProjectId,
                pending.AttendanceCorrectionRequestId);

            return Json(new
            {
                success = true,
                message = resubmitted
                    ? "Correction request resubmitted to admin."
                    : "Correction request sent to admin."
            });
        }

        private async Task<List<Project>> LoadProjectsAsync(bool includeFinished)
        {
            IQueryable<Project> query = _db.Projects.AsNoTracking();
            query = includeFinished ? query.ForAttendanceView() : query.Ongoing();
            var role = HttpContext.Session.GetString("Role");
            if (role != "PayrollStaff")
                return await query.OrderBy(p => p.ProjectName).ToListAsync();

            var staffName = StaffNames.FromSession(HttpContext.Session);
            if (string.IsNullOrWhiteSpace(staffName))
                return new List<Project>();

            var key = staffName.ToLower();
            return await query
                .Where(p => p.AssignedPayrollStaff != null && p.AssignedPayrollStaff.Trim().ToLower() == key)
                .OrderBy(p => p.ProjectName)
                .ToListAsync();
        }

        private async Task<(Project? Project, IActionResult? Error)> RequireProjectAsync(int projectId)
        {
            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == projectId);
            if (project == null)
                return (null, Json(new { success = false, message = "Project not found." }));
            return (project, null);
        }

        private async Task<(Project? Project, IActionResult? Error)> RequireOngoingProjectAsync(int projectId)
        {
            var (project, missing) = await RequireProjectAsync(projectId);
            if (missing != null)
                return (null, missing);
            if (ProjectStatusOptions.IsFinished(project!.Status))
                return (null, Json(new { success = false, message = FinishedAttendanceMessage }));
            return (project, null);
        }

        private string? StaffScope()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "PayrollStaff")
                return null;

            return StaffNames.FromSession(HttpContext.Session);
        }

        private string ImportedBy() =>
            HttpContext.Session.GetString("FullName") ?? "Staff";

        private bool IsAdmin =>
            string.Equals(HttpContext.Session.GetString("Role"), "Admin", StringComparison.OrdinalIgnoreCase);

        private static DateTime? ParseIsoDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var date))
                return AttendanceDisplay.UsableDate(date);
            return AttendanceDisplay.UsableDate(DateTime.TryParse(value, out var parsed) ? parsed : null);
        }

        private static string? ValidateUpload(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return "Choose an attendance Excel or CSV file first.";

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return "Use an .xls, .xlsx, or .csv attendance file.";

            return null;
        }

        private static object ToRowJson(
            AttendancePreviewRow row,
            int? recordId = null,
            string? format = null,
            DateTime? importedAt = null,
            bool pendingCorrection = false,
            bool correctionApproved = false,
            bool payrollLocked = false)
        {
            var computed = new AttendanceRecord
            {
                TimeIn1 = row.TimeIn1,
                TimeOut1 = row.TimeOut1,
                TimeIn2 = row.TimeIn2,
                TimeOut2 = row.TimeOut2,
                OvertimeIn = row.OvertimeIn,
                OvertimeOut = row.OvertimeOut,
                WorkHoursActual = row.WorkHoursActual
            };
            AttendanceRules.Apply(computed);
            var issues = AttendanceRules.DetectIssues(computed);
            var status = AttendanceStatuses.Display(computed.Status);
            var locked = payrollLocked || correctionApproved;
            string actionLabel;
            if (payrollLocked || correctionApproved)
                actionLabel = "Approved";
            else if (pendingCorrection)
                actionLabel = "Pending Review";
            else
                actionLabel = "Request Edit";

            return new
            {
                recordId,
                row.EmployeeId,
                row.DisplayId,
                row.ExternalUserId,
                row.EmployeeName,
                matchedEmployeeName = row.MatchedEmployeeName,
                workDate = AttendanceDisplay.LongDate(row.WorkDate),
                workDateIso = row.WorkDate?.ToString("yyyy-MM-dd"),
                timeIn1 = AttendanceDisplay.Clock(row.TimeIn1),
                timeOut1 = AttendanceDisplay.Clock(row.TimeOut1),
                timeIn2 = AttendanceDisplay.Clock(row.TimeIn2),
                timeOut2 = AttendanceDisplay.Clock(row.TimeOut2),
                overtimeIn = AttendanceDisplay.Clock(row.OvertimeIn),
                overtimeOut = AttendanceDisplay.Clock(row.OvertimeOut),
                row.WorkHoursNormal,
                workHoursActual = computed.WorkHoursActual,
                regularHours = computed.WorkHoursActual,
                overtimeHours = computed.OvertimeHours,
                row.LateMinutes,
                row.EarlyMinutes,
                row.AbsenceDays,
                Status = status,
                statusClass = AttendanceStatuses.CssClass(status),
                row.Matched,
                row.Note,
                issues = issues.Select(i => new { code = i.Code, message = i.Message }).ToList(),
                hasIssues = issues.Count > 0,
                format,
                importedAt = importedAt.HasValue ? PhilippinesTime.FormatDateTime(importedAt.Value) : null,
                actionLabel,
                pendingCorrection,
                payrollLocked,
                requestEdit = !locked,
                locked
            };
        }
    }
}