using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using RSDSystem.Validation;

namespace RSDSystem.Models
{
    /// <summary>
    /// Login account. Role is "Admin" or "PayrollStaff".
    /// PasswordHash is BCrypt, never the raw password.
    /// UserCode is the displayed Staff ID. FullName/Age are computed, not stored.
    /// IsActive=false blocks login (AccountController only loads active users).
    /// </summary>
    public class User : IValidatableObject
    {
        /// <summary>Database primary key; auto-incremented when a staff/admin account is created.</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UserId { get; set; }

        /// <summary>Displayed Staff ID on lists and the profile card (not the login username).</summary>
        [Required, MaxLength(10)]
        [Display(Name = "Staff ID")]
        public string UserCode { get; set; } = string.Empty;

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

        /// <summary>Optional birth date used to compute Age; validated in Validate().</summary>
        [Display(Name = "Date of Birth")]
        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        /// <summary>Computed years from DateOfBirth via InputRules.CalculateAge; not stored.</summary>
        [NotMapped]
        public int? Age => InputRules.CalculateAge(DateOfBirth);

        /// <summary>Optional Male/Female (or similar) stored as a short string.</summary>
        [MaxLength(10)]
        public string? Gender { get; set; }

        /// <summary>Login name posted by Account/Login; unique index in PayrollDbContext.</summary>
        [Required(ErrorMessage = "Username is required.")]
        [MaxLength(50)]
        [MinLength(3, ErrorMessage = "Username must be at least 3 characters.")]
        public string Username { get; set; } = string.Empty;

        /// <summary>BCrypt hash of the password; never store or display the raw password.</summary>
        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        /// <summary>Contact email; required on the form and unique in the Users table.</summary>
        [Required(ErrorMessage = "Email is required.")]
        [MaxLength(100)]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string? Email { get; set; }

        /// <summary>PH mobile number (09xxxxxxxxx) shown on User Management forms.</summary>
        [Required(ErrorMessage = "Contact number is required.")]
        [MaxLength(20)]
        [RegularExpression(InputRules.PhMobilePattern, ErrorMessage = InputRules.PhMobileMessage)]
        [Display(Name = "Contact Number")]
        public string? ContactNumber { get; set; }

        /// <summary>Optional home/office address; same pattern as employee/project location fields.</summary>
        [MaxLength(250)]
        [RegularExpression(InputRules.AddressPattern, ErrorMessage = InputRules.AddressMessage)]
        public string? Address { get; set; }

        /// <summary>"Admin" or "PayrollStaff"; drives which layout and screens the user sees.</summary>
        [Required(ErrorMessage = "Role is required.")]
        public string Role { get; set; } = "PayrollStaff";

        /// <summary>Relative path to the uploaded photo, or null to show initials on the profile card.</summary>
        public string? PhotoPath { get; set; }

        /// <summary>False blocks login; AccountController only loads active users.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>When the account row was inserted; shown on user lists as date added.</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>Runs InputRules.ValidateDateOfBirth so under-18 / future dates fail model binding.</summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            foreach (var result in InputRules.ValidateDateOfBirth(DateOfBirth, required: false))
                yield return result;
        }
    }
}
