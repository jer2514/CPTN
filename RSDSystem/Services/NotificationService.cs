using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;

namespace RSDSystem.Services
{
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
                Message = message,
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
                Message = message,
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
            await NotifyAdminsAsync(
                NotificationKinds.PayrollSubmitted,
                "Payroll Submitted",
                $"Payroll for {name} has been submitted for approval.",
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
                        "Payroll Prediction Available",
                        $"Payroll prediction for {name} is now available for review.",
                        project.ProjectId,
                        null,
                        "/Payroll/Prediction",
                        cancellationToken);

                    if (page.Rows.Any(r => r.UnusualChange))
                    {
                        await NotifyAdminsAsync(
                            NotificationKinds.PayrollAnomalyPattern,
                            "Payroll Anomaly",
                            "A significant increase in payroll was detected compared with the usual payroll pattern.",
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
                    "Payroll Anomaly",
                    $"Payroll for {name} may exceed the allocated project budget.",
                    project.ProjectId,
                    null,
                    "/Payroll/ReviewProject?projectId=" + project.ProjectId,
                    cancellationToken);
            }
        }

        public async Task NotifyAttendanceImportedAsync(Project project, string? importedBy, CancellationToken cancellationToken = default)
        {
            var name = ProjectName(project);
            var staff = string.IsNullOrWhiteSpace(importedBy) ? "Payroll Staff" : importedBy.Trim();
            await NotifyAdminsAsync(
                NotificationKinds.AttendanceImported,
                "Attendance Imported",
                $"Attendance records for {name} have been imported by {staff}.",
                project.ProjectId,
                null,
                "/Attendance/Records",
                cancellationToken);
        }

        public async Task NotifyNewTaskAsync(Project project, CancellationToken cancellationToken = default)
        {
            var staff = project.AssignedPayrollStaff?.Trim();
            if (string.IsNullOrWhiteSpace(staff))
                return;

            await NotifyStaffAsync(
                staff,
                NotificationKinds.NewTask,
                "New Task",
                "Admin assigned you a new task.",
                project.ProjectId,
                null,
                "/PayrollStaff/Index",
                cancellationToken);
        }

        public async Task NotifyPayrollApprovedAsync(Payroll payroll, Project? project, CancellationToken cancellationToken = default)
        {
            var staff = StaffFor(payroll, project);
            var name = ProjectName(project) is var n && n != "the project" ? n : "payroll";
            await NotifyStaffAsync(
                staff,
                NotificationKinds.PayrollApproved,
                "Payroll Approved",
                $"Payroll for {name} has been approved by the Admin.",
                payroll.ProjectId,
                payroll.PayrollId,
                "/PayrollStaff/PendingPayroll?projectId=" + payroll.ProjectId,
                cancellationToken);
        }

        public async Task NotifyPayrollReturnedAsync(Payroll payroll, Project? project, string reason, CancellationToken cancellationToken = default)
        {
            var staff = StaffFor(payroll, project);
            await NotifyStaffAsync(
                staff,
                NotificationKinds.PayrollCorrection,
                "Payroll Correction",
                $"Payroll for {staff} has been returned by the Admin for correction.",
                payroll.ProjectId,
                payroll.PayrollId,
                "/PayrollStaff/PendingPayroll?projectId=" + payroll.ProjectId,
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

        private static string ProjectName(Project? project) =>
            string.IsNullOrWhiteSpace(project?.ProjectName) ? "the project" : project!.ProjectName!.Trim();

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
