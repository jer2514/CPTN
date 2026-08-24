using RSDSystem.Models;

namespace RSDSystem.Services
{
    /// <summary>
    /// Preview DTOs: parsed rows plus which employee they matched (not database tables).
    /// </summary>
    public class AttendancePreviewResult
    {
        /// <summary>Error shown on the import preview when the project or file cannot be used; null on success.</summary>
        public string? Error { get; set; }
        /// <summary>Project the file is being imported into (after Load / resolve by name).</summary>
        public Project? Project { get; set; }
        /// <summary>Uploaded file name shown on the preview screen.</summary>
        public string FileName { get; set; } = "";
        /// <summary>Daily (punch times) or Statistic (pre-totaled hours) layout detected from headers.</summary>
        public string Format { get; set; } = AttendanceFormats.Daily;
        /// <summary>First day of the attendance window from the file or the overlapping payroll schedule.</summary>
        public DateTime? PeriodStart { get; set; }
        /// <summary>Last day of the attendance window used to expand missing days as Absent.</summary>
        public DateTime? PeriodEnd { get; set; }
        /// <summary>One preview row per employee per work date, including unmatched biometric names.</summary>
        public List<AttendancePreviewRow> Rows { get; set; } = new();
        /// <summary>Employees in the project (or all employees) offered in the manual-match dropdown.</summary>
        public List<Employee> CandidateEmployees { get; set; } = new();
        /// <summary>How many rows already linked to a system EmployeeId.</summary>
        public int MatchedCount { get; set; }
        /// <summary>How many rows still need a manual employee pick before import.</summary>
        public int UnmatchedCount { get; set; }
    }

    /// <summary>One parsed punch day on the import preview, before it is saved as AttendanceRecord.</summary>
    public class AttendancePreviewRow
    {
        /// <summary>Matched Employees.EmployeeId, or null until staff pick someone in the dropdown.</summary>
        public int? EmployeeId { get; set; }
        /// <summary>Raw biometric User ID from the file, shown in the preview ID column.</summary>
        public string DisplayId { get; set; } = "";
        /// <summary>External/biometric user id used to match and to find the row again after edits.</summary>
        public string ExternalUserId { get; set; } = "";
        /// <summary>Name printed on the time card or CSV, kept even when unmatched.</summary>
        public string EmployeeName { get; set; } = "";
        /// <summary>System FullName after auto or manual match; stored on import instead of the file name.</summary>
        public string? MatchedEmployeeName { get; set; }
        /// <summary>Calendar work date for this punch row.</summary>
        public DateTime? WorkDate { get; set; }
        /// <summary>Morning time in (first session).</summary>
        public string? TimeIn1 { get; set; }
        /// <summary>Morning time out (first session).</summary>
        public string? TimeOut1 { get; set; }
        /// <summary>Afternoon time in (second session).</summary>
        public string? TimeIn2 { get; set; }
        /// <summary>Afternoon time out (second session).</summary>
        public string? TimeOut2 { get; set; }
        /// <summary>Overtime in punch after 17:00 when the card has an OT column.</summary>
        public string? OvertimeIn { get; set; }
        /// <summary>Overtime out punch for the OT column.</summary>
        public string? OvertimeOut { get; set; }
        /// <summary>Scheduled/normal hours from statistic files; daily files often leave this 0.</summary>
        public decimal WorkHoursNormal { get; set; }
        /// <summary>Actual regular hours after AttendanceRules.Apply (shift windows only).</summary>
        public decimal WorkHoursActual { get; set; }
        /// <summary>Minutes late past 8:00 after the 30-minute grace.</summary>
        public int LateMinutes { get; set; }
        /// <summary>Minutes early before 17:00 after the 15-minute grace.</summary>
        public int EarlyMinutes { get; set; }
        /// <summary>Hours after 17:00 from OT punches or late regular punches.</summary>
        public decimal OvertimeHours { get; set; }
        /// <summary>Absence days from statistic files; daily missing days are expanded as Absent instead.</summary>
        public decimal AbsenceDays { get; set; }
        /// <summary>Complete / Late / Half-day / Absent / etc. from AttendanceRules, shown as a badge.</summary>
        public string Status { get; set; } = AttendanceStatuses.HalfDay;
        /// <summary>True when EmployeeId is set so import can link the row to payroll later.</summary>
        public bool Matched { get; set; }
        /// <summary>Hint on unmatched rows telling staff to pick the correct employee.</summary>
        public string? Note { get; set; }
    }

    /// <summary>Punch-time edits made on the preview grid and posted back as JSON with ImportFile.</summary>
    public class AttendanceDayOverride
    {
        /// <summary>Biometric id used with WorkDate to find the preview row to patch.</summary>
        public string? ExternalUserId { get; set; }
        /// <summary>File name used as a fallback key when ExternalUserId is blank.</summary>
        public string? EmployeeName { get; set; }
        /// <summary>Work date of the edited day.</summary>
        public DateTime WorkDate { get; set; }
        /// <summary>Replacement morning in, or blank to clear the punch.</summary>
        public string? TimeIn1 { get; set; }
        /// <summary>Replacement morning out.</summary>
        public string? TimeOut1 { get; set; }
        /// <summary>Replacement afternoon in.</summary>
        public string? TimeIn2 { get; set; }
        /// <summary>Replacement afternoon out.</summary>
        public string? TimeOut2 { get; set; }
        /// <summary>Replacement overtime in.</summary>
        public string? OvertimeIn { get; set; }
        /// <summary>Replacement overtime out.</summary>
        public string? OvertimeOut { get; set; }
    }

