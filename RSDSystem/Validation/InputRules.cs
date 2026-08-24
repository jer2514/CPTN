using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace RSDSystem.Validation
{
    /// <summary>
    /// Shared input rules used by models, controllers, and client-side validation.
    /// </summary>
    public static class InputRules
    {
        public const string PersonNamePattern = @"^[A-Za-zÑñ][A-Za-zÑñ\s.'\-]{0,79}$";
        public const string PersonNameMessage = "Use letters only. Spaces, hyphens, and apostrophes are allowed.";

        public const string MiddleInitialPattern = @"^[A-Za-zÑñ]{1,2}$";
        public const string MiddleInitialMessage = "Middle initial must be 1–2 letters.";

        public const string PhMobilePattern = @"^09\d{9}$";
        public const string PhMobileMessage = "Contact number must be 11 digits starting with 09.";

        public const string AddressPattern = @"^[A-Za-z0-9Ññ\s,.\-/#'()&]{5,250}$";
        public const string AddressMessage = "Enter a complete address (Barangay, Municipality/City, Province).";

        public const string ProjectNamePattern = @"^[A-Za-z0-9Ññ][A-Za-z0-9Ññ\s.'\-/&#()]{1,149}$";
        public const string ProjectNameMessage = "Enter a valid project name.";

        public const int MinWorkingAge = 18;
        public const int MaxWorkingAge = 80;
        public const long MaxPhotoBytes = 2 * 1024 * 1024;

        public static readonly string[] AllowedPhotoExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        public static readonly string[] AllowedPhotoContentTypes =
        {
            "image/jpeg", "image/png", "image/webp"
        };

        /// <summary>
        /// True when the string looks like a person name (letters, spaces, hyphen, apostrophe). Employee and user create forms use this.
        /// </summary>
        public static bool IsPersonName(string? value) =>
            !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value.Trim(), PersonNamePattern);

        /// <summary>
        /// True when middle initial is blank (optional) or 1–2 letters. Employee records store this separately from first/last name.
        /// </summary>
        public static bool IsMiddleInitial(string? value) =>
            string.IsNullOrWhiteSpace(value) || Regex.IsMatch(value.Trim(), MiddleInitialPattern);

        /// <summary>
        /// True for an 11-digit Philippine mobile starting with 09. Employee and user contact-number fields require this shape.
        /// </summary>
        public static bool IsPhMobile(string? value) =>
            !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value.Trim(), PhMobilePattern);

        /// <summary>
        /// Age in full years on <paramref name="asOf"/> (today if omitted). Returns null when birth date is missing.
        /// Employee hire validation uses this with <see cref="MinWorkingAge"/> / <see cref="MaxWorkingAge"/>.
        /// </summary>
        public static int? CalculateAge(DateTime? dateOfBirth, DateTime? asOf = null)
        {
            if (!dateOfBirth.HasValue) return null;

            var today = (asOf ?? DateTime.Today).Date;
            var dob = dateOfBirth.Value.Date;
            var age = today.Year - dob.Year;
            if (today < dob.AddYears(age)) age--;
            return age;
        }

        /// <summary>
        /// Yields errors if date of birth is required and missing, in the future, or produces an age outside 18–80.
        /// Called from Employee model validation before the person can be assigned to a project payroll.
        /// </summary>
        public static IEnumerable<ValidationResult> ValidateDateOfBirth(
            DateTime? dateOfBirth,
            string fieldName = "DateOfBirth",
            bool required = true)
        {
            if (!dateOfBirth.HasValue)
            {
                if (required)
                    yield return new ValidationResult("Date of birth is required.", new[] { fieldName });
                yield break;
            }

            var dob = dateOfBirth.Value.Date;
            if (dob > DateTime.Today)
            {
                yield return new ValidationResult("Date of birth cannot be in the future.", new[] { fieldName });
                yield break;
            }

            var age = CalculateAge(dob);
            if (age < MinWorkingAge)
            {
                yield return new ValidationResult(
                    $"Must be at least {MinWorkingAge} years old.", new[] { fieldName });
            }
            else if (age > MaxWorkingAge)
            {
                yield return new ValidationResult("Please enter a valid date of birth.", new[] { fieldName });
            }
        }

        public const int MinCalendarYear = DateRules.MinCalendarYear;
        public const int MaxCalendarYear = DateRules.MaxCalendarYear;
        public const string CalendarYearMessage = DateRules.CalendarYearMessage;

        /// <summary>Forwards to <see cref="DateRules.IsMissingDate"/> so older callers that import InputRules still compile.</summary>
        public static bool IsMissingDate(DateTime? value) => DateRules.IsMissingDate(value);

        /// <summary>Forwards to <see cref="DateRules.IsUsableDate"/> for payroll period and project date checks.</summary>
        public static bool IsUsableDate(DateTime? value) => DateRules.IsUsableDate(value);

        /// <summary>Forwards to <see cref="DateRules.InclusiveDays"/> for pay-period length.</summary>
        public static int InclusiveDays(DateTime start, DateTime end) => DateRules.InclusiveDays(start, end);

        /// <summary>Forwards to <see cref="DateRules.CountWeekdays"/> for weekday-only duration.</summary>
        public static int CountWeekdays(DateTime start, DateTime end) => DateRules.CountWeekdays(start, end);

        /// <summary>
        /// Forwards date-range validation to <see cref="DateRules.ValidateDateRange"/> (start required, end on or after start).
        /// </summary>
        public static IEnumerable<ValidationResult> ValidateDateRange(
            DateTime? start,
            DateTime? end,
            string startField,
            string endField,
            string startLabel = "Starting date",
            string endLabel = "End date") =>
            DateRules.ValidateDateRange(start, end, startField, endField, startLabel, endLabel);

        /// <summary>
        /// Parses a whole number that cannot be negative (late minutes, row counts). On failure <paramref name="error"/> is a field message.
        /// </summary>
        public static bool TryParseNonNegativeInt(string? raw, out int value, out string? error, string label)
        {
            value = 0;
            error = null;
            if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw.Trim(), out value))
            {
                error = $"{label} must be a whole number.";
                return false;
            }

            if (value < 0)
            {
                error = $"{label} cannot be negative.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Parses a money/hours amount that cannot be negative (rates, budget, overtime hours). Sets <paramref name="error"/> when invalid.
        /// </summary>
        public static bool TryParseNonNegativeDecimal(string? raw, out decimal value, out string? error, string label)
        {
            value = 0;
            error = null;
            if (string.IsNullOrWhiteSpace(raw) || !decimal.TryParse(raw.Trim(), out value))
            {
                error = $"{label} must be a valid amount.";
                return false;
            }

            if (value < 0)
            {
                error = $"{label} cannot be negative.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks an optional employee/user photo: 2 MB max and JPG/PNG/WEBP only. Missing file is allowed (returns true).
        /// </summary>
        public static bool TryValidatePhoto(IFormFile? photo, out string? error)
        {
            error = null;
            if (photo == null || photo.Length == 0)
                return true;

            if (photo.Length > MaxPhotoBytes)
            {
                error = "Photo must be 2 MB or smaller.";
                return false;
            }

            var ext = Path.GetExtension(photo.FileName)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(ext) || !AllowedPhotoExtensions.Contains(ext))
            {
                error = "Photo must be a JPG, PNG, or WEBP image.";
                return false;
            }

            if (!string.IsNullOrEmpty(photo.ContentType) &&
                !AllowedPhotoContentTypes.Contains(photo.ContentType.ToLowerInvariant()))
            {
                error = "Photo must be a JPG, PNG, or WEBP image.";
                return false;
            }

            return true;
        }

        public const int MinStaffPasswordLength = 8;
        public const string StaffPasswordMessage =
            "Password must be at least 8 characters and include 1 capital letter, 1 number, and 1 special character.";

        public static bool TryValidateStaffPassword(string? password, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(password))
            {
                error = "Password is required.";
                return false;
            }

            if (password.Length < MinStaffPasswordLength
                || !password.Any(char.IsUpper)
                || !password.Any(char.IsDigit)
                || !password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                error = StaffPasswordMessage;
                return false;
            }

            return true;
        }
    }
}
