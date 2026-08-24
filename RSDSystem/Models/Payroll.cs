using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    /// <summary>
    /// One employee's payslip for one period/schedule.
    ///
    /// Status flow:
    ///   Draft → (staff Submit) Submitted → (admin Approve) Approved
    ///                         ↘ (admin Return) Correction → (staff Submit again) Submitted
    ///
    /// Amounts: RegularPay + OvertimePay = GrossPay; GrossPay - CashAdvance = NetPay.
    /// Days/hours come from imported attendance when the slip is generated.
    /// </summary>
    public class Payroll
    {
        /// <summary>Database primary key for this payslip row.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PayrollId { get; set; }

        /// <summary>FK to the employee this slip is for (Restrict delete).</summary>
        public int EmployeeId { get; set; }

        /// <summary>Navigation to the employee whose rates and name appear on the slip.</summary>
        public Employee? Employee { get; set; }

        /// <summary>FK to the project this pay period belongs to (Restrict delete).</summary>
        public int ProjectId { get; set; }

        /// <summary>Navigation to the project shown as "Project Assigned" on the slip.</summary>
        public Project? Project { get; set; }

        /// <summary>Optional FK to the Admin-created PayrollSchedule this slip was generated under.</summary>
        public int? PayrollScheduleId { get; set; }

        /// <summary>Navigation to that schedule; unique with EmployeeId so one slip per worker per period.</summary>
        public PayrollSchedule? PayrollSchedule { get; set; }

        /// <summary>First calendar day of the pay period (inclusive).</summary>
        public DateTime PayPeriodStart { get; set; }

        /// <summary>Last calendar day of the pay period (inclusive).</summary>
        public DateTime PayPeriodEnd { get; set; }

        /// <summary>Present days counted from attendance; multiplied by DailyRate for RegularPay.</summary>
        public int RegularDaysWorked { get; set; }

        /// <summary>OT hours from attendance; multiplied by RatePerHour for OvertimePay.</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal OvertimeHours { get; set; }

        /// <summary>Days marked Absent in the same period (does not add to pay).</summary>
        public int AbsentDays { get; set; }

        /// <summary>DailyRate × RegularDaysWorked.</summary>
        [Column(TypeName = "decimal(12,2)")]
        public decimal RegularPay { get; set; }

        /// <summary>RatePerHour × OvertimeHours.</summary>
        [Column(TypeName = "decimal(12,2)")]
        public decimal OvertimePay { get; set; }

        /// <summary>RegularPay + OvertimePay before cash-advance deduction.</summary>
        [Column(TypeName = "decimal(12,2)")]
        public decimal GrossPay { get; set; }

        /// <summary>Amount deducted from GrossPay; cannot exceed GrossPay on the slip form.</summary>
        [Column(TypeName = "decimal(12,2)")]
        public decimal CashAdvance { get; set; }

        /// <summary>GrossPay − CashAdvance; take-home shown on the printed slip.</summary>
        [Column(TypeName = "decimal(12,2)")]
        public decimal NetPay { get; set; }

        /// <summary>Draft, Correction, Submitted, or Approved — see PayrollStatusOptions.</summary>
        [MaxLength(20)]
        public string Status { get; set; } = "Draft";

        /// <summary>Session FullName of the staff member who generated or last saved this slip.</summary>
        [MaxLength(150)]
        public string? GeneratedBy { get; set; }

        /// <summary>When the slip was first created (or last regenerated).</summary>
        public DateTime GeneratedDate { get; set; } = DateTime.Now;

        /// <summary>Admin Return reason; shown to staff on Correction slips so they know what to fix.</summary>
        [MaxLength(500)]
        public string? CorrectionReason { get; set; }
    }

    /// <summary>Payroll.Status constants and list-sort order (Correction first, then Draft, Submitted, Approved).</summary>
    public static class PayrollStatusOptions
    {
        /// <summary>Staff just generated the slip; can still edit or delete.</summary>
        public const string Draft = "Draft";

        /// <summary>Admin returned it; staff must edit and Submit again.</summary>
        public const string Correction = "Correction";

        /// <summary>Staff submitted; waiting for Admin Approve or Return.</summary>
        public const string Submitted = "Submitted";

        /// <summary>Admin approved; locked for staff and counted in View Payroll / prediction.</summary>
        public const string Approved = "Approved";

        /// <summary>Sort key so pending-correction slips appear first on staff lists.</summary>
        public static int SortRank(string status) => status switch
        {
            Correction => 0,
            Draft => 1,
            Submitted => 2,
            Approved => 3,
            _ => 4
        };
    }
}
