namespace RSDSystem.Models
{
    public class ProjectDateBounds
    {
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
    }

    public class PendingApprovalRow
    {
        public int ProjectId { get; set; }
        public int? PayrollScheduleId { get; set; }
        public string? StaffName { get; set; }
        public string ProjectName { get; set; } = "—";
        public DateTime Date { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
    }

    public class PendingCorrectionRow
    {
        public int CorrectionId { get; set; }
        public string StaffName { get; set; } = "Payroll Staff";
        public string ProjectName { get; set; } = "—";
        public string EmployeeName { get; set; } = "—";
        public DateTime? WorkDate { get; set; }
    }

    public class PayPeriodRange
    {
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
    }
}
