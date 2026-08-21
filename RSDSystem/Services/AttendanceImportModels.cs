using RSDSystem.Models;

namespace RSDSystem.Services
{
    public class AttendancePreviewResult
    {
        public string? Error { get; set; }
        public Project? Project { get; set; }
        public string FileName { get; set; } = "";
        public string Format { get; set; } = AttendanceFormats.Daily;
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public List<AttendancePreviewRow> Rows { get; set; } = new();
        public List<Employee> CandidateEmployees { get; set; } = new();
        public int MatchedCount { get; set; }
        public int UnmatchedCount { get; set; }
    }

    public class AttendancePreviewRow
    {
        public int? EmployeeId { get; set; }
        public string DisplayId { get; set; } = "";
        public string ExternalUserId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string? MatchedEmployeeName { get; set; }
        public DateTime? WorkDate { get; set; }
        public string? TimeIn1 { get; set; }
        public string? TimeOut1 { get; set; }
        public string? TimeIn2 { get; set; }
        public string? TimeOut2 { get; set; }
        public string? OvertimeIn { get; set; }
        public string? OvertimeOut { get; set; }
        public decimal WorkHoursNormal { get; set; }
        public decimal WorkHoursActual { get; set; }
        public int LateMinutes { get; set; }
        public int EarlyMinutes { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal AbsenceDays { get; set; }
        public string Status { get; set; } = AttendanceStatuses.HalfDay;
        public bool Matched { get; set; }
        public string? Note { get; set; }
    }

    public class AttendanceDayOverride
    {
        public string? ExternalUserId { get; set; }
        public string? EmployeeName { get; set; }
        public DateTime WorkDate { get; set; }
        public string? TimeIn1 { get; set; }
        public string? TimeOut1 { get; set; }
        public string? TimeIn2 { get; set; }
        public string? TimeOut2 { get; set; }
        public string? OvertimeIn { get; set; }
        public string? OvertimeOut { get; set; }
    }

    public class AttendanceManualMatch
    {
        public string? ExternalUserId { get; set; }
        public string? EmployeeName { get; set; }
        public DateTime WorkDate { get; set; }
        public int EmployeeId { get; set; }
    }

    public class AttendanceImportResult
    {
        public string? Error { get; set; }
        public int ImportId { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Format { get; set; } = "";
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public int RowCount { get; set; }
        public int MatchedCount { get; set; }
        public int UnmatchedCount { get; set; }
        public bool ReplacedPrevious { get; set; }
        public int SkippedLockedCount { get; set; }
    }

    public class AttendancePeriodOption
    {
        public string Key { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Label { get; set; } = "";
        public string? ImportedBy { get; set; }
    }

    public class AttendanceEmployeeSummary
    {
        public int? EmployeeId { get; set; }
        public string DisplayId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public bool Matched { get; set; }
        public int DaysWorked { get; set; }
        public int DaysPresent { get; set; }
        public int DaysAbsent { get; set; }
        public int DaysLate { get; set; }
        public int DaysIncomplete { get; set; }
        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }
    }

    public class AttendanceSummaryResult
    {
        public List<AttendanceEmployeeSummary> Rows { get; set; } = new();
        public int Total { get; set; }
        public int DaysWorked { get; set; }
        public int DaysAbsent { get; set; }
        public int DaysLate { get; set; }
        public int DaysIncomplete { get; set; }
        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }
        public int UnmatchedCount { get; set; }
        public string? ImportedBy { get; set; }
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
    }
}
