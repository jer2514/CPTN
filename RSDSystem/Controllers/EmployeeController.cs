using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Validation;

namespace RSDSystem.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly IWebHostEnvironment _env;

        public EmployeeController(PayrollDbContext db, IWebHostEnvironment env)
        {
            _db = db;
            _env = env;
        }

        public static readonly string[] JobClassifications = new[]
        {
            "Labor", "Foreman", "Applicator", "Leadman",
            "Safety Officer", "Welder", "Project Engineer",
            "Mason", "Carpenter", "Electrician"
        };

        // GET /Employee
        public async Task<IActionResult> Index(string? search, string? sortBy, int page = 1)
        {
            const int pageSize = 10;

            var query = _db.Employees
                           .Include(e => e.Project)
                           .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(e =>
                    e.FirstName.Contains(s) ||
                    e.LastName.Contains(s) ||
                    e.JobClassification.Contains(s) ||
                    (e.EmployeeCode != null && e.EmployeeCode.Contains(s)) ||
                    (e.EmployeeCode != null && e.EmployeeCode.Contains(s.Replace("-", ""))) ||
                    (e.Email != null && e.Email.Contains(s)) ||
                    (e.Project != null && e.Project.ProjectName.Contains(s)));
            }

            query = sortBy switch
            {
                "lastname" => query.OrderBy(e => e.LastName),
                "job" => query.OrderBy(e => e.JobClassification),
                "assigned" => query.OrderBy(e => e.Project!.ProjectName),
                "status" => query.OrderByDescending(e => e.IsActive),
                _ => query.OrderByDescending(e => e.DateAdded).ThenByDescending(e => e.EmployeeId)
            };

            var totalCount = await query.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
            page = Math.Clamp(page, 1, totalPages);

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            ViewBag.Search = search;
            ViewBag.SortBy = sortBy;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(items);
        }

        // GET /Employee/Create
        public IActionResult Create()
        {
            ViewBag.JobClassifications = JobClassifications;
            ViewBag.Projects = _db.Projects
                                  .Ongoing()
                                  .OrderBy(p => p.ProjectName)
                                  .ToList();
            return View(new Employee());
        }

        // POST /Employee/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee emp, IFormFile? photo)
        {
            ModelState.Remove("FullName");
            ModelState.Remove("Age");
            ModelState.Remove("Project");
            ModelState.Remove("EmployeeCode");

            NormalizeEmployee(emp);

            if (string.IsNullOrWhiteSpace(emp.Email))
                ModelState.Remove("Email");

            ClarifyNumericErrors(ModelState);
            ValidatePhoto(photo);

            if (await IsDuplicateEmployeeAsync(emp))
            {
                ModelState.AddModelError(string.Empty,
                    "This employee already exists. An employee with the same name and date of birth, or the same email, is already in the system.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.JobClassifications = JobClassifications;
                ViewBag.Projects = _db.Projects
                                      .Ongoing()
                                      .OrderBy(p => p.ProjectName)
                                      .ToList();
                return View(emp);
            }

            emp.EmployeeId = 0;
            emp.DateAdded = DateTime.Now;

            var newCode = await GenerateEmployeeCodeAsync();
            if (string.IsNullOrWhiteSpace(newCode))
                throw new InvalidOperationException("GenerateEmployeeCodeAsync returned an empty code — check the method logic.");

            emp.EmployeeCode = newCode;

            if (photo != null && photo.Length > 0)
                emp.PhotoPath = await SavePhotoAsync(photo);

            _db.Employees.Add(emp);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Employee added successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET /Employee/Edit/{id}
        public async Task<IActionResult> Edit(int id)
        {
            var emp = await _db.Employees.FindAsync(id);
            if (emp == null) return View("EmployeeNotFound");

            ViewBag.JobClassifications = JobClassifications;
            ViewBag.Projects = _db.Projects
                                  .Ongoing()
                                  .OrderBy(p => p.ProjectName)
                                  .ToList();
            return View(emp);
        }

        // POST /Employee/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Employee emp, IFormFile? photo)
        {
            ModelState.Remove("FullName");
            ModelState.Remove("Age");
            ModelState.Remove("Project");
            ModelState.Remove("EmployeeCode");

            NormalizeEmployee(emp);

            if (string.IsNullOrWhiteSpace(emp.Email))
                ModelState.Remove("Email");

            ClarifyNumericErrors(ModelState);
            ValidatePhoto(photo);

            if (await IsDuplicateEmployeeAsync(emp, emp.EmployeeId))
            {
                ModelState.AddModelError(string.Empty,
                    "This employee already exists. Another employee with the same name and date of birth, or the same email, is already in the system.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.JobClassifications = JobClassifications;
                ViewBag.Projects = _db.Projects
                                      .Ongoing()
                                      .OrderBy(p => p.ProjectName)
                                      .ToList();
                return View(emp);
            }

            var existing = await _db.Employees.FindAsync(emp.EmployeeId);
            if (existing == null) return NotFound();

            existing.FirstName = emp.FirstName;
            existing.LastName = emp.LastName;
            existing.MiddleInitial = emp.MiddleInitial;
            existing.DateOfBirth = emp.DateOfBirth;
            existing.Gender = emp.Gender;
            existing.Address = emp.Address;
            existing.Email = emp.Email;
            existing.ContactNumber = emp.ContactNumber;
            existing.JobClassification = emp.JobClassification;
            existing.DailyRate = emp.DailyRate;
            existing.RatePerHour = emp.RatePerHour;
            existing.ProjectId = emp.ProjectId;
            // existing.EmployeeCode intentionally untouched — never re-generated on edit

            if (photo != null && photo.Length > 0)
                existing.PhotoPath = await SavePhotoAsync(photo);

            await _db.SaveChangesAsync();
            TempData["Success"] = "Employee updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        private static void NormalizeEmployee(Employee emp)
        {
            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            emp.FirstName = TitleCase(emp.FirstName, ti);
            emp.LastName = TitleCase(emp.LastName, ti);
            emp.MiddleInitial = string.IsNullOrWhiteSpace(emp.MiddleInitial)
                ? null
                : emp.MiddleInitial.Trim().ToUpperInvariant();

            emp.Email = string.IsNullOrWhiteSpace(emp.Email) ? null : emp.Email.Trim();
            emp.ContactNumber = emp.ContactNumber?.Trim();
            emp.Address = emp.Address?.Trim();
            emp.Gender = string.IsNullOrWhiteSpace(emp.Gender) ? null : emp.Gender.Trim();
            emp.JobClassification = string.IsNullOrWhiteSpace(emp.JobClassification)
                ? string.Empty
                : emp.JobClassification.Trim();

            emp.Email = emp.Email?.Trim();
            emp.ContactNumber = emp.ContactNumber?.Trim();
            emp.Address = emp.Address?.Trim();
            emp.Gender = string.IsNullOrWhiteSpace(emp.Gender) ? null : emp.Gender.Trim();
            emp.JobClassification = emp.JobClassification?.Trim() ?? string.Empty;

        }

        private static string TitleCase(string? value, System.Globalization.TextInfo ti)
        {

            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return ti.ToTitleCase(value.Trim().ToLowerInvariant());

            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            return ti.ToTitleCase(value.Trim().ToLower());

        }

        private void ValidatePhoto(IFormFile? photo)
        {
            if (!InputRules.TryValidatePhoto(photo, out var error) && error != null)
                ModelState.AddModelError("photo", error);
        }

        private static void ClarifyNumericErrors(ModelStateDictionary modelState)
        {
            foreach (var key in new[] { "DailyRate", "RatePerHour" })
            {
                if (!modelState.TryGetValue(key, out var entry)) continue;

                var hasBindingError = entry.Errors.Any(e =>
                    e.Exception != null ||
                    e.ErrorMessage.Contains("is not valid", StringComparison.OrdinalIgnoreCase) ||
                    e.ErrorMessage.Contains("is invalid", StringComparison.OrdinalIgnoreCase));

                if (!hasBindingError) continue;

                entry.Errors.Clear();
                var label = key == "DailyRate" ? "Rate per day" : "Rate per hour";
                entry.Errors.Add($"{label} is required.");
            }
        }

        // Returns true if another employee already has the same
        // (FirstName + LastName + DateOfBirth) OR the same Email.
        private async Task<bool> IsDuplicateEmployeeAsync(Employee emp, int excludeId = 0)
        {
            var query = _db.Employees.Where(e => e.EmployeeId != excludeId);

            bool nameDobMatch = await query.AnyAsync(e =>
                e.FirstName == emp.FirstName &&
                e.LastName == emp.LastName &&
                e.DateOfBirth == emp.DateOfBirth);

            if (nameDobMatch) return true;

            if (!string.IsNullOrWhiteSpace(emp.Email))
            {
                var email = emp.Email.Trim().ToLower();
                bool emailMatch = await query.AnyAsync(e =>
                    e.Email != null && e.Email.ToLower() == email);

                if (emailMatch) return true;
            }

            return false;
        }

        // POST /Employee/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var emp = await _db.Employees.FindAsync(id);
            if (emp != null)
            {
                try
                {
                    _db.Employees.Remove(emp);
                    await _db.SaveChangesAsync();
                    TempData["Success"] = "Employee deleted.";
                }
                catch (DbUpdateException)
                {
                    TempData["Error"] = $"{emp.FullName} cannot be deleted because they have existing payroll records. " +
                                         "Set their status to Inactive instead to keep payroll history intact.";
                }
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<string> SavePhotoAsync(IFormFile photo)
        {
            var folder = Path.Combine(_env.WebRootPath, "uploads", "employees");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(photo.FileName)}";
            var filePath = Path.Combine(folder, fileName);
            using var stream = new FileStream(filePath, FileMode.Create);
            await photo.CopyToAsync(stream);
            return $"/uploads/employees/{fileName}";
        }

        // Unique 5-digit biometric ID: 00001, 00002, ...
        private async Task<string> GenerateEmployeeCodeAsync()
        {
            var codes = await _db.Employees
                                 .Where(e => e.EmployeeCode != null && e.EmployeeCode != "")
                                 .Select(e => e.EmployeeCode!)
                                 .ToListAsync();

            int maxSeq = 0;
            foreach (var code in codes)
            {
                var seq = EmployeeIds.Sequence(code);
                if (seq.HasValue && seq.Value > maxSeq)
                    maxSeq = seq.Value;
            }

            var next = maxSeq + 1;
            var candidate = next.ToString("D5");
            while (codes.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                next++;
                candidate = next.ToString("D5");
            }

            return candidate;
        }

        //delete multiple employees
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(List<int> selectedIds)
        {
            if (selectedIds == null || selectedIds.Count == 0)
                return RedirectToAction(nameof(Index));

            var employees = _db.Employees
                               .Where(e => selectedIds.Contains(e.EmployeeId))
                               .ToList();

            var blocked = new List<string>();

            foreach (var emp in employees)
            {
                _db.Employees.Remove(emp);
                try
                {
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    _db.Entry(emp).State = EntityState.Unchanged;
                    blocked.Add(emp.FullName);
                }
            }

            TempData[blocked.Any() ? "Error" : "Success"] = blocked.Any()
                ? $"Could not delete: {string.Join(", ", blocked)} — they have existing payroll records."
                : "Selected employees deleted.";

            return RedirectToAction(nameof(Index));
        }

        // POST /UserManagement/ToggleStatus/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var emp = await _db.Employees.FindAsync(id);
            if (emp != null)
            {
                emp.IsActive = !emp.IsActive;
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}