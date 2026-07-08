using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

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
        public async Task<IActionResult> Index(string? search, string? sortBy)
        {
            var query = _db.Employees
                           .Include(e => e.Project)
                           .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(e =>
                    e.FirstName.Contains(s) ||
                    e.LastName.Contains(s) ||
                    e.JobClassification.Contains(s));
            }

            query = sortBy switch
            {
                "lastname" => query.OrderBy(e => e.LastName),
                "job" => query.OrderBy(e => e.JobClassification),
                _ => query.OrderBy(e => e.EmployeeId)
            };

            ViewBag.Search = search;
            ViewBag.SortBy = sortBy;

            return View(await query.ToListAsync());
        }

        // GET /Employee/Create
        public IActionResult Create()
        {
            ViewBag.JobClassifications = JobClassifications;
            ViewBag.Projects = _db.Projects
                                  .Where(p => p.Status == "Active")
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

            if (!ModelState.IsValid)
            {
                ViewBag.JobClassifications = JobClassifications;
                ViewBag.Projects = _db.Projects
                                      .Where(p => p.Status == "Active")
                                      .OrderBy(p => p.ProjectName)
                                      .ToList();
                return View(emp);
            }

            emp.EmployeeId = 0;
            CapitalizeEmployee(emp);

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
                                  .Where(p => p.Status == "Active")
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

            if (!ModelState.IsValid)
            {
                ViewBag.JobClassifications = JobClassifications;
                ViewBag.Projects = _db.Projects
                                      .Where(p => p.Status == "Active")
                                      .OrderBy(p => p.ProjectName)
                                      .ToList();
                return View(emp);
            }

            var existing = await _db.Employees.FindAsync(emp.EmployeeId);
            if (existing == null) return NotFound();

            existing.FirstName = emp.FirstName;
            existing.LastName = emp.LastName;
            existing.MiddleInitial = emp.MiddleInitial?.Trim().ToUpper();
            existing.DateOfBirth = emp.DateOfBirth;
            existing.Gender = emp.Gender;
            existing.Address = emp.Address;
            existing.Email = emp.Email;
            existing.ContactNumber = emp.ContactNumber;
            existing.JobClassification = emp.JobClassification;
            existing.ProjectId = emp.ProjectId;  // ← saves project assignment

            if (photo != null && photo.Length > 0)
                existing.PhotoPath = await SavePhotoAsync(photo);

            await _db.SaveChangesAsync();
            TempData["Success"] = "Employee updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        private static void CapitalizeEmployee(Employee emp)
        {
            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            emp.FirstName = ti.ToTitleCase(emp.FirstName.Trim().ToLower());
            emp.LastName = ti.ToTitleCase(emp.LastName.Trim().ToLower());
            emp.MiddleInitial = emp.MiddleInitial?.Trim().ToUpper();
        }

        // POST /Employee/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var emp = await _db.Employees.FindAsync(id);
            if (emp != null)
            {
                _db.Employees.Remove(emp);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Employee deleted.";
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

            _db.Employees.RemoveRange(employees);
            await _db.SaveChangesAsync();
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
            return RedirectToAction("Edit", new { id });
        }
    }
}