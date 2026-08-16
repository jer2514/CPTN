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
        public string? StaffName { get; set; }
        public string ProjectName { get; set; } = "—";
        public DateTime Date { get; set; }
    }

    public class PayPeriodRange
    {
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
    }
}
