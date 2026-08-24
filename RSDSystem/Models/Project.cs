using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RSDSystem.Validation;

namespace RSDSystem.Models
{
    /// <summary>
    /// A construction job. Admin assigns PayrollStaff by name (AssignedPayrollStaff).
    /// Staff only see projects assigned to them. Status: On Going / Finished / Upcoming / On Hold.
    /// PayrollBudget is the overall budget; MonthlyBudgets are optional per-month caps for prediction.
    /// Admin creates PayrollSchedule rows for this project — those become staff to-do tasks.
    /// </summary>
    public class Project : IValidatableObject
    {
        /// <summary>Database primary key; auto-incremented when Admin creates a project.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ProjectId { get; set; }

        /// <summary>Job name shown on lists, slips, and typeahead search.</summary>
        [Required(ErrorMessage = "Project name is required.")]
        [MaxLength(150)]
        [RegularExpression(InputRules.ProjectNamePattern, ErrorMessage = InputRules.ProjectNameMessage)]
        [Display(Name = "Project Name")]
        public string? ProjectName { get; set; } = string.Empty;

        /// <summary>Site address; same character rules as employee address.</summary>
        [Required(ErrorMessage = "Location is required.")]
        [MaxLength(250)]
        [RegularExpression(InputRules.AddressPattern, ErrorMessage = InputRules.AddressMessage)]
        public string? Location { get; set; }

        /// <summary>Kind of work (E.I.F.S, painting, …) from TypeOfServiceOptions.</summary>
        [Required(ErrorMessage = "Type of service is required.")]
        [MaxLength(150)]
        [Display(Name = "Type of Service")]
        public string? TypeOfService { get; set; }

        /// <summary>Planned first day on site; payroll schedules must fall on or after this.</summary>
        [Required(ErrorMessage = "Starting date is required.")]
        [Display(Name = "Starting Date")]
        [DataType(DataType.Date)]
        public DateTime? StartingDate { get; set; }

        /// <summary>Planned last day; must be on or after StartingDate (checked in Validate).</summary>
        [Required(ErrorMessage = "Estimate end date is required.")]
        [Display(Name = "Estimate End Date")]
        [DataType(DataType.Date)]
        public DateTime? EstimateEndDate { get; set; }

        /// <summary>Overall payroll peso cap for the job; prediction can also use MonthlyBudgets.</summary>
        [Required(ErrorMessage = "Payroll budget is required.")]
        [Range(0.01, 999999999.99, ErrorMessage = "Payroll budget must be greater than 0.")]
        [Column(TypeName = "decimal(18,2)")]
        [Display(Name = "Payroll Budget")]
        public decimal? PayrollBudget { get; set; }

        /// <summary>How often workers are paid: Weekly, Half-Month, or Monthly.</summary>
        [Required(ErrorMessage = "Payroll distribution is required.")]
        [MaxLength(50)]
        [Display(Name = "Payroll Distribution")]
        public string? PayrollDistribution { get; set; }

        /// <summary>Full name of the PayrollStaff user who sees this project on staff screens.</summary>
        [Required(ErrorMessage = "Assigned payroll staff is required.")]
        [MaxLength(150)]
        [Display(Name = "Assigned to Payroll Staff")]
        public string? AssignedPayrollStaff { get; set; }

        /// <summary>On Going / On Hold / Finished; Index filter and staff to-do list use On Going.</summary>
        public string? Status { get; set; } = ProjectStatusOptions.OnGoing;

        /// <summary>Legacy project-level "Mark as Done" flag (schedules now use PayrollSchedule.TaskCompleted).</summary>
        public bool TaskCompleted { get; set; } = false;   // ← new, drives "Mark as Done"

        /// <summary>When Admin created this project row.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Optional per-month budget rows used by payroll prediction ExceedsBudget.</summary>
        public ICollection<ProjectMonthlyBudget> MonthlyBudgets { get; set; }
            = new List<ProjectMonthlyBudget>();

