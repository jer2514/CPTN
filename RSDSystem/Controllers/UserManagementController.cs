using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;
using RSDSystem.Validation;
using RSDSystem.Services;
using BCrypt.Net;

namespace RSDSystem.Controllers
{
    /// <summary>
    /// Admin: create/edit/deactivate login accounts (Admin or PayrollStaff).
    /// New PayrollStaff names appear in Project "Assigned to Payroll Staff".
    /// Passwords are hashed with BCrypt before save. Photos go under wwwroot/Uploads/users.
    /// </summary>
    public class UserManagementController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ActivityLogService _logs;

        public UserManagementController(PayrollDbContext db, IWebHostEnvironment env, ActivityLogService logs)
        {
            _db = db;
            _env = env;
            _logs = logs;
        }

        public static readonly string[] Roles = new[] { "Admin", "PayrollStaff" };

        /// <summary>
        /// GET /UserManagement. Admin User Management list with search, sort, and 10 users per page.
        /// Search matches first name, last name, email, or role. The Add User button goes to Create.
        /// </summary>
        /// <returns>The user list view for the current page.</returns>
        public async Task<IActionResult> Index(string? search, string? sortBy, int page = 1)
        {
            const int pageSize = 10;

            var query = _db.Users
                           .Where(u => u.FirstName != null && u.LastName != null && u.Role != null)
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
                _ => query.OrderByDescending(u => u.CreatedAt)
            };

            var totalCount = await query.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            ViewBag.Search = search;
            ViewBag.SortBy = sortBy;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.PageTitle = "User Management";

