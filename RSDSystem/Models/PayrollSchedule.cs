using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RSDSystem.Validation;

namespace RSDSystem.Models
{
    public class PayrollSchedule : IValidatableObject
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PayrollScheduleId { get; set; }

        [Required(ErrorMessage = "Project is required.")]
        [Range(1, int.MaxValue, ErrorMessage = "Project is required.")]
        public int ProjectId { get; set; }
        public Project? Project { get; set; }

        [Required(ErrorMessage = "Type of project is required.")]
        [MaxLength(150)]
        [Display(Name = "Type of Project")]
        public string? TypeOfService { get; set; }

        [Required(ErrorMessage = "Starting date is required.")]
        [Display(Name = "Starting Date")]
        [DataType(DataType.Date)]
        public DateTime StartingDate { get; set; }

        [Required(ErrorMessage = "End date is required.")]
        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime EndDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool TaskCompleted { get; set; } = false;

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
