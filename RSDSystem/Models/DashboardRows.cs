namespace RSDSystem.Models
{
    /// <summary>Small objects the Admin dashboard (Home/Index) sends to the view.</summary>
    public class ProjectDateBounds
    {
        /// <summary>ISO date string for the project's StartingDate (min bound for a new schedule).</summary>
        public string Start { get; set; } = "";

        /// <summary>ISO date string for the project's EstimateEndDate (max bound for a new schedule).</summary>
        public string End { get; set; } = "";
    }

    /// <summary>One Submitted payroll waiting on the Admin dashboard pending-approval list.</summary>
    public class PendingApprovalRow
    {
        /// <summary>Project to open in ReviewProject when Admin clicks the row.</summary>
        public int ProjectId { get; set; }

        /// <summary>Assigned payroll staff name who submitted the slips.</summary>
        public string? StaffName { get; set; }

        /// <summary>Project name shown on the pending-approval card.</summary>
        public string ProjectName { get; set; } = "—";

        /// <summary>When the latest Submitted slip for that project was generated.</summary>
        public DateTime Date { get; set; }
    }

    /// <summary>One existing payroll-schedule range shown as a chip on the dashboard calendar.</summary>
    public class PayPeriodRange
    {
        /// <summary>ISO start date of an existing PayrollSchedule.</summary>
        public string Start { get; set; } = "";

        /// <summary>ISO end date of an existing PayrollSchedule.</summary>
        public string End { get; set; } = "";
    }
}
