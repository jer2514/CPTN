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
    public class AccountController : Controller
    {
        private readonly PayrollDbContext _db;

        public AccountController(PayrollDbContext db)
        {
            _db = db;
        }

        // GET: /Account/Login
        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            var role = HttpContext.Session.GetString("Role");
            if (role == "Admin") return RedirectToAction("Index", "Home");
            if (role == "PayrollStaff") return RedirectToAction("Index", "PayrollStaff");

            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string Username, string Password, string? returnUrl)
        {
            if (Username == "admin" && Password == "Admin@123")
            {
                SignIn(0, "Louis Bloom", "Admin");
                return RedirectToAction("Index", "Home");
            }

            if (Username == "payroll.staff" && Password == "Staff@123")
            {
                SignIn(0, "Patrick Bateman", "PayrollStaff");
                return RedirectToAction("Index", "PayrollStaff");
            }

            // Database authentication
            var user = await _db.Users
                                .FirstOrDefaultAsync(u => u.Username == Username && u.IsActive);

            if (user != null && BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash))
            {
                SignIn(user.UserId, user.FullName, user.Role);
                return user.Role == "Admin"
                    ? RedirectToAction("Index", "Home")
                    : RedirectToAction("Index", "PayrollStaff");
            }

            ViewBag.Error = "Invalid username or password.";
            return View();
        }

        private void SignIn(int userId, string fullName, string role)
        {
            HttpContext.Session.SetString("UserId", userId.ToString());
            HttpContext.Session.SetString("FullName", fullName);
            HttpContext.Session.SetString("Role", role);
        }
        

        // GET: /Account/Logout
        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }
    }
}