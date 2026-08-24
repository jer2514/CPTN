namespace RSDSystem.Models
{
    /// <summary>Form models for editing a month of attendance in the UI (not a table).</summary>
    public class AttendanceMonthEdit
    {
        /// <summary>AttendanceRecordId that opened this screen (used to highlight that day).</summary>
        public int FocusRecordId { get; set; }

        /// <summary>Parent import batch these days belong to.</summary>
        public int AttendanceImportId { get; set; }

        /// <summary>Project id for Back-to-Records and payroll links.</summary>
        public int ProjectId { get; set; }

        /// <summary>Project name shown in the edit header.</summary>
        public string ProjectName { get; set; } = "";

        /// <summary>Matched employee id; when set, staff can jump to generate payroll.</summary>
        public int? EmployeeId { get; set; }

        /// <summary>Biometric/file user id shown when the row is not matched to an Employee.</summary>
        public string ExternalUserId { get; set; } = "";

        /// <summary>Formatted Employee ID (or external id) shown in the header.</summary>
        public string DisplayId { get; set; } = "";

        /// <summary>Worker name shown in the header.</summary>
        public string EmployeeName { get; set; } = "";

        /// <summary>Human-readable period label (e.g. month or schedule dates).</summary>
        public string MonthLabel { get; set; } = "";

        /// <summary>First day of the month/period being edited.</summary>
        public DateTime MonthStart { get; set; }

        /// <summary>True if the import row was matched to an Employee.</summary>
        public bool Matched { get; set; }

        /// <summary>Count of days that CountsAsWorked in this period.</summary>
        public int DaysWorked { get; set; }

        /// <summary>Count of Absent days in this period.</summary>
        public int DaysAbsent { get; set; }

        /// <summary>Count of Late / Late + Early Off days in this period.</summary>
        public int DaysLate { get; set; }

        /// <summary>Count of Half-day / Incomplete days in this period.</summary>
        public int DaysIncomplete { get; set; }

        /// <summary>Sum of regular (actual) hours across the month's days.</summary>
        public decimal RegularHours { get; set; }

        /// <summary>Sum of overtime hours across the month's days.</summary>
        public decimal OvertimeHours { get; set; }

        /// <summary>One AttendanceDayEdit per calendar day in the period.</summary>
        public List<AttendanceDayEdit> Days { get; set; } = new();
    }

    /// <summary>One day's punches on the attendance month-edit form (posted back per row).</summary>
    public class AttendanceDayEdit
    {
        /// <summary>AttendanceRecordId to POST to UpdateRecord / RequestCorrection.</summary>
        public int RecordId { get; set; }

        /// <summary>Calendar date of this row.</summary>
        public DateTime WorkDate { get; set; }

        /// <summary>Editable morning (or first) clock-in.</summary>
        public string? TimeIn1 { get; set; }

        /// <summary>Editable morning (or first) clock-out.</summary>
        public string? TimeOut1 { get; set; }

        /// <summary>Editable afternoon (or second) clock-in.</summary>
        public string? TimeIn2 { get; set; }

        /// <summary>Editable afternoon (or second) clock-out.</summary>
        public string? TimeOut2 { get; set; }

        /// <summary>Editable overtime clock-in.</summary>
        public string? OvertimeIn { get; set; }

        /// <summary>Editable overtime clock-out.</summary>
        public string? OvertimeOut { get; set; }

        /// <summary>Current AttendanceStatuses value; recalculated when punches change.</summary>
        public string Status { get; set; } = AttendanceStatuses.Absent;

        /// <summary>Regular hours for this day after rules run.</summary>
        public decimal RegularHours { get; set; }

        /// <summary>Overtime hours for this day after rules run.</summary>
        public decimal OvertimeHours { get; set; }
    }
}
