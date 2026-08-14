using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RSDSystem.Models
{
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UserId { get; set; }

        [Required, MaxLength(10)]
        [Display(Name = "Staff ID")]
        public string UserCode { get; set; } = string.Empty;

        [Required, MaxLength(80)]
        [Display(Name = "First Name")]
        public string FirstName { get; set; } = string.Empty;

        [Required, MaxLength(80)]
        [Display(Name = "Last Name")]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(5)]
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

        [Required, MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string? Email { get; set; }

        [Required, MaxLength(20)]
        [Display(Name = "Contact Number")]
        public string? ContactNumber { get; set; }

        [MaxLength(250)]
        public string? Address { get; set; }

        [Required]
        public string Role { get; set; } = "PayrollStaff";

        public string? PhotoPath { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}