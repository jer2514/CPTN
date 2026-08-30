using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    public class CashAdvance
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int CashAdvanceId { get; set; }

        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        public int EmployeeId { get; set; }
        public Employee? Employee { get; set; }

        public DateTime AdvanceDate { get; set; }

        [Column(TypeName = "decimal(12,2)")]
        public decimal Amount { get; set; }

        [MaxLength(400)]
        public string? Reason { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = CashAdvanceStatuses.Outstanding;

        public DateTime CreatedAt { get; set; } = Helpers.PhilippinesTime.Now;

        [MaxLength(150)]
        public string? CreatedBy { get; set; }

        public DateTime? MarkedAt { get; set; }

        [MaxLength(150)]
        public string? MarkedBy { get; set; }

        public int? PayrollId { get; set; }

        public DateTime? DeductedAt { get; set; }
    }

    public static class CashAdvanceStatuses
    {
        public const string Outstanding = "Outstanding";
        public const string Pending = "Pending";
        public const string Deducted = "Deducted";

        public static bool IsUnpaid(string? status) =>
            string.Equals(status, Outstanding, StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, Pending, StringComparison.OrdinalIgnoreCase);

        public static bool IsPaid(string? status) =>
            string.Equals(status, Deducted, StringComparison.OrdinalIgnoreCase);

        public static string Display(string? status) => (status ?? "").Trim() switch
        {
            Outstanding => "Unpaid",
            Pending => "Unpaid",
            Deducted => "Paid",
            _ => status ?? "—"
        };
    }

    public class CashAdvanceEmployeeRow
    {
        public int EmployeeId { get; set; }
        public string DisplayId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public string Job { get; set; } = "";
        public decimal Total { get; set; }
        public decimal Unpaid { get; set; }
        public decimal Paid { get; set; }
    }

    public class CashAdvanceTotals
    {
        public decimal Total { get; set; }
        public decimal Unpaid { get; set; }
        public decimal Paid { get; set; }
    }
}
