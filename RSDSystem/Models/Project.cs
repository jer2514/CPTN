using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    public class Project
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ProjectId { get; set; }

        [Required, MaxLength(150)]
        [Display(Name = "Project Name")]
        public string ProjectName { get; set; } = string.Empty;

        [MaxLength(250)]
        public string? Location { get; set; }

        [MaxLength(150)]
        [Display(Name = "Type of Service")]
        public string? TypeOfService { get; set; }

        [Display(Name = "Starting Date")]
        [DataType(DataType.Date)]
        public DateTime? StartingDate { get; set; }

        [Display(Name = "Estimate End Date")]
        [DataType(DataType.Date)]
        public DateTime? EstimateEndDate { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Payroll Budget")]
        public decimal? PayrollBudget { get; set; }

        [MaxLength(50)]
        [Display(Name = "Payroll Distribution")]
        public string? PayrollDistribution { get; set; }

        [MaxLength(150)]
        [Display(Name = "Assigned to Payroll Staff")]
        public string? AssignedPayrollStaff { get; set; }

        public string Status { get; set; } = "Active";

        public bool TaskCompleted { get; set; } = false;   // ← new, drives "Mark as Done"

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation
        public ICollection<ProjectMonthlyBudget> MonthlyBudgets { get; set; }
            = new List<ProjectMonthlyBudget>();

    }

    public class ProjectMonthlyBudget
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        [MaxLength(30)]
        public string MonthYear { get; set; } = string.Empty; // e.g. "January 2026"

        public DateTime MonthDate { get; set; } // first day of that month

        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; } = 0;
    }

    public static class TypeOfServiceOptions
    {
        public static readonly string[] All = new[]
        {
            "E.I.F.S CLADDING",
            "E.I.F.S MOULDING",
            "STUCCO FINISH",
            "ACP CLADDING",
            "PAINTING WORKS",
            "EXTERIOR PAINTING",
            "MASONRY WORKS",
            "CEILING & DRY WALL",
            "WELDING WORKS",
            "WATERPROOFING",
            "SEALANT APPLICATIONS",
            "TILING APPLICATION",
            "EXTERIOR INSULATION AND FINISH SYSTEM"
        };
    }

    public static class PayrollDistributionOptions
    {
        public static readonly string[] All = new[]
        {
            "Weekly",
            "Half-Month",
            "Monthly"
        };
    }
}