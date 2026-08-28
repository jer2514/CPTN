using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RSDSystem.Validation;

namespace RSDSystem.Models
{
    public class User : IValidatableObject
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UserId { get; set; }

        [Required, MaxLength(10)]
        [Display(Name = "Staff ID")]
        public string UserCode { get; set; } = string.Empty;

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

        [Display(Name = "Date of Birth")]
        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        [NotMapped]


        public int? Age => InputRules.CalculateAge(DateOfBirth);

        [MaxLength(10)]
        public string? Gender { get; set; }

        [Required(ErrorMessage = "Username is required.")]
        [MaxLength(50)]
        [MinLength(3, ErrorMessage = "Username must be at least 3 characters.")]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [MaxLength(100)]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Contact number is required.")]
        [MaxLength(20)]
        [RegularExpression(InputRules.PhMobilePattern, ErrorMessage = InputRules.PhMobileMessage)]
        [Display(Name = "Contact Number")]
        public string? ContactNumber { get; set; }

        [MaxLength(250)]
        [RegularExpression(InputRules.AddressPattern, ErrorMessage = InputRules.AddressMessage)]
        public string? Address { get; set; }

        [Required(ErrorMessage = "Role is required.")]
        public string Role { get; set; } = "PayrollStaff";

        public string? PhotoPath { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// True until the user finishes the emailed set-password link
        /// (or an older Admin-set password that still needs changing).
        /// </summary>
        public bool MustChangePassword { get; set; }

        /// <summary>SHA-256 hex of a one-click set-password token. Null when unused.</summary>
        [MaxLength(64)]
        public string? PasswordResetTokenHash { get; set; }

        public DateTime? PasswordResetExpiry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var result in InputRules.ValidateDateOfBirth(DateOfBirth, required: false))
                yield return result;
        }
    }
}
