namespace RSDSystem.Models
{
    /// <summary>View models for Admin View Payroll lists (not database tables).</summary>
    public class PayrollPeriodRow
    {
        /// <summary>Project whose approved slips make up this period card.</summary>
        public int ProjectId { get; set; }

        /// <summary>Project name shown on payroll/Index.</summary>
        public string ProjectName { get; set; } = "—";

        /// <summary>Pay period start (matches Payroll.PayPeriodStart of the grouped slips).</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Pay period end (matches Payroll.PayPeriodEnd of the grouped slips).</summary>
        public DateTime EndDate { get; set; }

        /// <summary>Assigned payroll staff name for that project.</summary>
        public string PayrollStaff { get; set; } = "—";
    }

    /// <summary>One employee line on payroll/Period (approved slips only).</summary>
    public class PayrollPeriodEmployeeRow
    {
        /// <summary>PayrollId to open in payroll/View; null if the row has no slip yet.</summary>
        public int? PayrollId { get; set; }

        /// <summary>Worker name on the period table.</summary>
        public string EmployeeName { get; set; } = "—";

        /// <summary>Job classification copied from Employee.</summary>
        public string Job { get; set; } = "—";

        /// <summary>Regular hours (days × typical day length) shown on the period table.</summary>
        public decimal RegularHours { get; set; }

        /// <summary>Overtime hours from the approved slip.</summary>
        public decimal OtHours { get; set; }

        /// <summary>Take-home pay from the approved slip.</summary>
        public decimal NetPay { get; set; }

        /// <summary>Status label (Approved on this screen).</summary>
        public string Status { get; set; } = "—";
    }
}
