using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Validation;

namespace RSDSystem.Controllers
{
    /// <summary>
    /// Admin: employee master list. Create/Edit/Delete/ToggleStatus.
    /// Employees are assigned to a Project so staff can generate their payroll.
    /// Photos go under wwwroot/Uploads/employees.
    /// </summary>
    public class EmployeeController : Controller
    {
        private readonly PayrollDbContext _db;
        private readonly IWebHostEnvironment _env;

        /// <summary>
        /// Receives the database and web root so employee photos can be saved under wwwroot/uploads/employees.
        /// </summary>
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

        /// <summary>
        /// GET /Employee. Admin employee list with search, sort, and 10 rows per page.
        /// Search matches name, job, employee code, email, or project. Add Employee opens Create.
        /// </summary>
        /// <returns>The employee list view for the current page.</returns>
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
                    (e.Project != null && e.Project.ProjectName != null && e.Project.ProjectName.Contains(s)));
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

        /// <summary>
        /// GET /Employee/Create. Opens the Add Employee form. Only ongoing projects can be assigned.
        /// </summary>
        /// <returns>An empty Employee form with job and project dropdowns.</returns>
        public IActionResult Create()
        {
            ViewBag.JobClassifications = JobClassifications;
            ViewBag.Projects = _db.Projects
                                  .Ongoing()
                                  .OrderBy(p => p.ProjectName)
                                  .ToList();
            return View(new Employee());
        }

        /// <summary>
        /// POST /Employee/Create. Save on the Add Employee form.
        /// Assigns a 5-digit biometric code, stores an optional photo, and blocks duplicate name+DOB or email.
        /// </summary>
        /// <returns>The employee list after save, or the form with validation errors.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Employee emp, IFormFile? photo)
        {
            ModelState.Remove("FullName");
            ModelState.Remove("Age");
            ModelState.Remove("Project");
            ModelState.Remove("EmployeeCode");
            ModelState.Remove("RatePerHour");

            NormalizeEmployee(emp);
            emp.RatePerHour = EmployeeRates.HourlyFromDaily(emp.DailyRate);
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
            emp.IsActive = emp.ProjectId.HasValue;

            if (photo != null && photo.Length > 0)
                emp.PhotoPath = await SavePhotoAsync(photo);

            _db.Employees.Add(emp);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Employee added successfully.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// GET /Employee/Edit/{id}. Opens the Edit Employee form from a row's Edit button.
        /// </summary>
        /// <returns>The filled form, or EmployeeNotFound when the id is missing.</returns>
        public async Task<IActionResult> Edit(int id)
        {
            var emp = await _db.Employees.Include(e => e.Project)
                       .FirstOrDefaultAsync(e => e.EmployeeId == id);

            if (emp == null) return View("EmployeeNotFound");

            ViewBag.JobClassifications = JobClassifications;
            ViewBag.Projects = _db.Projects
                                  .Ongoing()
                                  .OrderBy(p => p.ProjectName)
                                  .ToList();
            return View(emp);
        }

        /// <summary>
        /// POST /Employee/Edit. Save on the Edit Employee form.
        /// Assigning an inactive employee to a project turns them Active. Duplicate name+DOB or email is blocked.
        /// </summary>
        /// <returns>The employee list after save, or the form with validation errors.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Employee emp, IFormFile? photo)
        {
            ModelState.Remove("FullName");
            ModelState.Remove("Age");
            ModelState.Remove("Project");
            ModelState.Remove("EmployeeCode");
            ModelState.Remove("RatePerHour");

            NormalizeEmployee(emp);
            emp.RatePerHour = EmployeeRates.HourlyFromDaily(emp.DailyRate);

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
            existing.RatePerHour = EmployeeRates.HourlyFromDaily(emp.DailyRate);
            existing.ProjectId = emp.ProjectId;
            existing.IsActive = emp.ProjectId.HasValue;

            if (photo != null && photo.Length > 0)
                existing.PhotoPath = await SavePhotoAsync(photo);

            await _db.SaveChangesAsync();
            TempData["Success"] = "Employee updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Trim and title-case name fields before Create/Edit validation and save.
        /// Empty email and middle initial become null so optional fields stay optional.
        /// </summary>
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
        }

        /// <summary>
        /// Title-case a name: trim, lower, then capitalize each word. Empty input becomes "".
        /// </summary>
        private static string TitleCase(string? value, System.Globalization.TextInfo ti)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return ti.ToTitleCase(value.Trim().ToLowerInvariant());
        }

