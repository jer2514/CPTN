using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    public class NotificationController : Controller
    {
        private const int PageSize = 5;

        private readonly PayrollDbContext _db;
        private readonly NotificationService _notifications;
        private readonly AttendanceImportService _imports;

        public NotificationController(
            PayrollDbContext db,
            NotificationService notifications,
            AttendanceImportService imports)
        {
            _db = db;
            _notifications = notifications;
            _imports = imports;
        }

        public async Task<IActionResult> Index(int page = 1)
        {
            ViewData["Title"] = "Notifications";
            ViewBag.PageTitle = "Notifications";
            if (IsStaff)
                ViewBag.LayoutName = "_PayrollStaffLayout";

            var (items, total, unread) = await _notifications.ListAsync(
                CurrentRole(), CurrentName(), page, PageSize, HttpContext.RequestAborted);
            ViewBag.Page = page;
            ViewBag.TotalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            ViewBag.Unread = unread;
            ViewBag.IsAdmin = IsAdmin;
            return View(items);
        }

        [HttpGet]
        public async Task<IActionResult> Recent()
        {
            var items = await _notifications.RecentAsync(
                CurrentRole(), CurrentName(), 7, HttpContext.RequestAborted);
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

        [HttpGet]
        public async Task<IActionResult> UnreadCount()
        {
            var unread = await _notifications.UnreadCountAsync(
                CurrentRole(), CurrentName(), HttpContext.RequestAborted);
            return Json(new { success = true, unread });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkRead(int id)
        {
            await _notifications.MarkReadAsync(id, CurrentRole(), CurrentName(), HttpContext.RequestAborted);
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            await _notifications.MarkAllReadAsync(CurrentRole(), CurrentName(), HttpContext.RequestAborted);
            return Json(new { success = true });
        }

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
                reason = string.IsNullOrWhiteSpace(request.Reason) ? "Incomplete Attendance" : request.Reason,
                pending = request.Status == CorrectionRequestStatuses.Pending
            });
        }

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
            await _notifications.NotifyStaffAsync(
                request.PayrollStaffName,
                NotificationKinds.AttendanceCorrectionApproved,
                "Attendance Correction Approved",
                $"Your attendance correction request for {projectName} has been approved by the Admin.",
                request.ProjectId,
                request.AttendanceCorrectionRequestId,
                "/Attendance/Records",
                HttpContext.RequestAborted);

            return Json(new { success = true, message = "Attendance correction approved." });
        }

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
            await _notifications.NotifyStaffAsync(
                request.PayrollStaffName,
                NotificationKinds.AttendanceCorrectionRejected,
                "Attendance Correction Rejected",
                $"Your attendance correction request for {projectName} was rejected. Reason: {note}",
                request.ProjectId,
                request.AttendanceCorrectionRequestId,
                "/Attendance/Records",
                HttpContext.RequestAborted);

            return Json(new { success = true, message = "Correction request returned." });
        }

        private bool IsAdmin =>
            string.Equals(HttpContext.Session.GetString("Role"), "Admin", StringComparison.OrdinalIgnoreCase);

        private bool IsStaff =>
            string.Equals(HttpContext.Session.GetString("Role"), "PayrollStaff", StringComparison.OrdinalIgnoreCase);

        private string CurrentRole() =>
            HttpContext.Session.GetString("Role") ?? "";

        private string CurrentName() =>
            HttpContext.Session.GetString("FullName") ?? "";
    }
}
