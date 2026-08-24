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

        /// <summary>
        /// Receives the payroll database from dependency injection so Login can look up users.
        /// </summary>
        public AccountController(PayrollDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Show the login form at GET /Account/Login (the site landing page).
        /// If a session already exists, skip the form and send Admin to Home or staff to the to-do list.
        /// </summary>
        /// <param name="returnUrl">Optional URL after login. Not used for the role-based redirects.</param>
        /// <returns>The login view, or a redirect to Home or PayrollStaff.</returns>
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            // Already signed in: send each role to its dashboard instead of showing Login again.
            var role = HttpContext.Session.GetString("Role");
            if (role == "Admin") return RedirectToAction("Index", "Home");
            if (role == "PayrollStaff") return RedirectToAction("Index", "PayrollStaff");

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
                                .FirstOrDefaultAsync(u => u.Username == Username && u.IsActive);

            if (user != null && BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash))
            {
                SignIn(user.UserId, user.FullName, user.Role, user.PhotoPath);
                return user.Role == "Admin"
                    ? RedirectToAction("Index", "Home")
                    : RedirectToAction("Index", "PayrollStaff");
            }

            ViewBag.Error = "Invalid username or password.";
            return View();
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
            // Sign out the cookie authentication and clear session data.
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            HttpContext.Session.Clear();

            // Optional: show a message after logout
            TempData["Success"] = "You have been logged out.";

            // Redirect to the login page if you have one, otherwise to Home/Index.
            return RedirectToAction("Login", "Account");
        }
    }
}
