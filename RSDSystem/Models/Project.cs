using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RSDSystem.Validation;

namespace RSDSystem.Models
{
    public class Project : IValidatableObject
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ProjectId { get; set; }

        [Required(ErrorMessage = "Project name is required.")]
        [MaxLength(150)]
        [RegularExpression(InputRules.ProjectNamePattern, ErrorMessage = InputRules.ProjectNameMessage)]
        [Display(Name = "Project Name")]
        public string? ProjectName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Location is required.")]
        [MaxLength(250)]
        [RegularExpression(InputRules.AddressPattern, ErrorMessage = InputRules.AddressMessage)]
        public string? Location { get; set; }

        [Required(ErrorMessage = "Type of service is required.")]
        [MaxLength(150)]
        [Display(Name = "Type of Service")]
        public string? TypeOfService { get; set; }

        [Required(ErrorMessage = "Starting date is required.")]
        [Display(Name = "Starting Date")]
        [DataType(DataType.Date)]
        public DateTime? StartingDate { get; set; }

        [Required(ErrorMessage = "Estimate end date is required.")]
        [Display(Name = "Estimate End Date")]
        [DataType(DataType.Date)]
        public DateTime? EstimateEndDate { get; set; }

        [Required(ErrorMessage = "Payroll budget is required.")]
        [Range(0.01, 999999999.99, ErrorMessage = "Payroll budget must be greater than 0.")]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Payroll Budget")]
        public decimal? PayrollBudget { get; set; }

        [Required(ErrorMessage = "Payroll distribution is required.")]
        [MaxLength(50)]
        [Display(Name = "Payroll Distribution")]
        public string? PayrollDistribution { get; set; }

        [Required(ErrorMessage = "Assigned payroll staff is required.")]
        [MaxLength(150)]
        [Display(Name = "Assigned to Payroll Staff")]
        public string? AssignedPayrollStaff { get; set; }

        public string? Status { get; set; } = "Active";

        public bool TaskCompleted { get; set; } = false;   // ← new, drives "Mark as Done"

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation
        public ICollection<ProjectMonthlyBudget> MonthlyBudgets { get; set; }
            = new List<ProjectMonthlyBudget>();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (StartingDate.HasValue && !DateRules.IsUsableDate(StartingDate))
            {
                yield return new ValidationResult(
                    DateRules.CalendarYearMessage, new[] { nameof(StartingDate) });
            }

            if (EstimateEndDate.HasValue && !DateRules.IsUsableDate(EstimateEndDate))
            {
                yield return new ValidationResult(
                    DateRules.CalendarYearMessage, new[] { nameof(EstimateEndDate) });
            }

            if (DateRules.IsUsableDate(StartingDate) && DateRules.IsUsableDate(EstimateEndDate)
                && EstimateEndDate!.Value.Date < StartingDate!.Value.Date)
            {
                yield return new ValidationResult(
                    "Estimate end date must be on or after the starting date.",
                    new[] { nameof(EstimateEndDate) });
            }
        }
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