        /// <summary>Rejects dates outside 2000–2099 and an end date before the start date.</summary>
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

    /// <summary>
    /// Optional budget for one calendar month of a project. Prediction compares
    /// the next-month forecast against this amount (ExceedsBudget).
    /// </summary>
    public class ProjectMonthlyBudget
    {
        /// <summary>Database primary key for this month-budget row.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>FK to the parent project; rows cascade-delete with the project.</summary>
        public int ProjectId { get; set; }

        /// <summary>Navigation back to the Project that owns this monthly cap.</summary>
        public Project? Project { get; set; }

        /// <summary>Display label such as "January 2026" shown on project forms and prediction.</summary>
        [MaxLength(30)]
        public string MonthYear { get; set; } = string.Empty; // e.g. "January 2026"

        /// <summary>First calendar day of that month, used to sort and match prediction months.</summary>
        public DateTime MonthDate { get; set; } // first day of that month

        /// <summary>Peso amount allocated for that month (compared to predicted payroll).</summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; } = 0;
    }

    /// <summary>Dropdown values for Project.TypeOfService / PayrollSchedule.TypeOfService.</summary>
    public static class TypeOfServiceOptions
    {
        /// <summary>All allowed type-of-service labels shown on Create/Edit Project and Add Schedule.</summary>
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

    /// <summary>Dropdown values for Project.PayrollDistribution (how often workers are paid).</summary>
    public static class PayrollDistributionOptions
    {
        /// <summary>Weekly, Half-Month, and Monthly — the only pay-frequency choices on the project form.</summary>
        public static readonly string[] All = new[]
        {
            "Weekly",
            "Half-Month",
            "Monthly"
        };
    }

    /// <summary>Project.Status values. Index filter and staff to-do list use On Going.</summary>
    public static class ProjectStatusOptions
    {
        /// <summary>Active job; staff to-do list and default Index filter use this value.</summary>
        public const string OnGoing = "On Going";

        /// <summary>Paused job; Cancelled is treated as On Hold in Normalize/WithStatus.</summary>
        public const string OnHold = "On Hold";

        /// <summary>Completed job; Completed is treated as Finished in Normalize/WithStatus.</summary>
        public const string Finished = "Finished";

        /// <summary>The three status labels shown in the Projects Index filter dropdown.</summary>
        public static readonly string[] All = { OnGoing, OnHold, Finished };

        /// <summary>Maps old/alias strings (Active, Completed, Cancelled) onto On Going / On Hold / Finished.</summary>
        public static string Normalize(string? status)
        {
            var value = (status ?? string.Empty).Trim();
            if (value.Equals("Finished", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("Completed", StringComparison.OrdinalIgnoreCase))
                return Finished;
            if (value.Equals("On Hold", StringComparison.OrdinalIgnoreCase))
                return OnHold;
            if (value.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                return OnHold;
            return OnGoing;
        }

        /// <summary>CSS badge class for the status pill on Project Details (finished / hold / ongoing).</summary>
        public static string BadgeClass(string? status) => Normalize(status) switch
        {
            Finished => "badge-finished",
            OnHold => "badge-hold",
            _ => "badge-ongoing"
        };

        /// <summary>LINQ filter for On Going jobs, including legacy Active/null/blank Status values.</summary>
        public static IQueryable<Project> Ongoing(this IQueryable<Project> projects) =>
            projects.Where(p => p.Status == OnGoing || p.Status == "Active"
                || p.Status == null || p.Status == "");

        /// <summary>LINQ filter used by Project/Index when Admin picks On Going, On Hold, or Finished.</summary>
        public static IQueryable<Project> WithStatus(this IQueryable<Project> projects, string? status)
        {
            var filter = Normalize(status);
            if (filter == Finished)
                return projects.Where(p => p.Status == Finished || p.Status == "Completed");
            if (filter == OnHold)
                return projects.Where(p => p.Status == OnHold || p.Status == "Cancelled");
            return projects.Ongoing();
        }
    }
}
