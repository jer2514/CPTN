using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;
using RSDSystem.Services;
using RSDSystem.Validation;

namespace RSDSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly ActivityLogService _logs;
        private readonly EmailService _email;

        public AccountController(PayrollDbContext db, ActivityLogService logs, EmailService email)
        {
            _db = db;
            _logs = logs;
            _email = email;
        }

        [HttpGet]
        public async Task<IActionResult> Login(string? returnUrl = null, int? inactive = null)
        {
            var signedIn = await SignedInDestinationAsync();
            if (signedIn != null)
                return signedIn;

            if (inactive == 1)
                ViewBag.Error = "This account is inactive and cannot log in.";
            else if (TempData["LoginError"] is string loginError)
                ViewBag.Error = loginError;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string Username, string Password, string? returnUrl)
        {
            var user = await _db.Users
                                .FirstOrDefaultAsync(u => u.Username == Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash))
            {
                ViewBag.Error = "Invalid username or password.";
                return View();
            }

            if (!user.IsActive)
            {
                ViewBag.Error = "This account is inactive and cannot log in.";
                return View();
            }

            SignIn(user.UserId, user.FullName, user.Role, user.PhotoPath);
            await _logs.LogAsync(
                user.UserId,
                user.FullName,
                user.Role,
                ActivityTypes.Login,
                ActivityModules.Authentication,
                $"{user.FullName} signed in.");

            if (MustChangeOwnPassword(user))
                return RedirectToAction(nameof(ChangePassword));

            return user.Role == "Admin"
                ? RedirectToAction("Index", "Home")
                : RedirectToAction("Index", "PayrollStaff");
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string Username, string Email)
        {
            ViewBag.Username = Username;
            ViewBag.Email = Email;

            var username = (Username ?? "").Trim();
            var email = (Email ?? "").Trim();
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email))
            {
                ViewBag.Error = "Enter the username and email on this account.";
                return View();
            }

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            var emailMatches = user?.Email != null
                && string.Equals(user.Email.Trim(), email, StringComparison.OrdinalIgnoreCase);

            if (user == null || !emailMatches)
            {
                ViewBag.Error = "Username and email do not match an account.";
                return View();
            }

            if (!user.IsActive)
            {
                ViewBag.Error = "This account is inactive and cannot reset a password.";
                return View();
            }

            var sent = await StartEmailVerificationAsync(user);
            if (!sent.Sent)
            {
                ViewBag.Error = sent.Error ?? "Could not send the verification email.";
                return View();
            }

            return RedirectToAction(nameof(VerifyResetCode));
        }

        [HttpGet]
        public async Task<IActionResult> VerifyResetCode()
        {
            var user = await PasswordResetUserAsync();
            if (user == null)
            {
                TempData["Error"] = "Your reset session expired. Enter your username and email again.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            BindVerifyView();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyResetCode(string Code, string? resend)
        {
            var user = await PasswordResetUserAsync();
            if (user == null)
            {
                TempData["Error"] = "Your reset session expired. Enter your username and email again.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            if (string.Equals(resend, "1", StringComparison.Ordinal))
            {
                var sent = await StartEmailVerificationAsync(user);
                BindVerifyView();
                if (!sent.Sent)
                    ViewBag.Error = sent.Error ?? "Could not send the verification email.";
                else
                    ViewBag.Info = "A new code was sent to your email.";
                return View();
            }

            var entered = (Code ?? "").Trim();
            var expected = HttpContext.Session.GetString(ResetCodeHashKey);
            if (string.IsNullOrEmpty(entered) || string.IsNullOrEmpty(expected)
                || !FixedEquals(HashResetCode(entered, user.UserId), expected))
            {
                BindVerifyView();
                ViewBag.Error = "That code is incorrect. Check the email and try again.";
                return View();
            }

            HttpContext.Session.SetString(ResetVerifiedKey, "1");
            HttpContext.Session.Remove(ResetDisplayCodeKey);
            return RedirectToAction(nameof(ResetPassword));
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword()
        {
            var user = await PasswordResetUserAsync(requireVerified: true);
            if (user == null)
            {
                TempData["Error"] = "Confirm the code from your email before creating a new password.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            BindResetPasswordView(user);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string NewPassword, string ConfirmPassword)
        {
            var user = await PasswordResetUserAsync(requireVerified: true);
            if (user == null)
            {
                TempData["Error"] = "Confirm the code from your email before creating a new password.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            BindResetPasswordView(user);
            ValidateChosenPassword(user, NewPassword, ConfirmPassword);

            if (!ModelState.IsValid)
                return View();

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            user.MustChangePassword = false;
            await _db.SaveChangesAsync();
            var notifyEmail = user.Email;
            var notifyName = user.FullName;
            ClearPasswordReset();
            await _logs.LogAsync(
                user.UserId,
                notifyName,
                user.Role,
                ActivityTypes.ResetPassword,
                ActivityModules.Authentication,
                $"{notifyName} reset their password.");

            if (!string.IsNullOrWhiteSpace(notifyEmail))
                await _email.SendPasswordChangedAsync(notifyEmail, notifyName);

            TempData["Success"] = "Your password was saved. Sign in with the new password. A confirmation was sent to your email.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await CurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            return View(user);
        }

        [HttpGet]
        public async Task<IActionResult> ChangePassword()
        {
            var user = await CurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            BindChangePasswordView(user);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string CurrentPassword, string NewPassword, string ConfirmPassword)
        {
            var user = await CurrentUserAsync();
            if (user == null)
                return RedirectToAction(nameof(Login));

            BindChangePasswordView(user);

            if (string.IsNullOrWhiteSpace(CurrentPassword) || !BCrypt.Net.BCrypt.Verify(CurrentPassword, user.PasswordHash))
                ModelState.AddModelError("CurrentPassword", "Current password is incorrect.");

            ValidateChosenPassword(user, NewPassword, ConfirmPassword);

            if (!ModelState.IsValid)
                return View();

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            user.MustChangePassword = false;
            await _db.SaveChangesAsync();
            await _logs.LogAsync(
                ActivityTypes.ChangePassword,
                ActivityModules.Authentication,
                $"{user.FullName} changed their password.");

            if (!string.IsNullOrWhiteSpace(user.Email))
                await _email.SendPasswordChangedAsync(user.Email, user.FullName);

            TempData["Success"] = "Your password was changed. A confirmation was sent to your email.";
            return RedirectToAction(nameof(Profile));
        }

        private void SignIn(int userId, string fullName, string role, string? photoPath)
        {
            HttpContext.Session.SetString("UserId", userId.ToString());
            HttpContext.Session.SetString("FullName", fullName);
            HttpContext.Session.SetString("Role", role);

            if (!string.IsNullOrEmpty(photoPath))
                HttpContext.Session.SetString("PhotoPath", photoPath);
            else
                HttpContext.Session.Remove("PhotoPath");
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await _logs.LogAsync(
                ActivityTypes.Logout,
                ActivityModules.Authentication,
                "User signed out.");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();

            TempData["Success"] = "You have been logged out.";
            return RedirectToAction("Login", "Account");
        }

        private async Task<IActionResult?> SignedInDestinationAsync()
        {
            var user = await CurrentUserAsync();
            if (user == null)
                return null;

            if (MustChangeOwnPassword(user))
                return RedirectToAction(nameof(ChangePassword));

            return user.Role == "Admin"
                ? RedirectToAction("Index", "Home")
                : RedirectToAction("Index", "PayrollStaff");
        }

        private async Task<User?> CurrentUserAsync()
        {
            var userId = HttpContext.Session.GetString("UserId");
            if (!int.TryParse(userId, out var id))
                return null;

            return await _db.Users.FirstOrDefaultAsync(u => u.UserId == id && u.IsActive);
        }

        private static bool MustChangeOwnPassword(User user) =>
            string.Equals(user.Role, "PayrollStaff", StringComparison.OrdinalIgnoreCase)
            && user.MustChangePassword;

        private void BindChangePasswordView(User user)
        {
            ViewBag.Forced = MustChangeOwnPassword(user);
            ViewBag.IsStaff = string.Equals(user.Role, "PayrollStaff", StringComparison.OrdinalIgnoreCase);
        }

        private void BindResetPasswordView(User user)
        {
            ViewBag.IsStaff = string.Equals(user.Role, "PayrollStaff", StringComparison.OrdinalIgnoreCase);
        }

        private void BindVerifyView()
        {
            ViewBag.MaskedEmail = HttpContext.Session.GetString(ResetMaskedEmailKey) ?? "your email";
            ViewBag.DisplayCode = HttpContext.Session.GetString(ResetDisplayCodeKey);
        }

        private async Task<(bool Sent, string? Error)> StartEmailVerificationAsync(User user)
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            BeginPasswordReset(user.UserId);
            HttpContext.Session.SetString(ResetCodeHashKey, HashResetCode(code, user.UserId));
            HttpContext.Session.SetString(ResetMaskedEmailKey, MaskEmail(user.Email));
            HttpContext.Session.Remove(ResetVerifiedKey);

            var sent = await _email.SendVerificationCodeAsync(user.Email!, user.FullName, code);
            if (!sent.Sent)
            {
                if (_email.IsConfigured)
                    return sent;

                HttpContext.Session.SetString(ResetDisplayCodeKey, code);
                return (true, null);
            }

            HttpContext.Session.Remove(ResetDisplayCodeKey);
            return (true, null);
        }

        private static string HashResetCode(string code, int userId)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim() + ":" + userId));
            return Convert.ToHexString(bytes);
        }

        private static bool FixedEquals(string left, string right)
        {
            var a = Encoding.UTF8.GetBytes(left);
            var b = Encoding.UTF8.GetBytes(right);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        private static string MaskEmail(string? email)
        {
            var value = (email ?? "").Trim();
            var at = value.IndexOf('@');
            if (at <= 1)
                return value;
            return value[0] + new string('*', Math.Max(1, at - 1)) + value[at..];
        }

        private const string ResetUserIdKey = "PasswordResetUserId";
        private const string ResetUntilKey = "PasswordResetUntil";
        private const string ResetCodeHashKey = "PasswordResetCodeHash";
        private const string ResetVerifiedKey = "PasswordResetVerified";
        private const string ResetMaskedEmailKey = "PasswordResetMaskedEmail";
        private const string ResetDisplayCodeKey = "PasswordResetDisplayCode";

        private void ValidateChosenPassword(User user, string? newPassword, string? confirmPassword)
        {
            if (string.Equals(user.Role, "PayrollStaff", StringComparison.OrdinalIgnoreCase))
            {
                if (!InputRules.TryValidateStaffPassword(newPassword, out var passwordError))
                    ModelState.AddModelError("NewPassword", passwordError!);
            }
            else if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            {
                ModelState.AddModelError("NewPassword", "Password must be at least 8 characters.");
            }

            if (!string.IsNullOrWhiteSpace(newPassword) && newPassword != confirmPassword)
                ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");

            if (!string.IsNullOrWhiteSpace(newPassword)
                && BCrypt.Net.BCrypt.Verify(newPassword, user.PasswordHash))
            {
                ModelState.AddModelError("NewPassword", "Choose a password that is different from the current one.");
            }
        }

        private void BeginPasswordReset(int userId)
        {
            HttpContext.Session.SetInt32(ResetUserIdKey, userId);
            HttpContext.Session.SetString(
                ResetUntilKey,
                DateTime.UtcNow.AddMinutes(10).ToString("O"));
        }

        private async Task<User?> PasswordResetUserAsync(bool requireVerified = false)
        {
            var id = HttpContext.Session.GetInt32(ResetUserIdKey);
            var untilRaw = HttpContext.Session.GetString(ResetUntilKey);
            if (id == null
                || string.IsNullOrEmpty(untilRaw)
                || !DateTime.TryParse(untilRaw, null, System.Globalization.DateTimeStyles.RoundtripKind, out var until)
                || until < DateTime.UtcNow)
            {
                ClearPasswordReset();
                return null;
            }

            if (requireVerified && HttpContext.Session.GetString(ResetVerifiedKey) != "1")
                return null;

            return await _db.Users.FirstOrDefaultAsync(u => u.UserId == id && u.IsActive);
        }

        private void ClearPasswordReset()
        {
            HttpContext.Session.Remove(ResetUserIdKey);
            HttpContext.Session.Remove(ResetUntilKey);
            HttpContext.Session.Remove(ResetCodeHashKey);
            HttpContext.Session.Remove(ResetVerifiedKey);
            HttpContext.Session.Remove(ResetMaskedEmailKey);
            HttpContext.Session.Remove(ResetDisplayCodeKey);
        }
    }
}
