using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    public class PayrollPredictionHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        public DateTime PreviousMonth1 { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PreviousAmount1 { get; set; }

        public DateTime PreviousMonth2 { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PreviousAmount2 { get; set; }

        public DateTime PredictionMonth { get; set; }

        [MaxLength(40)]
        public string PredictionLabel { get; set; } = "";

        [Column(TypeName = "decimal(18,2)")]
        public decimal PredictedPayroll { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal AllocatedBudget { get; set; }

        public bool HasAllocatedBudget { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal BudgetDifference { get; set; }

        public bool ExceedsBudget { get; set; }
        public bool UnusualChange { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal ChangePercent { get; set; }

        [MaxLength(80)]
        public string? RiskTitle { get; set; }

        [MaxLength(300)]
        public string? RiskDetail { get; set; }

        [MaxLength(20)]
        public string Engine { get; set; } = "local";

        public DateTime GeneratedAt { get; set; } = Helpers.PhilippinesTime.Now;
    }
}
