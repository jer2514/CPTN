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
    public class AccountController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly ActivityLogService _logs;

        public AccountController(PayrollDbContext db, ActivityLogService logs)
        {
            _db = db;
            _logs = logs;
        }

        // GET: /Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null, int? inactive = null)
        {
            var role = HttpContext.Session.GetString("Role");
            if (role == "Admin") return RedirectToAction("Index", "Home");
            if (role == "PayrollStaff") return RedirectToAction("Index", "PayrollStaff");

            if (inactive == 1)
                ViewBag.Error = "This account is inactive and cannot log in.";
            else if (TempData["LoginError"] is string loginError)
                ViewBag.Error = loginError;

            return View();
        }

        // POST: /Account/Login
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


        // GET: /Account/Logout
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