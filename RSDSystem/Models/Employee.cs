using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RSDSystem.Validation;

namespace RSDSystem.Models
{
    public class Employee : IValidatableObject
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int EmployeeId { get; set; }

        [Required, MaxLength(10)]
        [Display(Name = "Employee ID")]
        public string EmployeeCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "First name is required.")]
        [MaxLength(80)]
        [RegularExpression(InputRules.PersonNamePattern, ErrorMessage = InputRules.PersonNameMessage)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Last name is required.")]
        [MaxLength(80)]
        [RegularExpression(InputRules.PersonNamePattern, ErrorMessage = InputRules.PersonNameMessage)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(2)]
        [RegularExpression(InputRules.MiddleInitialPattern, ErrorMessage = InputRules.MiddleInitialMessage)]
        [Display(Name = "M.I.")]
        public string? MiddleInitial { get; set; }

        [NotMapped]
        public string FullName => string.IsNullOrWhiteSpace(MiddleInitial)
            ? $"{FirstName} {LastName}"
            : $"{FirstName} {MiddleInitial}. {LastName}";

        [Required(ErrorMessage = "Date of birth is required.")]
        [Display(Name = "Date of Birth")]
        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }


        [NotMapped]


        public int? Age => InputRules.CalculateAge(DateOfBirth);

        [Required(ErrorMessage = "Gender is required.")]
        [MaxLength(10)]
        public string? Gender { get; set; }

        [Required(ErrorMessage = "Address is required.")]
        [MaxLength(250)]
        [RegularExpression(InputRules.AddressPattern, ErrorMessage = InputRules.AddressMessage)]
        [Display(Name = "Address")]
        public string? Address { get; set; }


        [MaxLength(100)]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Contact number is required.")]
        [MaxLength(20)]
        [RegularExpression(InputRules.PhMobilePattern, ErrorMessage = InputRules.PhMobileMessage)]
        [Display(Name = "Contact Number")]
        public string? ContactNumber { get; set; }

        [Display(Name = "Photo")]
        public string? PhotoPath { get; set; }

        [Required(ErrorMessage = "Job classification is required.")]
        [MaxLength(80)]
        [Display(Name = "Job Classification")]
        public string JobClassification { get; set; } = string.Empty;

        [Range(0.01, 999999.99, ErrorMessage = "Rate per day must be greater than 0.")]
        [Column(TypeName = "decimal(10,2)")]
        [Display(Name = "Rate per Day")]
        public decimal DailyRate { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        [Display(Name = "Rate per Hour")]
        public decimal RatePerHour { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        [Display(Name = "Date Added")]
        [DataType(DataType.DateTime)]
        public DateTime DateAdded { get; set; } = DateTime.Now;

        public int? ProjectId { get; set; }
        public Project? Project { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var result in InputRules.ValidateDateOfBirth(DateOfBirth, required: false))
                yield return result;
        }
    }
}
