using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    public class AttendanceController : Controller
    {
        private const string CurrentStaffName = "Patrick Bateman";
        private static readonly string[] AllowedExtensions = { ".xls", ".xlsx", ".csv", ".txt" };

        private readonly PayrollDbContext _db;
        private readonly AttendanceImportService _imports;

        public AttendanceController(PayrollDbContext db, AttendanceImportService imports)
        {
            _db = db;
            _imports = imports;
        }

        public async Task<IActionResult> Import()
        {
            ViewBag.PageTitle = "Import Attendance";
            return View(await LoadProjectsAsync());
        }

        public async Task<IActionResult> Records()
        {
            ViewBag.PageTitle = "Attendance Records";
            return View(await LoadProjectsAsync());
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
                rows = preview.Rows.Select(r => ToRowJson(r))
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> ImportFile(int? projectId, string? projectName, IFormFile? file, string? overridesJson)
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
                    HttpContext.RequestAborted);

                if (result.Error != null)
                    return Json(new { success = false, message = result.Error });

                return Json(new
                {
                    success = true,
                    message = result.ReplacedPrevious
                        ? $"Replaced previous attendance for these dates. Imported {result.RowCount} row(s) for {result.ProjectName}."
                        : $"Imported {result.RowCount} row(s) for {result.ProjectName}.",
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
        public async Task<IActionResult> GetRecords(int projectId, string? search, string? status, int page = 1)
        {
            var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ProjectId == projectId);
            if (project == null)
                return Json(new { success = false, message = "Project not found." });

            const int pageSize = 10;
            var (rows, total) = await _imports.QueryRecordsAsync(
                projectId, search, status, page, pageSize, HttpContext.RequestAborted);

            return Json(new
            {
                success = true,
                projectName = project.ProjectName,
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
                }, r.AttendanceRecordId, r.Import?.Format ?? AttendanceFormats.Daily, r.Import?.ImportedAt))
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
            return Json(new { success = false, message = "Attendance records are view only." });
        }

        private async Task<List<Project>> LoadProjectsAsync()
        {
            var staffName = HttpContext.Session.GetString("FullName") ?? CurrentStaffName;
            var role = HttpContext.Session.GetString("Role");
            var query = _db.Projects.AsNoTracking().Ongoing();

            if (role == "PayrollStaff" && !string.IsNullOrWhiteSpace(staffName))
            {
                var trimmed = staffName.Trim();
                var assigned = await query
                    .Where(p => p.AssignedPayrollStaff != null && p.AssignedPayrollStaff.Trim() == trimmed)
                    .OrderBy(p => p.ProjectName)
                    .ToListAsync();

                if (assigned.Count > 0)
                    return assigned;

                ViewBag.ShowingAllProjects = true;
            }

            return await query.OrderBy(p => p.ProjectName).ToListAsync();
        }

        private string? StaffScope()
        {
            var role = HttpContext.Session.GetString("Role");
            if (role != "PayrollStaff")
                return null;

            return (HttpContext.Session.GetString("FullName") ?? CurrentStaffName).Trim();
        }

        private string ImportedBy() =>
            HttpContext.Session.GetString("FullName") ?? "Staff";

        private static string? ValidateUpload(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return "Choose an attendance Excel or CSV file first.";

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return "Use an .xls, .xlsx, or .csv attendance file.";

            return null;
        }

        private static object ToRowJson(AttendancePreviewRow row, int? recordId = null, string? format = null, DateTime? importedAt = null) => new
        {
            recordId,
            row.EmployeeId,
            row.DisplayId,
            row.ExternalUserId,
            row.EmployeeName,
            workDate = AttendanceDisplay.LongDate(row.WorkDate),
            workDateIso = row.WorkDate?.ToString("yyyy-MM-dd"),
            timeIn1 = AttendanceDisplay.Clock(row.TimeIn1),
            timeOut1 = AttendanceDisplay.Clock(row.TimeOut1),
            timeIn2 = AttendanceDisplay.Clock(row.TimeIn2),
            timeOut2 = AttendanceDisplay.Clock(row.TimeOut2),
            overtimeIn = AttendanceDisplay.Clock(row.OvertimeIn),
            overtimeOut = AttendanceDisplay.Clock(row.OvertimeOut),
            row.WorkHoursNormal,
            row.WorkHoursActual,
            row.LateMinutes,
            row.EarlyMinutes,
            row.OvertimeHours,
            row.AbsenceDays,
            row.Status,
            statusClass = AttendanceStatuses.CssClass(row.Status),
            row.Matched,
            row.Note,
            format,
            importedAt = importedAt?.ToString("MMM dd, yyyy h:mm tt", AttendanceDisplay.English),
            actionLabel = row.Status == AttendanceStatuses.Complete ? "Request Edit" : "Edit"
        };
    }
}
