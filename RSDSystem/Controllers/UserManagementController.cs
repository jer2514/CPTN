using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;
using BCrypt.Net;

namespace RSDSystem.Controllers
{
    public class UserManagementController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly IWebHostEnvironment _env;

        public UserManagementController(PayrollDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public static readonly string[] Roles = new[] { "Admin", "PayrollStaff" };

        // GET /UserManagement
        public async Task<IActionResult> Index(string? search, string? sortBy)
        {
            var query = _db.Users
                           .Where(u => u.FirstName != null &&
                                        u.LastName != null &&
                                        u.Role != null)
                           .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(u =>
                    (u.FirstName != null && u.FirstName.Contains(s)) ||
                    (u.LastName != null && u.LastName.Contains(s)) ||
                    (u.Email != null && u.Email.Contains(s)) ||
                    (u.Role != null && u.Role.Contains(s)));
            }

            query = sortBy switch
            {
                "lastname" => query.OrderBy(u => u.LastName),
                "role" => query.OrderBy(u => u.Role),
                "email" => query.OrderBy(u => u.Email),
                "status" => query.OrderByDescending(u => u.IsActive),
                _ => query.OrderBy(u => u.UserId)
            };

            ViewBag.Search = search;
            ViewBag.SortBy = sortBy;
            ViewBag.PageTitle = "User Management";

            return View(await query.ToListAsync());
        }

        // GET /UserManagement/Create
        public IActionResult Create()
        {
            ViewBag.Roles = Roles;
            ViewBag.PageTitle = "Add User";
            return View(new User());
        }

        // POST /UserManagement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(User user, string Password,
            string ConfirmPassword, IFormFile? photo)
        {
            ModelState.Remove("PasswordHash");
            ModelState.Remove("FullName");
            ModelState.Remove("Age");

            if (Password != ConfirmPassword)
                ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");

            if (!ModelState.IsValid)
            {
                ViewBag.Roles = Roles;
                return View(user);
            }

            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            user.FirstName = ti.ToTitleCase(user.FirstName.Trim().ToLower());
            user.LastName = ti.ToTitleCase(user.LastName.Trim().ToLower());
            user.MiddleInitial = user.MiddleInitial?.Trim().ToUpper();
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password);
            user.UserId = 0;

            if (photo != null && photo.Length > 0)
                user.PhotoPath = await SavePhotoAsync(photo);

            _db.Users.Add(user);
            await _db.SaveChangesAsync();
            TempData["Success"] = "User added successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET /UserManagement/Edit/{id}
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();

            ViewBag.Roles = Roles;
            ViewBag.PageTitle = "Edit User";
            return View(user);
        }

        // POST /UserManagement/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(User user, string? NewPassword,
            string? ConfirmPassword, IFormFile? photo)
        {
            ModelState.Remove("PasswordHash");
            ModelState.Remove("FullName");
            ModelState.Remove("Age");

            if (!string.IsNullOrWhiteSpace(NewPassword) && NewPassword != ConfirmPassword)
                ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");

            if (!ModelState.IsValid)
            {
                ViewBag.Roles = Roles;
                return View(user);
            }

            var existing = await _db.Users.FindAsync(user.UserId);
            if (existing == null) return NotFound();

            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            existing.FirstName = ti.ToTitleCase(user.FirstName.Trim().ToLower());
            existing.LastName = ti.ToTitleCase(user.LastName.Trim().ToLower());
            existing.MiddleInitial = user.MiddleInitial?.Trim().ToUpper();
            existing.DateOfBirth = user.DateOfBirth;
            existing.Gender = user.Gender;
            existing.Username = user.Username;
            existing.Email = user.Email;
            existing.ContactNumber = user.ContactNumber;
            existing.Address = user.Address;
            existing.Role = user.Role;
            existing.IsActive = user.IsActive;

            if (!string.IsNullOrWhiteSpace(NewPassword))
                existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);

            if (photo != null && photo.Length > 0)
                existing.PhotoPath = await SavePhotoAsync(photo);

            await _db.SaveChangesAsync();
            TempData["Success"] = "User updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // POST /UserManagement/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user != null)
            {
                _db.Users.Remove(user);
                await _db.SaveChangesAsync();
                TempData["Success"] = "User deleted.";
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<string> SavePhotoAsync(IFormFile photo)
        {
            var folder = Path.Combine(_env.WebRootPath, "uploads", "users");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(photo.FileName)}";
            var filePath = Path.Combine(folder, fileName);
            using var stream = new FileStream(filePath, FileMode.Create);
            await photo.CopyToAsync(stream);
            return $"/uploads/users/{fileName}";
        }

        //delete multiple users
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(List<int> selectedIds)
        {
            if (selectedIds == null || selectedIds.Count == 0)
                return RedirectToAction(nameof(Index));

            var employees = _db.Employees
                               .Where(e => selectedIds.Contains(e.EmployeeId))
                               .ToList();

            _db.Employees.RemoveRange(employees);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user != null)
            {
                user.IsActive = !user.IsActive;
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}