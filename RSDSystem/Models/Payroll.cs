using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    public class Payroll
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PayrollId { get; set; }

        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        public DateTime PayPeriodStart { get; set; }
        public DateTime PayPeriodEnd { get; set; }

        public int RegularDaysWorked { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal OvertimeHours { get; set; }

        public int AbsentDays { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal RegularPay { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal OvertimePay { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal GrossPay { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal CashAdvance { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal NetPay { get; set; }

        // Draft | Correction | Submitted | Approved
        [MaxLength(20)]
        public string Status { get; set; } = "Draft";

        [MaxLength(150)]
        public string? GeneratedBy { get; set; }

        public DateTime GeneratedDate { get; set; } = DateTime.Now;

        [MaxLength(500)]
        public string? CorrectionReason { get; set; }
    }

    public static class PayrollStatusOptions
    {
        public const string Draft = "Draft";
        public const string Correction = "Correction";
        public const string Submitted = "Submitted";
        public const string Approved = "Approved";

        // Correction first, then Draft, then Submitted, then anything else
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