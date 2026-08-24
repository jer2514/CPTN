using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RSDSystem.Validation;

namespace RSDSystem.Models
{
    /// <summary>
    /// Field worker on a construction project. Payroll and attendance match this row.
    /// EmployeeCode is the displayed ID. RatePerDay (and sometimes RatePerHour) drive the slip.
    /// ProjectId links them to the job they are paid under.
    /// </summary>
    public class Employee : IValidatableObject
    {
        /// <summary>Database primary key; auto-incremented when Admin adds an employee.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int EmployeeId { get; set; }

        /// <summary>Displayed Employee ID on tables and payslips (formatted by EmployeeIds).</summary>
        [Required, MaxLength(10)]
        [Display(Name = "Employee ID")]
        public string EmployeeCode { get; set; } = string.Empty;

        /// <summary>Given name; letters only per InputRules.PersonNamePattern.</summary>
        [Required(ErrorMessage = "First name is required.")]
        [MaxLength(80)]
        [RegularExpression(InputRules.PersonNamePattern, ErrorMessage = InputRules.PersonNameMessage)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        /// <summary>Family name; letters only per InputRules.PersonNamePattern.</summary>
        [Required(ErrorMessage = "Last name is required.")]
        [MaxLength(80)]
        [RegularExpression(InputRules.PersonNamePattern, ErrorMessage = InputRules.PersonNameMessage)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        /// <summary>Optional 1–2 letter middle initial shown in FullName as "A.".</summary>
        [MaxLength(2)]
        [RegularExpression(InputRules.MiddleInitialPattern, ErrorMessage = InputRules.MiddleInitialMessage)]
        [Display(Name = "M.I.")]
        public string? MiddleInitial { get; set; }

        /// <summary>Computed "First M. Last" (or "First Last"); ignored by EF, not a column.</summary>
        [NotMapped]
        public string FullName => string.IsNullOrWhiteSpace(MiddleInitial)
            ? $"{FirstName} {LastName}"
            : $"{FirstName} {MiddleInitial}. {LastName}";

        /// <summary>Required birth date used to compute Age; validated in Validate().</summary>
        [Required(ErrorMessage = "Date of birth is required.")]
        [Display(Name = "Date of Birth")]
        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        /// <summary>Computed years from DateOfBirth via InputRules.CalculateAge; not stored.</summary>
        [NotMapped]
        public int? Age => InputRules.CalculateAge(DateOfBirth);

        /// <summary>Required Male/Female (or similar) stored as a short string.</summary>
        [Required(ErrorMessage = "Gender is required.")]
        [MaxLength(10)]
        public string? Gender { get; set; }

        /// <summary>Required home address; same pattern as project location fields.</summary>
        [Required(ErrorMessage = "Address is required.")]
        [MaxLength(250)]
        [RegularExpression(InputRules.AddressPattern, ErrorMessage = InputRules.AddressMessage)]
        [Display(Name = "Address")]
        public string? Address { get; set; }

        /// <summary>Optional email; unique when set (filtered unique index in PayrollDbContext).</summary>
        [MaxLength(100)]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string? Email { get; set; }

        /// <summary>Required PH mobile number (09xxxxxxxxx) on the employee form.</summary>
        [Required(ErrorMessage = "Contact number is required.")]
        [MaxLength(20)]
        [RegularExpression(InputRules.PhMobilePattern, ErrorMessage = InputRules.PhMobileMessage)]
        [Display(Name = "Contact Number")]
        public string? ContactNumber { get; set; }

        /// <summary>Relative path to the uploaded photo, or null for initials/placeholder.</summary>
        [Display(Name = "Photo")]
        public string? PhotoPath { get; set; }

        /// <summary>Job title used on payslips (Painter, Mason, etc.).</summary>
        [Required(ErrorMessage = "Job classification is required.")]
        [MaxLength(80)]
        [Display(Name = "Job Classification")]
        public string JobClassification { get; set; } = string.Empty;

        /// <summary>Daily wage; RegularPay on a slip is this times RegularDaysWorked.</summary>
        [Range(0.01, 999999.99, ErrorMessage = "Rate per day must be greater than 0.")]
        [Column(TypeName = "decimal(10,2)")]
        [Display(Name = "Rate per Day")]
        public decimal DailyRate { get; set; } = 0;

        [Column(TypeName = "decimal(10,2)")]
        [Display(Name = "Rate per Hour")]
        public decimal RatePerHour { get; set; } = 0;

        /// <summary>False hides the worker from active payroll generate lists.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>When Admin created this employee row; shown on the employee list.</summary>
        [Display(Name = "Date Added")]
        [DataType(DataType.DateTime)]
        public DateTime DateAdded { get; set; } = DateTime.Now;

        /// <summary>FK to the project this worker is assigned to; null if unassigned.</summary>
        public int? ProjectId { get; set; }

        /// <summary>Navigation to the assigned Project (optional; SetNull on project delete).</summary>
        public Project? Project { get; set; }

        /// <summary>Runs InputRules.ValidateDateOfBirth so under-18 / future dates fail model binding.</summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var result in InputRules.ValidateDateOfBirth(DateOfBirth, required: false))
                yield return result;
        }
    }
}
