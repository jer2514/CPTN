using System;
using System.Linq;
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
        private readonly PasswordLinkService _links;

        public AccountController(
            PayrollDbContext db,
            ActivityLogService logs,
            EmailService email,
            PasswordLinkService links)
        {
            _db = db;
            _logs = logs;
            _email = email;
            _links = links;
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
                ViewBag.Error = user != null && MustChangeOwnPassword(user)
                    ? "This account does not have a password yet. Open the set-password link we emailed you, or use Forgot password."
                    : "Invalid username or password.";
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

            var issued = await IssuePasswordLinkAsync(user, PasswordLinkService.ResetLifetime, isInvite: false);
            HttpContext.Session.SetString(ResetMaskedEmailKey, MaskEmail(user.Email));
            if (!issued.Sent)
                HttpContext.Session.SetString(ResetDisplayLinkKey, issued.Link);
            else
                HttpContext.Session.Remove(ResetDisplayLinkKey);

            return RedirectToAction(nameof(CheckEmail));
        }

        [HttpGet]
        public IActionResult CheckEmail()
        {
            ViewBag.MaskedEmail = HttpContext.Session.GetString(ResetMaskedEmailKey) ?? "your email";
            ViewBag.DisplayLink = HttpContext.Session.GetString(ResetDisplayLinkKey);
            return View();
        }

        [HttpGet]
        public IActionResult VerifyResetCode() => RedirectToAction(nameof(ForgotPassword));

        [HttpGet]
        public IActionResult ResetPassword() => RedirectToAction(nameof(ForgotPassword));

        [HttpGet]
        public async Task<IActionResult> SetPassword(string? token)
        {
            var user = await _links.FindValidAsync(token);
            if (user == null)
            {
                ViewBag.Invalid = true;
                return View();
            }

            ViewBag.Token = token;
            BindResetPasswordView(user);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPassword(string? token, string NewPassword, string ConfirmPassword)
        {
            var user = await _links.FindValidAsync(token);
            if (user == null)
            {
                ViewBag.Invalid = true;
                return View();
            }

            ViewBag.Token = token;
            BindResetPasswordView(user);
            ValidateChosenPassword(user, NewPassword, ConfirmPassword);

            if (!ModelState.IsValid)
                return View();

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            user.MustChangePassword = false;
            _links.Clear(user);
            await _db.SaveChangesAsync();
            var notifyEmail = user.Email;
            var notifyName = user.FullName;
            await _logs.LogAsync(
                user.UserId,
                notifyName,
                user.Role,
                ActivityTypes.ResetPassword,
                ActivityModules.Authentication,
                $"{notifyName} set their password from an email link.");

            if (!string.IsNullOrWhiteSpace(notifyEmail))
                await _email.SendPasswordChangedAsync(notifyEmail, notifyName);

            TempData["Success"] = "Your password was saved. Sign in with the new password.";
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

        private async Task<(bool Sent, string Link)> IssuePasswordLinkAsync(
            User user, TimeSpan lifetime, bool isInvite)
        {
            var token = _links.Issue(user, lifetime);
            await _db.SaveChangesAsync();
            var link = Url.Action(nameof(SetPassword), "Account", new { token }, Request.Scheme, Request.Host.Value)
                ?? throw new InvalidOperationException("Could not build the set-password URL.");
            var sent = await _email.SendSetPasswordLinkAsync(
                user.Email!, user.FullName, user.Username, link, isInvite);
            return (sent.Sent, link);
        }

        private static string MaskEmail(string? email)
        {
            var value = (email ?? "").Trim();
            var at = value.IndexOf('@');
            if (at <= 1)
                return value;
            return value[0] + new string('*', Math.Max(1, at - 1)) + value[at..];
        }

        private const string ResetMaskedEmailKey = "PasswordResetMaskedEmail";
        private const string ResetDisplayLinkKey = "PasswordResetDisplayLink";

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
    }
}
