using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RSDSystem.Validation;

namespace RSDSystem.Models
{
    /// <summary>
    /// One pay period for a project (start/end dates). Created by Admin on the dashboard.
    ///
    /// TaskCompleted = staff clicked "mark done" (waits for admin).
    /// TaskApproved  = admin approved the done request; task leaves the staff to-do list.
    /// Staff generate one slip per employee per schedule (unique index on Payroll).
    /// </summary>
    public class PayrollSchedule : IValidatableObject
    {
        /// <summary>Database primary key for this pay-period task.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PayrollScheduleId { get; set; }

        /// <summary>FK to the project this schedule belongs to (Cascade delete with the project).</summary>
        [Required(ErrorMessage = "Project is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "Project is required.")]
        public int ProjectId { get; set; }

        /// <summary>Navigation to the project; staff only see schedules whose AssignedPayrollStaff matches them.</summary>
        public Project? Project { get; set; }

        /// <summary>Copied type of work (E.I.F.S, painting, …) shown on the staff to-do table.</summary>
        [Required(ErrorMessage = "Type of project is required.")]
        [MaxLength(150)]
        [Display(Name = "Type of Project")]
        public string? TypeOfService { get; set; }

        /// <summary>First day of the pay period; slips must fall on or after this date.</summary>
        [Required(ErrorMessage = "Starting date is required.")]
        [Display(Name = "Starting Date")]
        [DataType(DataType.Date)]
        public DateTime StartingDate { get; set; }

        /// <summary>Last day of the pay period; slips must fall on or before this date.</summary>
        [Required(ErrorMessage = "End date is required.")]
        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        /// <summary>When Admin added this schedule from the dashboard.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>True after staff ticks "Mark as Done"; stays on the list until TaskApproved.</summary>
        public bool TaskCompleted { get; set; } = false;

        /// <summary>True after Admin ApproveTask; the row leaves the staff to-do list.</summary>
        public bool TaskApproved { get; set; } = false;

        /// <summary>Ensures end date is on/after start and both years are in the allowed calendar range.</summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var result in DateRules.ValidateDateRange(
                StartingDate, EndDate,
                nameof(StartingDate), nameof(EndDate),
                "Starting date", "End date"))
            {
                yield return result;
            }
        }
    }
}
