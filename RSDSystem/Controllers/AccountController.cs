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
            ViewBag.ReturnUrl = returnUrl;
            return View();
        }

        // POST: /Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string Username, string Password, string? returnUrl)
        {
            // Normalize inputs
            Username = Username?.Trim() ?? string.Empty;
            Password = Password ?? string.Empty;

            if (Username.Length == 0 || Password.Length == 0)
            {
                TempData["Error"] = "Invalid username or password.";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            // Try case-sensitive lookup first, then case-insensitive fallback
            var user = await _db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Username == Username);

            if (user == null)
            {
                // EF Core will translate ToLower() to LOWER(...) in SQL
                var unameLower = Username.ToLower();
                user = await _db.Users
                    .AsNoTracking()
                    .SingleOrDefaultAsync(u => u.Username.ToLower() == unameLower);
            }

            if (user == null || !user.IsActive || string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                TempData["Error"] = "Invalid username or password.";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            // Debug info -> console (safe for local dev only)
            Console.WriteLine($"[Login] Attempt user='{Username}' dbUser='{user.Username}' hashLen={(user.PasswordHash?.Length ?? 0)}");

            bool ok = false;
            try
            {
                ok = BCrypt.Net.BCrypt.Verify(Password, user.PasswordHash);
            }
            catch (Exception ex)
            {
                // Log verify errors (hash format issues, etc.)
                Console.WriteLine("[Login] BCrypt.Verify threw: " + ex.Message);
            }

            Console.WriteLine($"[Login] Verify result: {ok}");

            if (!ok)
            {
                TempData["Error"] = "Invalid username or password.";
                ViewBag.ReturnUrl = returnUrl;
                return View();
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.GivenName, user.FirstName ?? string.Empty),
                new Claim(ClaimTypes.Surname, user.LastName ?? string.Empty),
                new Claim(ClaimTypes.Role, user.Role ?? "PayrollStaff")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
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