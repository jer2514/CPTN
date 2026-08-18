namespace RSDSystem.Models
{
    public class PayrollPeriodRow
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "—";
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string PayrollStaff { get; set; } = "—";
    }

    public class PayrollPeriodEmployeeRow
    {
        public int? PayrollId { get; set; }
        public string EmployeeName { get; set; } = "—";
        public string Job { get; set; } = "—";
        public decimal RegularHours { get; set; }
        public decimal OtHours { get; set; }
        public decimal NetPay { get; set; }
        public string Status { get; set; } = "—";
    }
}