        /// <summary>
        /// Add a ModelState error when the uploaded photo fails InputRules (type or size).
        /// </summary>
        private void ValidatePhoto(IFormFile? photo)
        {
            if (!InputRules.TryValidatePhoto(photo, out var error) && error != null)
                ModelState.AddModelError("photo", error);
        }

        /// <summary>
        /// Replace ASP.NET "not valid" messages on DailyRate and RatePerHour with "is required."
        /// Binding fails the same way for blank and non-numeric input, so the form wording is clearer.
        /// </summary>
        private static void ClarifyNumericErrors(ModelStateDictionary modelState)
        {
            foreach (var key in new[] { "DailyRate" })
            {
                if (!modelState.TryGetValue(key, out var entry)) continue;

                var hasBindingError = entry.Errors.Any(e =>
                    e.Exception != null ||
                    e.ErrorMessage.Contains("is not valid", StringComparison.OrdinalIgnoreCase) ||
                    e.ErrorMessage.Contains("is invalid", StringComparison.OrdinalIgnoreCase));

                if (!hasBindingError) continue;

                entry.Errors.Clear();
                entry.Errors.Add("Rate per day is required.");
            }
        }

        /// <summary>
        /// Returns true if another employee already has the same
        /// (FirstName + LastName + DateOfBirth) OR the same Email.
        /// Create and Edit call this so the same person cannot be added twice.
        /// </summary>
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

        /// <summary>
        /// POST /Employee/Delete/{id}. Row Delete button.
        /// If payroll history exists, the delete is blocked and the admin is told to set Inactive instead.
        /// </summary>
        /// <returns>A redirect to the employee list with success or error in TempData.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(int id)
        {
            TempData["Error"] = "Employees cannot be deleted. Unassign them from a project to set them inactive.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Save an uploaded photo under wwwroot/uploads/employees with a new GUID file name.
        /// Create and Edit call this when a photo file is present.
        /// </summary>
        /// <returns>The public URL path stored on Employee.PhotoPath.</returns>
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

        /// <summary>
        /// Unique 5-digit biometric ID: 00001, 00002, ...
        /// Create assigns this before save. Uses the highest existing sequence, then skips collisions.
        /// </summary>
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

        /// <summary>
        /// POST /Employee/BulkDelete. Delete selected checkbox on the list.
        /// Each employee is deleted one at a time so payroll FK failures can be listed by name.
        /// </summary>
        /// <returns>A redirect to Index with success, or an error naming employees that still have payroll.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult BulkDelete(List<int> selectedIds)
        {
            TempData["Error"] = "Employees cannot be deleted. Unassign them from a project to set them inactive.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// POST /Employee/ToggleStatus/{id}. Active/Inactive switch on a row.
        /// Inactive employees are unassigned from their project so they drop off payroll generate lists.
        /// </summary>
        /// <returns>A redirect back to the employee list.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var emp = await _db.Employees.FindAsync(id);
            if (emp != null)
            {
                if (emp.IsActive)
                {
                    emp.IsActive = false;
                    emp.ProjectId = null;
                }
                else if (emp.ProjectId.HasValue)
                {
                    emp.IsActive = true;
                }

                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// POST /Employee/ToggleStatusAjax. Same Active/Inactive rule as ToggleStatus, for the list switch without a full reload.
        /// Clearing ProjectId when inactive keeps the employee off Generate Payroll.
        /// </summary>
        /// <returns>JSON with the new isActive flag and projectId (null when inactivated).</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatusAjax(int id)
        {
            var emp = await _db.Employees.FindAsync(id);
            if (emp == null)
                return Json(new { success = false, message = "Employee not found." });

            if (emp.IsActive)
            {
                emp.IsActive = false;
                emp.ProjectId = null;
            }
            else if (!emp.ProjectId.HasValue)
            {
                return Json(new { success = false, message = "Assign this employee to a project to make them active." });
            }
            else
            {
                emp.IsActive = true;
            }

            await _db.SaveChangesAsync();
            return Json(new
            {
                success = true,
                isActive = emp.IsActive,
                projectId = emp.ProjectId
            });
        }
    }
}
