using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    public class Employee
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int EmployeeId { get; set; }

        [Required, MaxLength(80)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required, MaxLength(80)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(2)]
        [Display(Name = "M.I.")]
        public string? MiddleInitial { get; set; }

        public string FullName => string.IsNullOrWhiteSpace(MiddleInitial)
            ? $"{FirstName} {LastName}"
            : $"{FirstName} {MiddleInitial}. {LastName}";

        [Display(Name = "Date of Birth")]
        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        public int? Age => DateOfBirth.HasValue
            ? (int?)(DateTime.Today.Year - DateOfBirth.Value.Year -
                     (DateTime.Today.DayOfYear < DateOfBirth.Value.DayOfYear ? 1 : 0))
            : null;

        [MaxLength(10)]
        public string? Gender { get; set; }

        [MaxLength(250)]
        [Display(Name = "Address")]
        public string? Address { get; set; }

        [Required, MaxLength(100), EmailAddress]
        public string? Email { get; set; }

        [Required, MaxLength(20)]
        [Display(Name = "Contact Number")]
        public string? ContactNumber { get; set; }

        [Display(Name = "Photo")]
        public string? PhotoPath { get; set; }

        [Required, MaxLength(80)]
        [Display(Name = "Job Classification")]
        public string JobClassification { get; set; } = string.Empty;


        [Column(TypeName = "decimal(10,2)")]
        [Display(Name = "Daily Rate")]
        public decimal DailyRate { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        // FK to Project (nullable — employee may not be assigned yet)
        public int? ProjectId { get; set; }
        public Project? Project { get; set; }
    }
}