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

        public AccountController(PayrollDbContext db)
        {
            _db = db;
        }

        /// <summary>Show the login form, or skip it if a session already exists.</summary>
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            var role = HttpContext.Session.GetString("Role");
            if (role == "Admin") return RedirectToAction("Index", "Home");
            if (role == "PayrollStaff") return RedirectToAction("Index", "PayrollStaff");

            return View();
        }

        /// <summary>Validate username/password and start a session.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string Username, string Password, string? returnUrl)
        {
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


        // GET: /Account/Logout
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