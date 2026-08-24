using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    /// <summary>
    /// Bell panel + Notifications page.
    /// Recent/UnreadCount/MarkRead are called by wwwroot/js/notifications.js every few seconds.
    ///
    /// Admin extra actions from a notification:
    ///   Attendance correction → GetCorrection / ApproveCorrection / ReturnCorrection
    ///   Staff "mark done"     → GetTask / ApproveTask
    /// </summary>
    public class NotificationController : Controller
    {
        private const int PageSize = 5;

        private readonly PayrollDbContext _db;
        private readonly NotificationService _notifications;
        private readonly AttendanceImportService _imports;
        private readonly ActivityLogService _logs;

        /// <summary>
        /// Receives the database, notification service, and attendance importer used to approve corrections.
        /// </summary>
        public NotificationController(
            PayrollDbContext db,
            NotificationService notifications,
            AttendanceImportService imports,
            ActivityLogService logs)
        {
            _db = db;
            _notifications = notifications;
            _imports = imports;
            _logs = logs;
        }

        public async Task<IActionResult> Index(int page = 1, string? filter = null)
        {
            ViewData["Title"] = "Notifications";
            ViewBag.PageTitle = "Notifications";
            if (IsStaff)
                ViewBag.LayoutName = "_PayrollStaffLayout";

            var unreadOnly = string.Equals(filter, "unread", StringComparison.OrdinalIgnoreCase);
            var (items, total, unread) = await _notifications.ListAsync(
                CurrentRole(), CurrentName(), page, PageSize, unreadOnly, HttpContext.RequestAborted);
            ViewBag.Page = page;
            ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            ViewBag.Unread = unread;
            ViewBag.Filter = unreadOnly ? "unread" : "all";
            ViewBag.IsAdmin = IsAdmin;
            return View(items);
        }

        /// <summary>
        /// GET /Notification/Recent. The header bell polls this every few seconds via notifications.js.
        /// Returns the latest 7 items plus the unread count for the dropdown.
        /// </summary>
        /// <returns>JSON with unread, viewAllUrl, and item rows.</returns>
        [HttpGet]
        public async Task<IActionResult> Recent()
        {
            var items = await _notifications.RecentAsync(
                CurrentRole(), CurrentName(), 15, HttpContext.RequestAborted);
            var unread = await _notifications.UnreadCountAsync(
                CurrentRole(), CurrentName(), HttpContext.RequestAborted);
            return Json(new
            {
                success = true,
                unread,
                viewAllUrl = Url.Action("Index", "Notification"),
                items = items.Select(NotificationService.ToJson)
            });
        }

        /// <summary>
        /// GET /Notification/UnreadCount. The bell badge calls this to refresh the red number.
        /// </summary>
        /// <returns>JSON with the unread count for the signed-in user.</returns>
        [HttpGet]
        public async Task<IActionResult> UnreadCount()
        {
            var unread = await _notifications.UnreadCountAsync(
                CurrentRole(), CurrentName(), HttpContext.RequestAborted);
            return Json(new { success = true, unread });
        }

        /// <summary>
        /// POST /Notification/MarkRead. Clicking one notification marks it read for this user.
        /// </summary>
        /// <returns>JSON success so the dropdown can drop the unread styling.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int id)
        {
            await _notifications.MarkReadAsync(id, CurrentRole(), CurrentName(), HttpContext.RequestAborted);
            return Json(new { success = true });
        }

        /// <summary>
        /// POST /Notification/MarkAllRead. The "Mark all as read" button on the Notifications page.
        /// </summary>
        /// <returns>JSON success after every unread item for this user is cleared.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            await _notifications.MarkAllReadAsync(CurrentRole(), CurrentName(), HttpContext.RequestAborted);
            return Json(new { success = true });
        }

        /// <summary>
        /// GET /Notification/GetCorrection/{id}. Admin opens a correction from a notification.
        /// Loads the pending times and reason so the review modal can show Approve / Return.
        /// </summary>
        /// <returns>JSON with the request fields, or an error if the caller is not Admin.</returns>
        [HttpGet]
        public async Task<IActionResult> GetCorrection(int id)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });

            var request = await _db.AttendanceCorrectionRequests
                .AsNoTracking()
                .Include(c => c.Project)
                .FirstOrDefaultAsync(c => c.AttendanceCorrectionRequestId == id, HttpContext.RequestAborted);
            if (request == null)
                return Json(new { success = false, message = "Correction request not found." });

            return Json(new
            {
                success = true,
                id = request.AttendanceCorrectionRequestId,
                status = request.Status,
                payrollStaff = request.PayrollStaffName,
                projectName = request.Project?.ProjectName ?? "—",
                employeeName = request.EmployeeName,
                date = request.WorkDate?.ToString("MM-dd-yy", AttendanceDisplay.English) ?? "—",
                timeIn1 = AttendanceDisplay.Clock(request.TimeIn1) ?? "—",
                timeOut1 = AttendanceDisplay.Clock(request.TimeOut1) ?? "—",
                timeIn2 = AttendanceDisplay.Clock(request.TimeIn2) ?? "—",
                timeOut2 = AttendanceDisplay.Clock(request.TimeOut2) ?? "—",
                overtimeIn = AttendanceDisplay.Clock(request.OvertimeIn) ?? "—",
                overtimeOut = AttendanceDisplay.Clock(request.OvertimeOut) ?? "—",
                reason = string.IsNullOrWhiteSpace(request.Reason) ? "—" : request.Reason,
                pending = request.Status == CorrectionRequestStatuses.Pending
            });
        }

        /// <summary>
        /// POST /Notification/ApproveCorrection. Admin Approve on a correction modal.
        /// Writes the requested times onto the attendance row, marks the request Approved, and notifies staff.
        /// </summary>
        /// <returns>JSON success after the record is updated, or an error if it was already reviewed.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveCorrection(int id)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });

            var request = await _db.AttendanceCorrectionRequests
                .Include(c => c.Project)
                .FirstOrDefaultAsync(c => c.AttendanceCorrectionRequestId == id, HttpContext.RequestAborted);
            if (request == null)
                return Json(new { success = false, message = "Correction request not found." });
            if (request.Status != CorrectionRequestStatuses.Pending)
                return Json(new { success = false, message = "This request was already reviewed." });

            var attendance = await _db.AttendanceRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.AttendanceRecordId == request.AttendanceRecordId, HttpContext.RequestAborted);
            if (attendance != null
                && await PayrollAttendanceLock.IsLockedAsync(
                    _db, attendance.ProjectId, attendance.EmployeeId, attendance.WorkDate, HttpContext.RequestAborted))
            {
                return Json(new
                {
                    success = false,
                    message = "Payroll for this employee is already approved. Attendance cannot be edited."
                });
            }

            var error = await _imports.UpdateRecordAsync(
                request.AttendanceRecordId,
                AttendanceDisplay.HtmlTime(request.TimeIn1),
                AttendanceDisplay.HtmlTime(request.TimeOut1),
                AttendanceDisplay.HtmlTime(request.TimeIn2),
                AttendanceDisplay.HtmlTime(request.TimeOut2),
                AttendanceDisplay.HtmlTime(request.OvertimeIn),
                AttendanceDisplay.HtmlTime(request.OvertimeOut),
                null,
                HttpContext.RequestAborted);
            if (error != null)
                return Json(new { success = false, message = error });

            request.Status = CorrectionRequestStatuses.Approved;
            request.ReviewedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            var projectName = string.IsNullOrWhiteSpace(request.Project?.ProjectName)
                ? "the project"
                : request.Project!.ProjectName!.Trim();
            var employee = string.IsNullOrWhiteSpace(request.EmployeeName) ? "the employee" : request.EmployeeName.Trim();
            await _notifications.NotifyStaffAsync(
                request.PayrollStaffName,
                NotificationKinds.AttendanceCorrectionApproved,
                "Attendance correction approved",
                $"Admin approved your correction for {employee} on {projectName}. The attendance record is updated.",
                request.ProjectId,
                request.AttendanceCorrectionRequestId,
                "/Attendance/Records",
                HttpContext.RequestAborted);

            await _logs.LogAsync(
                ActivityTypes.ApproveCorrection,
                ActivityModules.Attendance,
                $"Approved an attendance correction for {employee} on {projectName}.",
                request.ProjectId,
                request.AttendanceCorrectionRequestId);

            return Json(new { success = true, message = "Attendance correction approved." });
        }

        /// <summary>
        /// POST /Notification/ReturnCorrection. Admin Return on a correction modal, with an optional reason.
        /// Does not change the attendance row. Staff must edit and send a new request from Attendance Records.
        /// </summary>
        /// <returns>JSON success after the request is marked Returned and staff are notified.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnCorrection(int id, string? reason)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });

            var note = string.IsNullOrWhiteSpace(reason)
                ? "The submitted correction could not be verified."
                : reason.Trim();

            var request = await _db.AttendanceCorrectionRequests
                .Include(c => c.Project)
                .FirstOrDefaultAsync(c => c.AttendanceCorrectionRequestId == id, HttpContext.RequestAborted);
            if (request == null)
                return Json(new { success = false, message = "Correction request not found." });
            if (request.Status != CorrectionRequestStatuses.Pending)
                return Json(new { success = false, message = "This request was already reviewed." });

            request.Status = CorrectionRequestStatuses.Returned;
            request.ReturnReason = note;
            request.ReviewedAt = DateTime.Now;
            await _db.SaveChangesAsync();

            var projectName = string.IsNullOrWhiteSpace(request.Project?.ProjectName)
                ? "the project"
                : request.Project!.ProjectName!.Trim();
            var employee = string.IsNullOrWhiteSpace(request.EmployeeName) ? "the employee" : request.EmployeeName.Trim();
            await _notifications.NotifyStaffAsync(
                request.PayrollStaffName,
                NotificationKinds.AttendanceCorrectionRejected,
                "Attendance correction returned",
                $"Admin returned your correction for {employee} on {projectName}. Reason: {note}. Open Attendance Records to edit and send it again.",
                request.ProjectId,
                request.AttendanceCorrectionRequestId,
                "/Attendance/Records",
                HttpContext.RequestAborted);

            await _logs.LogAsync(
                ActivityTypes.ReturnCorrection,
                ActivityModules.Attendance,
                $"Returned an attendance correction for {employee} on {projectName}.",
                request.ProjectId,
                request.AttendanceCorrectionRequestId);

            return Json(new { success = true, message = "Correction request returned." });
        }

        /// <summary>
        /// GET /Notification/GetTask/{id}. Admin opens a "mark done" notification.
        /// Loads the payroll schedule so the modal can show Approve if TaskCompleted is true and TaskApproved is still false.
        /// </summary>
        /// <returns>JSON with schedule details and a pending flag.</returns>
        [HttpGet]
        public async Task<IActionResult> GetTask(int id)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });

            var schedule = await _db.PayrollSchedules
                .AsNoTracking()
                .Include(s => s.Project)
                .FirstOrDefaultAsync(s => s.PayrollScheduleId == id, HttpContext.RequestAborted);
            if (schedule == null)
                return Json(new { success = false, message = "Task not found." });

            var pending = schedule.TaskCompleted && !schedule.TaskApproved;
            return Json(new
            {
                success = true,
                id = schedule.PayrollScheduleId,
                payrollStaff = string.IsNullOrWhiteSpace(schedule.Project?.AssignedPayrollStaff)
                    ? "—"
                    : schedule.Project!.AssignedPayrollStaff.Trim(),
                projectName = string.IsNullOrWhiteSpace(schedule.Project?.ProjectName)
                    ? "—"
                    : schedule.Project!.ProjectName!.Trim(),
                projectType = schedule.TypeOfService ?? schedule.Project?.TypeOfService ?? "—",
                period = PayrollPeriods.Label(schedule),
                pending
            });
        }

        /// <summary>
        /// Admin accepts the staff "mark done". TaskApproved=true removes it from to-do.
        /// POST /Notification/ApproveTask from the task modal. Staff already clicked Mark as Done (ToggleTask).
        /// </summary>
        /// <returns>JSON success after the schedule is approved and staff are notified.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveTask(int id)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });

            var schedule = await _db.PayrollSchedules
                .Include(s => s.Project)
                .FirstOrDefaultAsync(s => s.PayrollScheduleId == id, HttpContext.RequestAborted);
            if (schedule == null)
                return Json(new { success = false, message = "Task not found." });
            if (!schedule.TaskCompleted)
                return Json(new { success = false, message = "Payroll staff have not marked this task as done." });
            if (schedule.TaskApproved)
                return Json(new { success = false, message = "This task was already approved." });

            schedule.TaskApproved = true;
            await _db.SaveChangesAsync();
            await _notifications.NotifyTaskCompletionApprovedAsync(schedule, HttpContext.RequestAborted);

            return Json(new { success = true, message = "Task approved. It has been removed from To do task." });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnTask(int id, string? reason)
        {
            if (!IsAdmin)
                return Json(new { success = false, message = "Admin access is required." });

            var note = string.IsNullOrWhiteSpace(reason)
                ? "Please correct this payroll task and mark it done again."
                : reason.Trim();

            var schedule = await _db.PayrollSchedules
                .Include(s => s.Project)
                .FirstOrDefaultAsync(s => s.PayrollScheduleId == id, HttpContext.RequestAborted);
            if (schedule == null)
                return Json(new { success = false, message = "Task not found." });
            if (!schedule.TaskCompleted)
                return Json(new { success = false, message = "Payroll staff have not marked this task as done." });
            if (schedule.TaskApproved)
                return Json(new { success = false, message = "This task was already approved." });

            schedule.TaskCompleted = false;
            schedule.TaskApproved = false;
            await _db.SaveChangesAsync();
            await _notifications.NotifyTaskCompletionReturnedAsync(schedule, note, HttpContext.RequestAborted);

            return Json(new { success = true, message = "Task returned for correction." });
        }

        private bool IsAdmin =>
            string.Equals(HttpContext.Session.GetString("Role"), "Admin", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when the session Role is PayrollStaff. Index uses this to pick the staff layout.
        /// </summary>
        private bool IsStaff =>
            string.Equals(HttpContext.Session.GetString("Role"), "PayrollStaff", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Session Role string passed into NotificationService so Admin and staff see different lists.
        /// </summary>
        private string CurrentRole() =>
            HttpContext.Session.GetString("Role") ?? "";

        /// <summary>
        /// Session FullName used to match staff-targeted notifications.
        /// </summary>
        private string CurrentName() =>
            HttpContext.Session.GetString("FullName") ?? "";
    }
}
