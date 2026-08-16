namespace RSDSystem.Models
{
    public class AttendanceMonthEdit
    {
        public int FocusRecordId { get; set; }
        public int AttendanceImportId { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public int? EmployeeId { get; set; }
        public string ExternalUserId { get; set; } = "";
        public string DisplayId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string MonthLabel { get; set; } = "";
        public DateTime MonthStart { get; set; }
        public bool Matched { get; set; }
        public int DaysWorked { get; set; }
        public int DaysAbsent { get; set; }
        public int DaysLate { get; set; }
        public int DaysIncomplete { get; set; }
        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }
        public List<AttendanceDayEdit> Days { get; set; } = new();
    }

    public class AttendanceDayEdit
    {
        public int RecordId { get; set; }
        public DateTime WorkDate { get; set; }
        public string? TimeIn1 { get; set; }
        public string? TimeOut1 { get; set; }
        public string? TimeIn2 { get; set; }
        public string? TimeOut2 { get; set; }
        public string? OvertimeIn { get; set; }
        public string? OvertimeOut { get; set; }
        public string Status { get; set; } = AttendanceStatuses.Absent;
        public decimal RegularHours { get; set; }
        public decimal OvertimeHours { get; set; }
    }
}