            return View(items);
        }

        /// <summary>
        /// GET /UserManagement/Create. Opens the Add User form from the list page button.
        /// </summary>
        /// <returns>An empty User form with Admin and PayrollStaff role choices.</returns>
        public IActionResult Create()
        {
            ViewBag.PageTitle = "Add User";
            return View(new User { Role = "PayrollStaff" });
        }

        /// <summary>
        /// POST /UserManagement/Create. Save on the Add User form.
        /// Hashes the password, assigns a user code, stores an optional photo, then returns to the list.
        /// Duplicate username or email is rejected. Names are title-cased before save.
        /// </summary>
        /// <returns>The list after a successful save, or the form with validation errors.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(User user, string Password,
            string ConfirmPassword, IFormFile? photo)
        {
            ModelState.Remove("PasswordHash");
            ModelState.Remove("FullName");
            ModelState.Remove("Age");
            ModelState.Remove("UserCode");
            user.Role = "PayrollStaff";

            if (string.IsNullOrWhiteSpace(Password))
                ModelState.AddModelError("Password", "Password is required.");
            else if (!InputRules.TryValidateStaffPassword(Password, out var passwordError))
                ModelState.AddModelError("Password", passwordError!);

            if (Password != ConfirmPassword)
                ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");

            if (!InputRules.TryValidatePhoto(photo, out var photoError) && photoError != null)
                ModelState.AddModelError("photo", photoError);

            var username = user.Username?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(username))
            {
                var usernameTaken = await _db.Users
                    .AnyAsync(u => u.Username == username);
                if (usernameTaken)
                    ModelState.AddModelError("Username", "This username is already taken.");
            }

            var email = user.Email?.Trim();
            if (!string.IsNullOrEmpty(email))
            {
                var emailTaken = await _db.Users
                    .AnyAsync(u => u.Email == email);
                if (emailTaken)
                    ModelState.AddModelError("Email", "This email is already registered.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.PageTitle = "Add User";
                user.Role = "PayrollStaff";
                return View(user);
            }

            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            user.FirstName = ti.ToTitleCase((user.FirstName ?? string.Empty).Trim().ToLower());
            user.LastName = ti.ToTitleCase((user.LastName ?? string.Empty).Trim().ToLower());
            user.MiddleInitial = string.IsNullOrWhiteSpace(user.MiddleInitial)
                ? null
                : user.MiddleInitial.Trim().ToUpperInvariant();
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password ?? string.Empty);
            user.UserId = 0;
            user.UserCode = GenerateUserCode();
            user.CreatedAt = DateTime.Now;

            if (photo != null && photo.Length > 0)
                user.PhotoPath = await SavePhotoAsync(photo);

            _db.Users.Add(user);
            user.UserCode = await GenerateUserCodeAsync();
            await _db.SaveChangesAsync();
            await _logs.LogAsync(
                ActivityTypes.CreateUser,
                ActivityModules.UserManagement,
                $"Added payroll staff user {user.FullName} ({user.Username}).",
                relatedId: user.UserId);
            TempData["Success"] = "User added successfully.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Build a yyNNNN user code from how many codes already start with this year.
        /// Create calls this after Add so the sequence includes the new row being saved.
        /// </summary>
        private async Task<string> GenerateUserCodeAsync()
        {
            var year = DateTime.Now.ToString("yy");
            var count = await _db.Users.CountAsync(u => u.UserCode != null && u.UserCode.StartsWith(year));
            var seq = (count + 1).ToString().PadLeft(4, '0');
            return $"{year}{seq}";
        }

        /// <summary>
        /// GET /UserManagement/Edit/{id}. Opens the Edit User form from a row's Edit button.
        /// </summary>
        /// <returns>The filled form, or 404 if the user id does not exist.</returns>
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null) return NotFound();

            ViewBag.PageTitle = "Edit User";
            return View(user);
        }

        /// <summary>
        /// POST /UserManagement/Edit. Save on the Edit User form.
        /// Password is optional; a blank NewPassword keeps the old hash. If this is the signed-in account,
        /// session FullName/Role/PhotoPath are refreshed so the sidebar updates immediately.
        /// </summary>
        /// <returns>The user list after save, or the form with validation errors.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(User user, string? NewPassword,
    string? ConfirmPassword, IFormFile? photo)
        {
            ModelState.Remove("PasswordHash");
            ModelState.Remove("FullName");
            ModelState.Remove("Age");
            ModelState.Remove("UserCode");

            var existing = await _db.Users.FindAsync(user.UserId);
            if (existing == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(NewPassword))
            {
                if (string.Equals(existing.Role, "PayrollStaff", StringComparison.OrdinalIgnoreCase))
                {
                    if (!InputRules.TryValidateStaffPassword(NewPassword, out var passwordError))
                        ModelState.AddModelError("NewPassword", passwordError!);
                }
                else if (NewPassword.Length < 8)
                {
                    ModelState.AddModelError("NewPassword", "Password must be at least 8 characters.");
                }

                if (NewPassword != ConfirmPassword)
                    ModelState.AddModelError("ConfirmPassword", "Passwords do not match.");
            }

            if (!InputRules.TryValidatePhoto(photo, out var photoError) && photoError != null)
                ModelState.AddModelError("photo", photoError);

            var username = user.Username?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(username))
            {
                var usernameTaken = await _db.Users
                    .AnyAsync(u => u.Username == username && u.UserId != user.UserId);
                if (usernameTaken)
                    ModelState.AddModelError("Username", "This username is already taken.");
            }

            var email = user.Email?.Trim();
            if (!string.IsNullOrEmpty(email))
            {
                var emailTaken = await _db.Users
                    .AnyAsync(u => u.Email == email && u.UserId != user.UserId);
                if (emailTaken)
                    ModelState.AddModelError("Email", "This email is already registered.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.PageTitle = "Edit User";
                user.Role = existing.Role;
                user.PhotoPath = existing.PhotoPath;
                user.UserCode = existing.UserCode;
                return View(user);
            }

            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            existing.FirstName = ti.ToTitleCase((user.FirstName ?? string.Empty).Trim().ToLower());
            existing.LastName = ti.ToTitleCase((user.LastName ?? string.Empty).Trim().ToLower());
            existing.MiddleInitial = string.IsNullOrWhiteSpace(user.MiddleInitial)
                ? null
                : user.MiddleInitial.Trim().ToUpperInvariant();
            existing.DateOfBirth = user.DateOfBirth;
            existing.Gender = user.Gender;
            existing.Username = username;
            existing.Email = email;
            existing.ContactNumber = user.ContactNumber?.Trim();
            existing.Address = user.Address?.Trim();

            if (!string.IsNullOrWhiteSpace(NewPassword))
                existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(NewPassword);

            if (photo != null && photo.Length > 0)
                existing.PhotoPath = await SavePhotoAsync(photo);

            await _db.SaveChangesAsync();
            await _logs.LogAsync(
                ActivityTypes.EditUser,
                ActivityModules.UserManagement,
                $"Updated user {existing.FullName} ({existing.Username}).",
                relatedId: existing.UserId);

            // If the account being edited is the one currently logged in,
            // refresh Session so the sidebar reflects the change immediately.
            var sessionUserId = HttpContext.Session.GetString("UserId");
            if (sessionUserId == existing.UserId.ToString())
            {
                HttpContext.Session.SetString("FullName", existing.FullName);
                HttpContext.Session.SetString("Role", existing.Role);

                if (!string.IsNullOrEmpty(existing.PhotoPath))
                    HttpContext.Session.SetString("PhotoPath", existing.PhotoPath);
                else
                    HttpContext.Session.Remove("PhotoPath");
            }

            TempData["Success"] = "User updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// POST /UserManagement/Delete/{id}. Row Delete button. Permanently removes the login account.
        /// </summary>
        /// <returns>A redirect back to the user list.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            TempData["Error"] = "Users cannot be deleted. Set payroll staff inactive to stop them from logging in.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Save an uploaded photo under wwwroot/uploads/users with a new GUID file name.
        /// Create and Edit call this when a photo file is present.
        /// </summary>
        /// <returns>The public URL path stored on User.PhotoPath.</returns>
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BulkDelete(List<int> selectedIds)
        {
            TempData["Error"] = "Users cannot be deleted. Set payroll staff inactive to stop them from logging in.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// POST /UserManagement/ToggleStatus/{id}. Active/Inactive switch on a user row.
        /// Inactive users fail the Login IsActive filter and cannot sign in.
        /// </summary>
        /// <returns>A redirect back to the user list.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var user = await _db.Users.FindAsync(id);
            if (user != null)
            {
                if (user.IsActive && await IsLastAdminAsync(user))
                {
                    TempData["Error"] = "The system must keep one active admin account.";
                    return RedirectToAction(nameof(Index));
                }

                user.IsActive = !user.IsActive;
                await _db.SaveChangesAsync();
                TempData["Success"] = user.IsActive
                    ? "User is now active and can log in."
                    : "User is now inactive and cannot log in.";
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> IsLastAdminAsync(User user)
        {
            if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                return false;
            return !await _db.Users.AnyAsync(u =>
                u.UserId != user.UserId
                && u.Role == "Admin"
                && u.IsActive);
        }

        private string GenerateUserCode()
        {
            string yearPrefix = DateTime.Now.ToString("yy");

            var lastCode = _db.Users
                .Where(u => u.UserCode.StartsWith(yearPrefix))
                .OrderByDescending(u => u.UserCode)
                .Select(u => u.UserCode)
                .FirstOrDefault();

            int nextSeq = 1;
            if (lastCode != null && lastCode.Length == 6)
            {
                var seqPart = lastCode.Substring(2);
                if (int.TryParse(seqPart, out int lastSeq))
                    nextSeq = lastSeq + 1;
            }

            return yearPrefix + nextSeq.ToString("D4");
        }
    }
}
