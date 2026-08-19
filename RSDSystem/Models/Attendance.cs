using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    public class AttendanceImport
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AttendanceImportId { get; set; }

        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        [MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Source { get; set; } = AttendanceImportSources.Manual;

        [MaxLength(30)]
        public string Format { get; set; } = AttendanceFormats.Daily;

        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }

        [MaxLength(150)]
        public string? ImportedBy { get; set; }

        public DateTime ImportedAt { get; set; } = DateTime.Now;

        public int RowCount { get; set; }

        public ICollection<AttendanceRecord> Records { get; set; } = new List<AttendanceRecord>();
    }

    public class AttendanceRecord
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AttendanceRecordId { get; set; }

        public int AttendanceImportId { get; set; }
        public AttendanceImport? Import { get; set; }

        public int ProjectId { get; set; }

        public int? EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        [MaxLength(40)]
        public string ExternalUserId { get; set; } = string.Empty;

        [MaxLength(150)]
        public string EmployeeName { get; set; } = string.Empty;

        public DateTime? WorkDate { get; set; }
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }

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

        [Column(TypeName = "decimal(10,2)")]
        public decimal WorkHoursNormal { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal WorkHoursActual { get; set; }

        public int LateMinutes { get; set; }
        public int EarlyMinutes { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal OvertimeHours { get; set; }

        [Column(TypeName = "decimal(10,2)")]
        public decimal AbsenceDays { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = AttendanceStatuses.Incomplete;

        public bool Matched { get; set; }
    }

    public static class AttendanceImportSources
    {
        public const string Manual = "Manual";
        public const string N8n = "n8n";
    }

    public static class AttendanceFormats
    {
        public const string Statistic = "Statistic";
        public const string Daily = "Daily";
    }

    public static class AttendanceStatuses
    {
        public const string Complete = "Complete";
        public const string Incomplete = "Incomplete";
        public const string Late = "Late";
        public const string Absent = "Absent";

        public static readonly string[] All = { Complete, Incomplete, Late, Absent };

        public static string CssClass(string? status) => (status ?? "").Trim() switch
        {
            Complete => "att-status-complete",
            Incomplete => "att-status-incomplete",
            Late => "att-status-late",
            Absent => "att-status-absent",
            _ => "att-status-incomplete"
        };
    }
}
