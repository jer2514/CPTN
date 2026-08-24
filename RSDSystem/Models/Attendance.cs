using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    /// <summary>
    /// One Excel/CSV upload for a project. Child AttendanceRecord rows are the daily punches.
    /// Preview does not write this yet; ImportFile does.
    /// </summary>
    public class AttendanceImport
    {
        /// <summary>Database primary key for this upload batch.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AttendanceImportId { get; set; }

        /// <summary>FK to the project these punches belong to (cascade-delete with the job).</summary>
        public int ProjectId { get; set; }

        /// <summary>Navigation to that project (used when listing periods on Records/Summary).</summary>
        public Project? Project { get; set; }

        /// <summary>Original Excel/CSV file name shown in import preview and records meta.</summary>
        [MaxLength(260)]
        public string FileName { get; set; } = string.Empty;

        /// <summary>Who uploaded it: Manual (staff UI) or n8n (API); see AttendanceImportSources.</summary>
        [MaxLength(20)]
        public string Source { get; set; } = AttendanceImportSources.Manual;

        /// <summary>Parsed layout: Daily punches or Statistic totals; see AttendanceFormats.</summary>
        [MaxLength(30)]
        public string Format { get; set; } = AttendanceFormats.Daily;

        /// <summary>Earliest work date in this batch (period dropdown start).</summary>
        public DateTime? PeriodStart { get; set; }

        /// <summary>Latest work date in this batch (period dropdown end).</summary>
        public DateTime? PeriodEnd { get; set; }

        /// <summary>Session FullName of the staff member (or API user) who imported the file.</summary>
        [MaxLength(150)]
        public string? ImportedBy { get; set; }

        public DateTime ImportedAt { get; set; } = Helpers.PhilippinesTime.Now;

        /// <summary>How many AttendanceRecord rows were created from the file.</summary>
        public int RowCount { get; set; }

        /// <summary>Daily punch rows that belong to this upload.</summary>
        public ICollection<AttendanceRecord> Records { get; set; } = new List<AttendanceRecord>();
    }

    /// <summary>
    /// One person's punches for one calendar day. AttendanceRules.Apply() fills hours and Status
    /// (Complete, Late, Half-day, Absent, …) from TimeIn/TimeOut fields.
    /// </summary>
    public class AttendanceRecord
    {
        /// <summary>Database primary key for this daily punch row.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AttendanceRecordId { get; set; }

        /// <summary>FK to the parent AttendanceImport batch (cascade-delete with the import).</summary>
        public int AttendanceImportId { get; set; }

        /// <summary>Navigation to the upload this row came from.</summary>
        public AttendanceImport? Import { get; set; }

        /// <summary>Copied project id so Records/Summary can filter without joining Import.</summary>
        public int ProjectId { get; set; }

        /// <summary>Matched employee FK; null until staff pick a name for an unmatched import row.</summary>
        public int? EmployeeId { get; set; }

        /// <summary>Navigation to the matched Employee (optional; NoAction on employee delete).</summary>
        public Employee? Employee { get; set; }

        /// <summary>ID from the Excel/CSV (biometric user id) before or instead of EmployeeCode.</summary>
        [MaxLength(40)]
        public string ExternalUserId { get; set; } = string.Empty;

        /// <summary>Name from the file (or matched employee) shown on Records and correction requests.</summary>
        [MaxLength(150)]
        public string EmployeeName { get; set; } = string.Empty;

        /// <summary>Calendar day of these punches.</summary>
        public DateTime? WorkDate { get; set; }

        /// <summary>Optional statistic-format period start copied from the file.</summary>
        public DateTime? PeriodStart { get; set; }

        /// <summary>Optional statistic-format period end copied from the file.</summary>
        public DateTime? PeriodEnd { get; set; }

        /// <summary>Morning (or first) clock-in time as a string (HH:mm) from the file or editor.</summary>
        [MaxLength(40)]
        public string? TimeIn1 { get; set; }

        /// <summary>Morning (or first) clock-out time as a string (HH:mm).</summary>
        [MaxLength(40)]
        public string? TimeOut1 { get; set; }

        /// <summary>Afternoon (or second) clock-in time as a string (HH:mm).</summary>
        [MaxLength(40)]
        public string? TimeIn2 { get; set; }

        /// <summary>Afternoon (or second) clock-out time as a string (HH:mm).</summary>
        [MaxLength(40)]
        public string? TimeOut2 { get; set; }

        /// <summary>Overtime clock-in time as a string (HH:mm).</summary>
        [MaxLength(40)]
        public string? OvertimeIn { get; set; }

        /// <summary>Overtime clock-out time as a string (HH:mm).</summary>
        [MaxLength(40)]
        public string? OvertimeOut { get; set; }

        /// <summary>Scheduled/normal hours for the day (usually 8) after AttendanceRules.Apply().</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal WorkHoursNormal { get; set; }

        /// <summary>Hours actually worked from the punches (feeds payroll days/hours).</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal WorkHoursActual { get; set; }

        /// <summary>Minutes past the expected clock-in; Late status when this is &gt; 0.</summary>
        public int LateMinutes { get; set; }

        /// <summary>Minutes before the expected clock-out; Early Off status when this is &gt; 0.</summary>
        public int EarlyMinutes { get; set; }

        /// <summary>OT hours from OvertimeIn/Out; copied onto the payslip OvertimeHours.</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal OvertimeHours { get; set; }

        /// <summary>1 when Status is Absent; 0 otherwise (used in period totals).</summary>
        [Column(TypeName = "decimal(10,2)")]
        public decimal AbsenceDays { get; set; }

        /// <summary>Complete / Half-day / Late / Early Off / Late + Early Off / Absent.</summary>
        [MaxLength(20)]
        public string Status { get; set; } = AttendanceStatuses.HalfDay;

        /// <summary>True when ExternalUserId/name was matched to an Employee on import.</summary>
        public bool Matched { get; set; }
    }

    /// <summary>AttendanceImport.Source values: staff UI vs n8n API.</summary>
    public static class AttendanceImportSources
    {
        /// <summary>Uploaded from Attendance/Import in the browser.</summary>
        public const string Manual = "Manual";

        /// <summary>Pushed by the n8n/AttendanceApi import endpoint.</summary>
        public const string N8n = "n8n";
    }

    /// <summary>AttendanceImport.Format values for how the Excel/CSV was parsed.</summary>
    public static class AttendanceFormats
    {
        /// <summary>File had period totals instead of per-day punches.</summary>
        public const string Statistic = "Statistic";

        /// <summary>File had one row per person per day (Time In/Out columns).</summary>
        public const string Daily = "Daily";
    }

    /// <summary>AttendanceRecord.Status labels plus helpers for filters, badges, and payroll counts.</summary>
    public static class AttendanceStatuses
    {
        /// <summary>Full day, on time; staff cannot edit without an Admin-reviewed correction.</summary>
        public const string Complete = "Complete";

        /// <summary>Worked only part of the day (also matches Incomplete / "Half Day" aliases).</summary>
        public const string HalfDay = "Half-day";

        /// <summary>Legacy alias treated as Half-day by IsHalfDay/Display.</summary>
        public const string Incomplete = "Incomplete";

        /// <summary>Clocked in late (LateMinutes &gt; 0) but finished the day.</summary>
        public const string Late = "Late";

        /// <summary>Left before the expected clock-out (EarlyMinutes &gt; 0).</summary>
        public const string EarlyOff = "Early Off";

        /// <summary>Both late and left early on the same day.</summary>
        public const string LateEarlyOff = "Late + Early Off";

        /// <summary>No punches / did not work; counts as an absent day on the slip.</summary>
        public const string Absent = "Absent";

        /// <summary>Status values shown in the Records/Summary filter dropdown (Incomplete omitted).</summary>
        public static readonly string[] All = { Complete, HalfDay, Late, EarlyOff, LateEarlyOff, Absent };

        /// <summary>True for Half-day and its aliases (Incomplete, "Half Day", "Half-Day").</summary>
        public static bool IsHalfDay(string? status) =>
            string.Equals(status, HalfDay, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, Incomplete, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Half Day", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Half-Day", StringComparison.OrdinalIgnoreCase);

        /// <summary>Normalizes Incomplete/aliases to "Half-day" for table badges.</summary>
        public static string Display(string? status) =>
            IsHalfDay(status) ? HalfDay : (status ?? "");

        public static bool CountsAsFullDay(string? status) =>
            status is Complete or Late or EarlyOff or LateEarlyOff;

        public static bool CountsAsWorked(string? status) =>
            CountsAsFullDay(status) || IsHalfDay(status);

        /// <summary>True for Late or Late + Early Off; used by the Late filter and Summary chips.</summary>
        public static bool CountsAsLate(string? status) =>
            status is Late or LateEarlyOff;

        /// <summary>True for Early Off or Late + Early Off; used by the Early Off filter.</summary>
        public static bool CountsAsEarly(string? status) =>
            status is EarlyOff or LateEarlyOff;

        /// <summary>Whether a row should show when the Records filter dropdown is set to this status.</summary>
        public static bool MatchesFilter(string? status, string? filter)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(filter, Late, StringComparison.OrdinalIgnoreCase))
                return CountsAsLate(status);

            if (string.Equals(filter, EarlyOff, StringComparison.OrdinalIgnoreCase))
                return CountsAsEarly(status);

            if (IsHalfDay(filter))
                return IsHalfDay(status);

            return string.Equals(status, filter, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>CSS class for the colored status pill on Records/Summary tables.</summary>
        public static string CssClass(string? status)
        {
            var value = Display(status);
            return value switch
            {
                Complete => "att-status-complete",
                HalfDay => "att-status-halfday",
                Late => "att-status-late",
                EarlyOff => "att-status-early",
                LateEarlyOff => "att-status-late",
                Absent => "att-status-absent",
                _ => "att-status-halfday"
            };
        }
    }
}
