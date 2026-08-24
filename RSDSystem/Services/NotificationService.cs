using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    /// <summary>
    /// Creates bell rows. Controllers call Notify* after a real event
    /// (payroll submitted, attendance imported, task marked done, prediction ready, …).
    /// NotifyAdminsAsync writes RecipientRole=Admin (all admins see it).
    /// NotifyStaffAsync writes RecipientRole=PayrollStaff + RecipientName.
    /// RecentlyNotifiedAsync blocks duplicate spam of the same kind for a few minutes.
    /// </summary>
    public class NotificationService
    {
        private readonly PayrollDbContext _db;
        private readonly PayrollPredictionService _predictions;

        public NotificationService(PayrollDbContext db, PayrollPredictionService predictions)
        {
            _db = db;
            _predictions = predictions;
        }

        public async Task NotifyAdminsAsync(
            string kind, string title, string message,
            int? projectId = null, int? relatedId = null, string? url = null,
            CancellationToken cancellationToken = default)
        {
            if (await RecentlyNotifiedAsync(NotificationRoles.Admin, null, kind, projectId, relatedId, cancellationToken))
                return;

            _db.AppNotifications.Add(new AppNotification
            {
                RecipientRole = NotificationRoles.Admin,
                RecipientName = null,
                Kind = kind,
                Title = title,
                Message = ClipMessage(message),
                ProjectId = projectId,
                RelatedId = relatedId,
                Url = url,
                CreatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task NotifyStaffAsync(
            string staffName, string kind, string title, string message,
            int? projectId = null, int? relatedId = null, string? url = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(staffName))
                return;

            var name = staffName.Trim();
            if (await RecentlyNotifiedAsync(NotificationRoles.PayrollStaff, name, kind, projectId, relatedId, cancellationToken))
                return;

            _db.AppNotifications.Add(new AppNotification
            {
                RecipientRole = NotificationRoles.PayrollStaff,
                RecipientName = name,
                Kind = kind,
                Title = title,
                Message = ClipMessage(message),
                ProjectId = projectId,
                RelatedId = relatedId,
                Url = url,
                CreatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task NotifyPayrollSubmittedAsync(Project project, string? submittedBy, CancellationToken cancellationToken = default)
        {
            var name = ProjectName(project);
            var staff = PersonName(submittedBy, "Payroll staff");
            await NotifyAdminsAsync(
                NotificationKinds.PayrollSubmitted,
                "Payroll submitted for review",
                $"{staff} submitted payroll for {name}. Open Review Payroll to approve or return it.",
                project.ProjectId,
                null,
                "/Payroll/ReviewProject?projectId=" + project.ProjectId,
                cancellationToken);

            await NotifyPayrollAlertsAsync(project, cancellationToken);
        }

        public async Task NotifyPayrollAlertsAsync(Project project, CancellationToken cancellationToken = default)
        {
            var name = ProjectName(project);
            try
            {
                var page = await _predictions.LoadAsync(project.ProjectId, cancellationToken);
                if (page.Rows.Count > 0 && page.Error == null)
                {
                    await NotifyAdminsAsync(
                        NotificationKinds.PayrollPredictionAvailable,
                        "Payroll prediction is ready",
                        $"A payroll prediction for {name} is ready. Open Payroll Prediction to review the next-month estimate.",
                        project.ProjectId,
                        null,
                        "/Payroll/Prediction",
                        cancellationToken);

                    if (page.Rows.Any(r => r.ExceedsBudget))
                    {
                        var over = page.Rows.First(r => r.ExceedsBudget);
                        await NotifyAdminsAsync(
                            NotificationKinds.PayrollAnomalyBudget,
                            "Next month may exceed budget",
                            $"The predicted amount for {over.PredictionLabel} on {name} is {Peso(over.PredictedPayroll)}, which exceeds the allocated budget of {Peso(over.AllocatedBudget)}. Open Payroll Prediction to review.",
                            project.ProjectId,
                            null,
                            "/Payroll/Prediction",
                            cancellationToken);
                    }

                    if (page.Rows.Any(r => r.UnusualChange))
                    {
                        await NotifyAdminsAsync(
                            NotificationKinds.PayrollAnomalyPattern,
                            "Unusual payroll change",
                            $"Payroll for {name} jumped compared with recent months. Open Payroll Prediction to review the change before approving.",
                            project.ProjectId,
                            null,
                            "/Payroll/Prediction",
                            cancellationToken);
                    }
                }
            }
            catch
            {
                // Prediction is optional; submission still succeeded.
            }

            var budget = project.PayrollBudget ?? 0;
            if (budget <= 0)
                return;

            var latest = await _db.Payrolls.AsNoTracking()
                .Where(p => p.ProjectId == project.ProjectId)
                .OrderByDescending(p => p.PayPeriodEnd)
                .Select(p => new { p.PayPeriodStart, p.PayPeriodEnd })
                .FirstOrDefaultAsync(cancellationToken);
            if (latest == null)
                return;

            var periodTotal = await _db.Payrolls.AsNoTracking()
                .Where(p => p.ProjectId == project.ProjectId
                    && p.PayPeriodStart == latest.PayPeriodStart
                    && p.PayPeriodEnd == latest.PayPeriodEnd)
                .SumAsync(p => (decimal?)p.NetPay, cancellationToken) ?? 0;

            if (periodTotal > budget)
            {
                await NotifyAdminsAsync(
                    NotificationKinds.PayrollAnomalyBudget,
                    "Payroll may exceed budget",
                    $"Payroll for {name} this period is {Peso(periodTotal)}, which is over the {Peso(budget)} payroll budget. Review the payroll before approving.",
                    project.ProjectId,
                    null,
                    "/Payroll/ReviewProject?projectId=" + project.ProjectId,
                    cancellationToken);
            }
        }

        public async Task NotifyAttendanceImportedAsync(Project project, string? importedBy, CancellationToken cancellationToken = default)
        {
            var name = ProjectName(project);
            var staff = PersonName(importedBy, "Payroll staff");
            await NotifyAdminsAsync(
                NotificationKinds.AttendanceImported,
                "Attendance imported",
                $"{staff} imported attendance for {name}. Open Attendance Records to review or edit the punches.",
                project.ProjectId,
                null,
                "/Attendance/Records",
                cancellationToken);
        }

        public async Task NotifyNewTaskAsync(
            Project project, DateTime? periodStart = null, DateTime? periodEnd = null,
            CancellationToken cancellationToken = default)
        {
            var staff = project.AssignedPayrollStaff?.Trim();
            if (string.IsNullOrWhiteSpace(staff))
                return;

            var name = ProjectName(project);
            var period = PeriodLabel(periodStart, periodEnd);
            var when = string.IsNullOrEmpty(period) ? "" : $" ({period})";
            await NotifyStaffAsync(
                staff,
                NotificationKinds.NewTask,
                "New payroll task",
                $"Admin assigned you to generate payroll for {name}{when}. Open To do task, import attendance if needed, then generate payroll.",
                project.ProjectId,
                null,
                "/PayrollStaff/Index",
                cancellationToken);
        }

        public async Task NotifyTaskCompletionRequestedAsync(
            PayrollSchedule schedule, string? staffName, CancellationToken cancellationToken = default)
        {
            var project = schedule.Project;
            var name = ProjectName(project);
            var staff = PersonName(staffName ?? project?.AssignedPayrollStaff, "Payroll staff");
            var period = PeriodLabel(schedule.StartingDate, schedule.EndDate);
            await NotifyAdminsAsync(
                NotificationKinds.TaskCompletionRequested,
                "Task marked done — approval needed",
                $"{staff} marked the payroll task for {name} ({period}) as done. Open the notification to approve and close the task.",
                schedule.ProjectId,
                schedule.PayrollScheduleId,
                "/Notification/Index",
                cancellationToken);
        }

        public async Task NotifyTaskCompletionApprovedAsync(
            PayrollSchedule schedule, CancellationToken cancellationToken = default)
        {
            var project = schedule.Project;
            var staff = project?.AssignedPayrollStaff?.Trim();
            if (string.IsNullOrWhiteSpace(staff))
                return;

            var name = ProjectName(project);
            var period = PeriodLabel(schedule.StartingDate, schedule.EndDate);
            await NotifyStaffAsync(
                staff,
                NotificationKinds.TaskCompletionApproved,
                "Task approved",
                $"Admin approved your completed task for {name} ({period}). It has been removed from To do task.",
                schedule.ProjectId,
                schedule.PayrollScheduleId,
                "/PayrollStaff/Index",
                cancellationToken);
        }

        public async Task NotifyPayrollApprovedAsync(Payroll payroll, Project? project, CancellationToken cancellationToken = default)
        {
            var staff = StaffFor(payroll, project);
            var name = ProjectName(project);
            var employee = PersonName(payroll.Employee?.FullName, "an employee");
            var period = PeriodLabel(payroll.PayPeriodStart, payroll.PayPeriodEnd);
            await NotifyStaffAsync(
                staff,
                NotificationKinds.PayrollApproved,
                "Payroll approved",
                $"Admin approved the payroll slip for {employee} on {name} ({period}).",
                payroll.ProjectId,
                payroll.PayrollId,
                "/PayrollStaff/PendingPayroll?projectId=" + payroll.ProjectId,
                cancellationToken);
        }

        public async Task NotifyPayrollReturnedAsync(Payroll payroll, Project? project, string reason, CancellationToken cancellationToken = default)
        {
            var staff = StaffFor(payroll, project);
            var name = ProjectName(project);
            var employee = PersonName(payroll.Employee?.FullName, "an employee");
            var period = PeriodLabel(payroll.PayPeriodStart, payroll.PayPeriodEnd);
            var note = string.IsNullOrWhiteSpace(reason) ? "No reason was given." : reason.Trim();
            await NotifyStaffAsync(
                staff,
                NotificationKinds.PayrollCorrection,
                "Payroll returned for correction",
                $"Admin returned the payroll slip for {employee} on {name} ({period}). Reason: {note}. Open Pending Payroll to correct and submit again.",
                payroll.ProjectId,
                payroll.PayrollId,
                "/PayrollStaff/PendingPayroll?projectId=" + payroll.ProjectId,
                cancellationToken);
        }

        public async Task NotifyAttendanceCorrectionRequestedAsync(
            string? staffName, string? employeeName, string? projectName, DateTime? workDate,
            int projectId, int requestId, CancellationToken cancellationToken = default)
        {
            var staff = PersonName(staffName, "Payroll staff");
            var employee = PersonName(employeeName, "an employee");
            var project = string.IsNullOrWhiteSpace(projectName) ? "the project" : projectName.Trim();
            var date = workDate.HasValue
                ? workDate.Value.ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : "an attendance date";
            await NotifyAdminsAsync(
                NotificationKinds.AttendanceCorrectionRequest,
                "Attendance correction requested",
                $"{staff} asked to correct {employee}'s attendance on {date} for {project}. Open the notification to approve or return the request.",
                projectId,
                requestId,
                "/Notification/Index",
                cancellationToken);
        }

        public async Task<(List<AppNotification> Items, int Total, int Unread)> ListAsync(
            string role, string? fullName, int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var query = ForUser(_db.AppNotifications.AsNoTracking(), role, fullName);
            var total = await query.CountAsync(cancellationToken);
            var unread = await query.CountAsync(n => !n.IsRead, cancellationToken);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 50);
            var items = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            return (items, total, unread);
        }

        public async Task<List<AppNotification>> RecentAsync(
            string role, string? fullName, int take = 7, CancellationToken cancellationToken = default)
        {
            return await ForUser(_db.AppNotifications.AsNoTracking(), role, fullName)
                .OrderByDescending(n => n.CreatedAt)
                .Take(Math.Clamp(take, 1, 20))
                .ToListAsync(cancellationToken);
        }

        public async Task<int> UnreadCountAsync(string role, string? fullName, CancellationToken cancellationToken = default) =>
            await ForUser(_db.AppNotifications.AsNoTracking(), role, fullName)
                .CountAsync(n => !n.IsRead, cancellationToken);

        public async Task MarkReadAsync(int id, string role, string? fullName, CancellationToken cancellationToken = default)
        {
            var item = await ForUser(_db.AppNotifications, role, fullName)
                .FirstOrDefaultAsync(n => n.AppNotificationId == id, cancellationToken);
            if (item == null || item.IsRead)
                return;
            item.IsRead = true;
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task MarkAllReadAsync(string role, string? fullName, CancellationToken cancellationToken = default)
        {
            var unread = await ForUser(_db.AppNotifications, role, fullName)
                .Where(n => !n.IsRead)
                .ToListAsync(cancellationToken);
            foreach (var item in unread)
                item.IsRead = true;
            if (unread.Count > 0)
                await _db.SaveChangesAsync(cancellationToken);
        }

        public static string RelativeTime(DateTime createdAt)
        {
            var local = createdAt.Kind == DateTimeKind.Utc ? createdAt.ToLocalTime() : createdAt;
            var now = DateTime.Now;
            var span = now - local;
            if (span.TotalSeconds < 45)
                return "just now";
            if (span.TotalMinutes < 60)
            {
                var minutes = Math.Max(1, (int)Math.Round(span.TotalMinutes));
                return minutes == 1 ? "1 min ago" : minutes + " min ago";
            }
            if (span.TotalHours < 24 && local.Date == now.Date)
            {
                var hours = Math.Max(1, (int)Math.Round(span.TotalHours));
                return hours == 1 ? "1 hour ago" : hours + " hours ago";
            }
            if (local.Date == now.Date.AddDays(-1))
                return "yesterday";
            return local.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static object ToJson(AppNotification n) => new
        {
            id = n.AppNotificationId,
            kind = n.Kind,
            title = n.Title,
            message = n.Message,
            timeAgo = RelativeTime(n.CreatedAt),
            isRead = n.IsRead,
            url = n.Url,
            relatedId = n.RelatedId,
            projectId = n.ProjectId,
            icon = NotificationKinds.Icon(n.Kind),
            iconClass = NotificationKinds.IconClass(n.Kind)
        };

        private static IQueryable<AppNotification> ForUser(IQueryable<AppNotification> source, string role, string? fullName)
        {
            if (string.Equals(role, NotificationRoles.Admin, StringComparison.OrdinalIgnoreCase))
                return source.Where(n => n.RecipientRole == NotificationRoles.Admin);

            var name = (fullName ?? "").Trim().ToLower();
            return source.Where(n => n.RecipientRole == NotificationRoles.PayrollStaff
                && n.RecipientName != null
                && n.RecipientName.Trim().ToLower() == name);
        }

        private async Task<bool> RecentlyNotifiedAsync(
            string role, string? name, string kind, int? projectId, int? relatedId,
            CancellationToken cancellationToken)
        {
            var since = DateTime.Now.AddMinutes(-10);
            var query = _db.AppNotifications.AsNoTracking()
                .Where(n => n.RecipientRole == role
                    && n.Kind == kind
                    && n.CreatedAt >= since
                    && n.ProjectId == projectId);

            if (!string.IsNullOrWhiteSpace(name))
                query = query.Where(n => n.RecipientName == name);

            if (relatedId.HasValue)
                query = query.Where(n => n.RelatedId == relatedId);

            return await query.AnyAsync(cancellationToken);
        }

        private static string ClipMessage(string message)
        {
            var text = (message ?? "").Trim();
            return text.Length <= 500 ? text : text[..497] + "...";
        }

        private static string ProjectName(Project? project) =>
            string.IsNullOrWhiteSpace(project?.ProjectName) ? "the project" : project!.ProjectName!.Trim();

        private static string PersonName(string? name, string fallback) =>
            string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

        private static string PeriodLabel(DateTime? start, DateTime? end)
        {
            if (!start.HasValue || !end.HasValue)
                return "";
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return start.Value.ToString("MMMM d, yyyy", culture) + " – " + end.Value.ToString("MMMM d, yyyy", culture);
        }

        private static string Peso(decimal amount) =>
            "₱" + amount.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);

        private static string StaffFor(Payroll payroll, Project? project)
        {
            if (!string.IsNullOrWhiteSpace(payroll.GeneratedBy))
                return payroll.GeneratedBy.Trim();
            if (!string.IsNullOrWhiteSpace(project?.AssignedPayrollStaff))
                return project.AssignedPayrollStaff.Trim();
            return "";
        }
    }
}
