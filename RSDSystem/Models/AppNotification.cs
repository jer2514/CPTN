using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    /// <summary>
    /// Bell-item stored in AppNotifications.
    /// RecipientRole = Admin (all admins) or PayrollStaff (one person via RecipientName).
    /// Kind constants below tell the UI which icon to show. Url is where the bell click goes.
    /// </summary>
    public class AppNotification
    {
        /// <summary>Database primary key; posted as id when marking read or opening a modal.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AppNotificationId { get; set; }

        /// <summary>Admin (all admins see it) or PayrollStaff (only RecipientName sees it).</summary>
        [MaxLength(30)]
        public string RecipientRole { get; set; } = NotificationRoles.Admin;

        /// <summary>Payroll staff FullName when RecipientRole is PayrollStaff; null for Admin broadcasts.</summary>
        [MaxLength(150)]
        public string? RecipientName { get; set; }

        /// <summary>NotificationKinds value that picks the bell icon/color and Admin modal type.</summary>
        [MaxLength(60)]
        public string Kind { get; set; } = string.Empty;

        /// <summary>Short heading on the bell row (e.g. "Payroll Submitted").</summary>
        [MaxLength(120)]
        public string Title { get; set; } = string.Empty;

        /// <summary>Longer body text under the title.</summary>
        [MaxLength(500)]
        public string Message { get; set; } = string.Empty;

        /// <summary>Optional project this event is about (review payroll, open records, …).</summary>
        public int? ProjectId { get; set; }

        /// <summary>PayrollId, AttendanceCorrectionRequestId, or PayrollScheduleId depending on Kind.</summary>
        public int? RelatedId { get; set; }

        /// <summary>Where the bell click navigates if there is no in-panel modal.</summary>
        [MaxLength(250)]
        public string? Url { get; set; }

        /// <summary>True after MarkRead; unread items show a badge count and a blue dot.</summary>
        public bool IsRead { get; set; }

        /// <summary>When the bell item was created; newest first on Recent/Index.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>Who sees the bell item: all Admins, or one PayrollStaff by name.</summary>
    public static class NotificationRoles
    {
        /// <summary>Every Admin user sees this item (RecipientName is unused).</summary>
        public const string Admin = "Admin";

        /// <summary>Only the PayrollStaff whose FullName equals RecipientName sees this item.</summary>
        public const string PayrollStaff = "PayrollStaff";
    }

    /// <summary>Kind string stored on AppNotification. Picks the icon/color in the bell UI.</summary>
    public static class NotificationKinds
    {
        /// <summary>Staff submitted slips; Admin is sent to ReviewProject.</summary>
        public const string PayrollSubmitted = "PayrollSubmitted";
        public const string PayrollResubmitted = "PayrollResubmitted";
        public const string PayrollPredictionAvailable = "PayrollPredictionAvailable";

        /// <summary>Staff imported attendance for a project.</summary>
        public const string AttendanceImported = "AttendanceImported";

        /// <summary>Predicted payroll exceeds the allocated monthly budget.</summary>
        public const string PayrollAnomalyBudget = "PayrollAnomalyBudget";

        /// <summary>Predicted payroll jumped unusually versus prior months.</summary>
        public const string PayrollAnomalyPattern = "PayrollAnomalyPattern";

        /// <summary>Staff requested a punch change; Admin opens the correction modal.</summary>
        public const string AttendanceCorrectionRequest = "AttendanceCorrectionRequest";
        public const string AttendanceCorrectionResubmitted = "AttendanceCorrectionResubmitted";
        public const string PayrollCorrection = "PayrollCorrection";

        /// <summary>Admin added a PayrollSchedule; staff see a new to-do task.</summary>
        public const string NewTask = "NewTask";

        /// <summary>Staff marked a schedule done; Admin opens the task-approval modal.</summary>
        public const string TaskCompletionRequested = "TaskCompletionRequested";

        /// <summary>Admin approved the done request; task leaves the staff to-do list.</summary>
        public const string TaskCompletionApproved = "TaskCompletionApproved";
        public const string TaskCompletionReturned = "TaskCompletionReturned";
        public const string PayrollApproved = "PayrollApproved";

        /// <summary>Admin approved a punch correction; new times are on the record.</summary>
        public const string AttendanceCorrectionApproved = "AttendanceCorrectionApproved";

        /// <summary>Admin returned a punch correction; staff see the return reason.</summary>
        public const string AttendanceCorrectionRejected = "AttendanceCorrectionRejected";
        public const string PayslipsSent = "PayslipsSent";
        public const string StaffAssigned = "StaffAssigned";

        /// <summary>Bootstrap-icons class for the round icon in the bell row.</summary>
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

        /// <summary>Color wrapper class (ok / warn / send / …) around that icon.</summary>
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
