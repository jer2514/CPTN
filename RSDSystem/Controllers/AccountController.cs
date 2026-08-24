using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    /// <summary>
    /// Login and logout. Default landing page of the site.
    ///
    /// POST Login: look up active User by username → BCrypt-check password →
    /// write Session (UserId, FullName, Role, PhotoPath) →
    /// Admin goes to Home, PayrollStaff goes to PayrollStaff (to-do list).
    /// Inactive users cannot sign in (IsActive filter).
    /// </summary>
    public class AccountController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly ActivityLogService _logs;

        public AccountController(PayrollDbContext db, ActivityLogService logs)
        {
            _db = db;
            _logs = logs;
        }

        /// <summary>
        /// Show the login form at GET /Account/Login (the site landing page).
        /// If a session already exists, skip the form and send Admin to Home or staff to the to-do list.
        /// </summary>
        /// <param name="returnUrl">Optional URL after login. Not used for the role-based redirects.</param>
        /// <returns>The login view, or a redirect to Home or PayrollStaff.</returns>
        [HttpGet]
        public IActionResult Login(string? returnUrl = null, int? inactive = null)
        {
            // Already signed in: send each role to its dashboard instead of showing Login again.
            var role = HttpContext.Session.GetString("Role");
            if (role == "Admin") return RedirectToAction("Index", "Home");
            if (role == "PayrollStaff") return RedirectToAction("Index", "PayrollStaff");

            if (inactive == 1)
                ViewBag.Error = "This account is inactive and cannot log in.";
            else if (TempData["LoginError"] is string loginError)
                ViewBag.Error = loginError;

            return View();
        }

        /// <summary>
        /// Validate username/password from the Login button and start a session.
        /// Looks up an active User, checks the BCrypt hash, then calls SignIn.
        /// Admin is sent to Home; PayrollStaff is sent to the to-do list. Failed logins stay on the form.
        /// </summary>
        /// <returns>A redirect to the role dashboard, or the login view with an error.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string Username, string Password, string? returnUrl)
        {
            // IsActive filters out deactivated accounts so they cannot sign in.
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
            return user.Role == "Admin"
                ? RedirectToAction("Index", "Home")
                : RedirectToAction("Index", "PayrollStaff");
        }

        /// <summary>
        /// Store who is logged in. AuthCheckFilter reads these session keys on every request.
        /// Called after a successful password check. PhotoPath is removed when the user has no photo.
        /// </summary>
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


        /// <summary>
        /// Sign out from GET /Account/Logout (sidebar Logout).
        /// Clears the auth cookie and session, then returns to the login page.
        /// </summary>
        /// <returns>A redirect to /Account/Login with a success message.</returns>
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await _logs.LogAsync(
                ActivityTypes.Logout,
                ActivityModules.Authentication,
                "User signed out.");
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();

            // Optional: show a message after logout
            TempData["Success"] = "You have been logged out.";

            // Redirect to the login page if you have one, otherwise to Home/Index.
            return RedirectToAction("Login", "Account");
        }
    }
}
