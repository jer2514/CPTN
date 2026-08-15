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

        public static bool IsPersonName(string? value) =>
            !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value.Trim(), PersonNamePattern);

        public static bool IsMiddleInitial(string? value) =>
            string.IsNullOrWhiteSpace(value) || Regex.IsMatch(value.Trim(), MiddleInitialPattern);

        public static bool IsPhMobile(string? value) =>
            !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value.Trim(), PhMobilePattern);

        public static int? CalculateAge(DateTime? dateOfBirth, DateTime? asOf = null)
        {
            if (!dateOfBirth.HasValue) return null;

            var today = (asOf ?? DateTime.Today).Date;
            var dob = dateOfBirth.Value.Date;
            var age = today.Year - dob.Year;
            if (today < dob.AddYears(age)) age--;
            return age;
        }

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

        public const int MinCalendarYear = 2000;
        public const int MaxCalendarYear = 2099;

        public static bool IsMissingDate(DateTime? value) =>
            !value.HasValue || value.Value == default || value.Value.Year < 1000;

        public static bool IsUsableDate(DateTime? value) =>
            value.HasValue
            && !IsMissingDate(value)
            && value.Value.Year >= MinCalendarYear
            && value.Value.Year <= MaxCalendarYear;

        public static int InclusiveDays(DateTime start, DateTime end) =>
            (end.Date - start.Date).Days + 1;

        public static int CountWeekdays(DateTime start, DateTime end)
        {
            var days = 0;
            for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
            {
                if (date.DayOfWeek != DayOfWeek.Saturday && date.DayOfWeek != DayOfWeek.Sunday)
                    days++;
            }
            return days;
        }

        public const string CalendarYearMessage = "Enter a valid date with a 4-digit year (2000–2099).";

        public static IEnumerable<ValidationResult> ValidateDateRange(
            DateTime? start,
            DateTime? end,
            string startField,
            string endField,
            string startLabel = "Starting date",
            string endLabel = "End date")
        {
            if (IsMissingDate(start))
                yield return new ValidationResult($"{startLabel} is required.", new[] { startField });
            else if (!IsUsableDate(start))
                yield return new ValidationResult(CalendarYearMessage, new[] { startField });

            if (IsMissingDate(end))
                yield return new ValidationResult($"{endLabel} is required.", new[] { endField });
            else if (!IsUsableDate(end))
                yield return new ValidationResult(CalendarYearMessage, new[] { endField });

            if (IsUsableDate(start) && IsUsableDate(end) && end!.Value.Date < start!.Value.Date)
            {
                yield return new ValidationResult(
                    $"{endLabel} must be on or after the starting date.",
                    new[] { endField });
            }
        }

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
    }
}
