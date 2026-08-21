using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    public class AppNotification
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AppNotificationId { get; set; }

        [MaxLength(30)]
        public string RecipientRole { get; set; } = NotificationRoles.Admin;

        [MaxLength(150)]
        public string? RecipientName { get; set; }

        [MaxLength(60)]
        public string Kind { get; set; } = string.Empty;

        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        public int? ProjectId { get; set; }

        public int? RelatedId { get; set; }

        [MaxLength(250)]
        public string? Url { get; set; }

        public bool IsRead { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public static class NotificationRoles
    {
        public const string Admin = "Admin";
        public const string PayrollStaff = "PayrollStaff";
    }

    public static class NotificationKinds
    {
        public const string PayrollSubmitted = "PayrollSubmitted";
        public const string PayrollResubmitted = "PayrollResubmitted";
        public const string PayrollPredictionAvailable = "PayrollPredictionAvailable";
        public const string AttendanceImported = "AttendanceImported";
        public const string PayrollAnomalyBudget = "PayrollAnomalyBudget";
        public const string PayrollAnomalyPattern = "PayrollAnomalyPattern";
        public const string AttendanceCorrectionRequest = "AttendanceCorrectionRequest";
        public const string AttendanceCorrectionResubmitted = "AttendanceCorrectionResubmitted";
        public const string PayrollCorrection = "PayrollCorrection";
        public const string NewTask = "NewTask";
        public const string TaskCompletionRequested = "TaskCompletionRequested";
        public const string TaskCompletionApproved = "TaskCompletionApproved";
        public const string TaskCompletionReturned = "TaskCompletionReturned";
        public const string PayrollApproved = "PayrollApproved";
        public const string AttendanceCorrectionApproved = "AttendanceCorrectionApproved";
        public const string AttendanceCorrectionRejected = "AttendanceCorrectionRejected";
        public const string PayslipsSent = "PayslipsSent";
        public const string StaffAssigned = "StaffAssigned";

        public static string Icon(string? kind) => (kind ?? "").Trim() switch
        {
            PayrollSubmitted => "bi-send-fill",
            PayrollResubmitted => "bi-arrow-repeat",
            PayrollPredictionAvailable => "bi-check-circle-fill",
            AttendanceImported => "bi-calendar-event-fill",
            PayrollAnomalyBudget => "bi-exclamation-circle-fill",
            PayrollAnomalyPattern => "bi-exclamation-circle-fill",
            AttendanceCorrectionRequest => "bi-file-earmark-text-fill",
            AttendanceCorrectionResubmitted => "bi-arrow-repeat",
            PayrollCorrection => "bi-exclamation-triangle-fill",
            NewTask => "bi-file-earmark-fill",
            TaskCompletionRequested => "bi-clipboard-check-fill",
            TaskCompletionApproved => "bi-check-circle-fill",
            TaskCompletionReturned => "bi-arrow-return-left",
            PayrollApproved => "bi-check-circle-fill",
            AttendanceCorrectionApproved => "bi-file-earmark-text-fill",
            AttendanceCorrectionRejected => "bi-file-earmark-excel-fill",
            PayslipsSent => "bi-file-earmark-pdf-fill",
            StaffAssigned => "bi-person-check-fill",
            _ => "bi-bell-fill"
        };

        public static string IconClass(string? kind) => (kind ?? "").Trim() switch
        {
            PayrollSubmitted => "notif-icon-send",
            PayrollResubmitted => "notif-icon-send",
            PayrollPredictionAvailable => "notif-icon-ok",
            AttendanceImported => "notif-icon-cal",
            PayrollAnomalyBudget => "notif-icon-warn",
            PayrollAnomalyPattern => "notif-icon-warn",
            AttendanceCorrectionRequest => "notif-icon-doc",
            AttendanceCorrectionResubmitted => "notif-icon-doc",
            PayrollCorrection => "notif-icon-warn",
            NewTask => "notif-icon-task",
            TaskCompletionRequested => "notif-icon-doc",
            TaskCompletionApproved => "notif-icon-ok",
            TaskCompletionReturned => "notif-icon-warn",
            PayrollApproved => "notif-icon-ok",
            AttendanceCorrectionApproved => "notif-icon-task",
            AttendanceCorrectionRejected => "notif-icon-reject",
            PayslipsSent => "notif-icon-send",
            StaffAssigned => "notif-icon-task",
            _ => "notif-icon-task"
        };
    }
}
