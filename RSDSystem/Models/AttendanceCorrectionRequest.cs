using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    /// <summary>
    /// Staff asked to change an imported punch. Admin approves or returns it from the bell/notification page.
    /// Proposed times sit here until approved, then they are copied onto AttendanceRecord.
    /// </summary>
    public class AttendanceCorrectionRequest
    {
        /// <summary>Database primary key for this correction request.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AttendanceCorrectionRequestId { get; set; }

        /// <summary>FK to the AttendanceRecord the staff want to change (cascade-delete with the row).</summary>
        public int AttendanceRecordId { get; set; }

        /// <summary>Navigation to the punch row; copied onto the record if Admin approves.</summary>
        public AttendanceRecord? Record { get; set; }

        /// <summary>Project the punch belongs to (NoAction delete so the request can still be listed).</summary>
        public int ProjectId { get; set; }

        /// <summary>Navigation to that project; shown as corrProject in the Admin modal.</summary>
        public Project? Project { get; set; }

        /// <summary>Employee FK copied from the record for display (may be null if unmatched).</summary>
        public int? EmployeeId { get; set; }

        /// <summary>Worker name shown on the Admin correction modal.</summary>
        [MaxLength(150)]
        public string EmployeeName { get; set; } = string.Empty;

        /// <summary>Session FullName of the staff member who submitted the request.</summary>
        [MaxLength(150)]
        public string PayrollStaffName { get; set; } = string.Empty;

        /// <summary>Work date of the punch being corrected.</summary>
        public DateTime? WorkDate { get; set; }

        /// <summary>Proposed morning (or first) clock-in.</summary>
        [MaxLength(40)]
        public string? TimeIn1 { get; set; }

        /// <summary>Proposed morning (or first) clock-out.</summary>
        [MaxLength(40)]
        public string? TimeOut1 { get; set; }

        /// <summary>Proposed afternoon (or second) clock-in.</summary>
        [MaxLength(40)]
        public string? TimeIn2 { get; set; }

        /// <summary>Proposed afternoon (or second) clock-out.</summary>
        [MaxLength(40)]
        public string? TimeOut2 { get; set; }

        /// <summary>Proposed overtime clock-in.</summary>
        [MaxLength(40)]
        public string? OvertimeIn { get; set; }

        /// <summary>Proposed overtime clock-out.</summary>
        [MaxLength(40)]
        public string? OvertimeOut { get; set; }

        /// <summary>Why staff need the change; required when they submit RequestCorrection.</summary>
        [MaxLength(250)]
        public string Reason { get; set; } = string.Empty;

        /// <summary>Pending, Approved, or Returned — see CorrectionRequestStatuses.</summary>
        [MaxLength(20)]
        public string Status { get; set; } = CorrectionRequestStatuses.Pending;

        /// <summary>When staff submitted the request (newest first on the bell list).</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>When Admin approved or returned it; null while still Pending.</summary>
        public DateTime? ReviewedAt { get; set; }

        /// <summary>Admin's reason when Status is Returned; shown to staff.</summary>
        [MaxLength(250)]
        public string? ReturnReason { get; set; }
    }

    /// <summary>AttendanceCorrectionRequest.Status values for the Admin approve/return flow.</summary>
    public static class CorrectionRequestStatuses
    {
        /// <summary>Waiting for Admin; Approve/Return buttons show on the notification modal.</summary>
        public const string Pending = "Pending";

        /// <summary>Admin accepted; proposed times were copied onto AttendanceRecord.</summary>
        public const string Approved = "Approved";

        /// <summary>Admin sent it back; staff see ReturnReason and may submit again.</summary>
        public const string Returned = "Returned";
    }
}