    /// <summary>Staff choice from the preview "Select employee" dropdown for a previously unmatched row.</summary>
    public class AttendanceManualMatch
    {
        /// <summary>Biometric id of the unmatched row.</summary>
        public string? ExternalUserId { get; set; }
        /// <summary>File name of the unmatched row.</summary>
        public string? EmployeeName { get; set; }
        /// <summary>Work date of that row (one match per person per day).</summary>
        public DateTime WorkDate { get; set; }
        /// <summary>Employees.EmployeeId the staff member selected.</summary>
        public int EmployeeId { get; set; }
    }

    /// <summary>Result returned to AttendanceController / AttendanceApi after a file is saved (or fails).</summary>
    public class AttendanceImportResult
    {
        /// <summary>User-facing save error; null when the batch was written.</summary>
        public string? Error { get; set; }
        /// <summary>New AttendanceImports primary key.</summary>
        public int ImportId { get; set; }
        /// <summary>Project the rows were saved under.</summary>
        public int ProjectId { get; set; }
        /// <summary>Project name for the success toast.</summary>
        public string ProjectName { get; set; } = "";
        /// <summary>Stored file name on the import header.</summary>
        public string FileName { get; set; } = "";
        /// <summary>Daily or Statistic format stored on the import header.</summary>
        public string Format { get; set; } = "";
        /// <summary>Period start written on the import header.</summary>
        public DateTime? PeriodStart { get; set; }
        /// <summary>Period end written on the import header.</summary>
        public DateTime? PeriodEnd { get; set; }
        /// <summary>How many AttendanceRecord rows were inserted.</summary>
        public int RowCount { get; set; }
        /// <summary>How many of those rows have EmployeeId set.</summary>
        public int MatchedCount { get; set; }
        /// <summary>How many rows were saved unmatched (name still from the file).</summary>
        public int UnmatchedCount { get; set; }
        /// <summary>True when an overlapping previous import for the same dates was deleted first.</summary>
        public bool ReplacedPrevious { get; set; }
        public int SkippedLockedCount { get; set; }
    }

    /// <summary>One distinct date range for the Attendance Records period dropdown.</summary>
    public class AttendancePeriodOption
    {
        /// <summary>Stable key <c>yyyy-MM-dd|yyyy-MM-dd</c> posted back as the selected period.</summary>
        public string Key { get; set; } = "";
        /// <summary>First day of this imported window.</summary>
        public DateTime Start { get; set; }
        /// <summary>Last day of this imported window.</summary>
        public DateTime End { get; set; }
        /// <summary>Long English dates shown in the dropdown.</summary>
        public string Label { get; set; } = "";
        /// <summary>Session name of the staff/admin who imported that batch.</summary>
        public string? ImportedBy { get; set; }
    }

    /// <summary>Per-employee totals for the Attendance Records summary tab and payroll GetAttendanceTotals.</summary>
    public class AttendanceEmployeeSummary
    {
        /// <summary>System employee id when matched; null for unmatched biometric rows.</summary>
        public int? EmployeeId { get; set; }
        /// <summary>Five-digit employee code (or biometric id) shown in the summary grid.</summary>
        public string DisplayId { get; set; } = "";
        /// <summary>System FullName when matched, otherwise the file name.</summary>
        public string EmployeeName { get; set; } = "";
        /// <summary>True if any day in the group is linked to an Employee.</summary>
        public bool Matched { get; set; }
        /// <summary>Days whose status counts as worked (Complete, Late, and similar).</summary>
        public int DaysWorked { get; set; }
        public int DaysPresent { get; set; }
        public int DaysAbsent { get; set; }
        /// <summary>Days with Late or Late/Early Off.</summary>
        public int DaysLate { get; set; }
        /// <summary>Half-day / incomplete punch days.</summary>
        public int DaysIncomplete { get; set; }
        /// <summary>Sum of regular hours in the period (feeds payroll generation).</summary>
        public decimal RegularHours { get; set; }
        /// <summary>Sum of overtime hours in the period (feeds payroll OT pay).</summary>
        public decimal OvertimeHours { get; set; }
    }

    /// <summary>Paged summary plus project-wide totals for one attendance period.</summary>
    public class AttendanceSummaryResult
    {
        /// <summary>Current page of employee summary rows.</summary>
        public List<AttendanceEmployeeSummary> Rows { get; set; } = new();
        /// <summary>Total employee groups before paging (for the pager).</summary>
        public int Total { get; set; }
        /// <summary>Sum of DaysWorked across all employees in the period.</summary>
        public int DaysWorked { get; set; }
        /// <summary>Sum of DaysAbsent across all employees.</summary>
        public int DaysAbsent { get; set; }
        /// <summary>Sum of DaysLate across all employees.</summary>
        public int DaysLate { get; set; }
        /// <summary>Sum of half-day / incomplete days.</summary>
        public int DaysIncomplete { get; set; }
        /// <summary>Sum of regular hours for the period header.</summary>
        public decimal RegularHours { get; set; }
        /// <summary>Sum of overtime hours for the period header.</summary>
        public decimal OvertimeHours { get; set; }
        /// <summary>How many employee groups are still unmatched.</summary>
        public int UnmatchedCount { get; set; }
        /// <summary>Who imported the batch, shown on the summary header.</summary>
        public string? ImportedBy { get; set; }
        /// <summary>Period start echoed back to the UI.</summary>
        public DateTime? PeriodStart { get; set; }
        /// <summary>Period end echoed back to the UI.</summary>
        public DateTime? PeriodEnd { get; set; }
    }
}
