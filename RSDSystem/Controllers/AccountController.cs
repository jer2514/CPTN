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

        public AccountController(PayrollDbContext db, ActivityLogService logs)
        {
            _db = db;
            _logs = logs;
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

            if (string.Equals(user.Role, "PayrollStaff", StringComparison.OrdinalIgnoreCase))
            {
                if (!InputRules.TryValidateStaffPassword(NewPassword, out var passwordError))
                    ModelState.AddModelError("NewPassword", passwordError!);
            }
            else if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 8)
            {
                ModelState.AddModelError("NewPassword", "Password must be at least 8 characters.");
            }

            if (!string.IsNullOrWhiteSpace(NewPassword) && NewPassword != ConfirmPassword)
                ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");

            if (!string.IsNullOrWhiteSpace(NewPassword)
                && BCrypt.Net.BCrypt.Verify(NewPassword, user.PasswordHash))
            {
                ModelState.AddModelError("NewPassword", "Choose a password that is different from the current one.");
            }

            if (!ModelState.IsValid)
                return View();

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);
            user.MustChangePassword = false;
            await _db.SaveChangesAsync();
            await _logs.LogAsync(
                ActivityTypes.ChangePassword,
                ActivityModules.Authentication,
                $"{user.FullName} changed their password.");

            TempData["Success"] = "Your password was changed. Use the new password the next time you sign in.";
            return user.Role == "Admin"
                ? RedirectToAction("Index", "Home")
                : RedirectToAction("Index", "PayrollStaff");
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
    }
}
