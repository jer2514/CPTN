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
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AttendanceCorrectionRequestId { get; set; }

        public int AttendanceRecordId { get; set; }
        public AttendanceRecord? Record { get; set; }

        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        public int? EmployeeId { get; set; }

        [MaxLength(150)]
        public string EmployeeName { get; set; } = string.Empty;

        [MaxLength(150)]
        public string PayrollStaffName { get; set; } = string.Empty;

        public DateTime? WorkDate { get; set; }

        [MaxLength(40)]
        public string? TimeIn1 { get; set; }
        [MaxLength(40)]
        public string? TimeOut1 { get; set; }
        [MaxLength(40)]
        public string? TimeIn2 { get; set; }
        [MaxLength(40)]
        public string? TimeOut2 { get; set; }
        [MaxLength(40)]
        public string? OvertimeIn { get; set; }
        [MaxLength(40)]
        public string? OvertimeOut { get; set; }

        [MaxLength(250)]
        public string Reason { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Status { get; set; } = CorrectionRequestStatuses.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ReviewedAt { get; set; }

        [MaxLength(250)]
        public string? ReturnReason { get; set; }
    }

    public static class CorrectionRequestStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Returned = "Returned";
    }
}
