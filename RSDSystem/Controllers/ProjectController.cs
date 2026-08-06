using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Controllers
{
    public class ProjectController : Controller
    {
        private readonly PayrollDbContext _db;

        public ProjectController(PayrollDbContext db)
        {
            _db = db;
        }

        private void PopulateViewBag()
        {
            ViewBag.TypeOfServiceOptions = TypeOfServiceOptions.All;
            ViewBag.DistributionOptions = PayrollDistributionOptions.All;
            ViewBag.PayrollStaffList = _db.Users
                                              .Where(u => u.Role == "PayrollStaff" && u.IsActive)
                                              .Select(u => u.FullName)
                                              .ToList();
        }

        // GET /Project
        public async Task<IActionResult> Index(string? search)
        {
            var query = _db.Projects.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(p =>
                    p.ProjectName.Contains(s) ||
                    (p.Location != null && p.Location.Contains(s)) ||
                    (p.TypeOfService != null && p.TypeOfService.Contains(s)));
            }

            ViewBag.Search = search;
            ViewBag.PageTitle = "Projects";

            return View(await query.OrderBy(p => p.ProjectName).ToListAsync());
        }

        // GET /Project/Details/{id}
        public async Task<IActionResult> Details(int id)
        {
            var project = await _db.Projects
                           .Include(p => p.MonthlyBudgets)
                           .FirstOrDefaultAsync(p => p.ProjectId == id);

            if (project == null) return NotFound();

            var employees = await _db.Employees
                                     .Where(e => e.ProjectId == id)
                                     .ToListAsync();

            var unassignedEmployees = await _db.Employees
                                     .Where(e => e.ProjectId == null)
                                     .OrderBy(e => e.FirstName)
                                     .ToListAsync();

            ViewBag.Employees = employees;
            ViewBag.UnassignedEmployees = unassignedEmployees;
            ViewBag.PageTitle = "View Project";
            return View(project);
        }

        // GET /Project/Create
        public IActionResult Create()
        {
            ViewBag.PageTitle = "Add Project";
            PopulateViewBag();
            return View(new Project());
        }

        // POST /Project/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Project project,
            List<string> MonthYears, List<decimal> MonthAmounts)
        {
            ModelState.Remove("MonthlyBudgets");

            if (!ModelState.IsValid)
            {
                ViewBag.PageTitle = "Add Project";
                PopulateViewBag();
                return View(project);
            }

            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            project.ProjectName = ti.ToTitleCase(project.ProjectName.Trim().ToLower());
            project.ProjectId = 0;

            // Add monthly budget rows from date range
            for (int i = 0; i < MonthYears.Count; i++)
            {
                if (DateTime.TryParse(MonthYears[i], out var dt))
                {
                    project.MonthlyBudgets.Add(new ProjectMonthlyBudget
                    {
                        MonthYear = dt.ToString("MMMM yyyy"),
                        MonthDate = dt,
                        Amount = i < MonthAmounts.Count ? MonthAmounts[i] : 0
                    });
                }
            }

            _db.Projects.Add(project);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // GET /Project/Edit/{id}
        public async Task<IActionResult> Edit(int id)
        {
            var project = await _db.Projects
                                   .Include(p => p.MonthlyBudgets.OrderBy(m => m.MonthDate))
                                   .FirstOrDefaultAsync(p => p.ProjectId == id);

            if (project == null) return NotFound();

            var employees = await _db.Employees
                                     .Where(e => e.ProjectId == id)
                                     .ToListAsync();

            ViewBag.Employees = employees;
            ViewBag.PageTitle = "Edit Project";
            PopulateViewBag();
            return View(project);
        }

        // POST /Project/Edit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Project project,
            List<string> MonthYears, List<decimal> MonthAmounts)
        {
            ModelState.Remove("MonthlyBudgets");

            if (!ModelState.IsValid)
            {
                ViewBag.PageTitle = "Edit Project";
                PopulateViewBag();
                return View(project);
            }

            var existing = await _db.Projects
                                    .Include(p => p.MonthlyBudgets)
                                    .FirstOrDefaultAsync(p => p.ProjectId == project.ProjectId);

            if (existing == null) return NotFound();

            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            existing.ProjectName = ti.ToTitleCase(project.ProjectName.Trim().ToLower());
            existing.Location = project.Location;
            existing.TypeOfService = project.TypeOfService;
            existing.StartingDate = project.StartingDate;
            existing.EstimateEndDate = project.EstimateEndDate;
            existing.PayrollBudget = project.PayrollBudget;
            existing.PayrollDistribution = project.PayrollDistribution;
            existing.AssignedPayrollStaff = project.AssignedPayrollStaff;
            existing.Status = project.Status;

            // Replace monthly budgets
            _db.ProjectMonthlyBudgets.RemoveRange(existing.MonthlyBudgets);

            for (int i = 0; i < MonthYears.Count; i++)
            {
                if (DateTime.TryParse(MonthYears[i], out var dt))
                {
                    existing.MonthlyBudgets.Add(new ProjectMonthlyBudget
                    {
                        MonthYear = dt.ToString("MMMM yyyy"),
                        MonthDate = dt,
                        Amount = i < MonthAmounts.Count ? MonthAmounts[i] : 0,
                        ProjectId = existing.ProjectId
                    });
                }
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST /Project/Delete/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var project = await _db.Projects
                                   .Include(p => p.MonthlyBudgets)
                                   .FirstOrDefaultAsync(p => p.ProjectId == id);
            if (project != null)
            {
                _db.Projects.Remove(project);
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // POST /Project/RemoveEmployee
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveEmployee(int employeeId, int projectId)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            if (emp != null)
            {
                emp.ProjectId = null;
                await _db.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Edit), new { id = projectId });
        }


        // POST /Project/AssignEmployee
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignEmployee(int employeeId, int projectId)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            if (emp == null)
                return Json(new { success = false, message = "Employee not found." });

            emp.ProjectId = projectId;
            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                employee = new
                {
                    id = emp.EmployeeId,
                    name = emp.FullName,
                    job = emp.JobClassification,
                    rate = emp.DailyRate.ToString("N2"),
                    status = emp.IsActive ? "Active" : "Inactive"
                }
            });
        }

        // POST /Project/DeactivateAndRemove
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeactivateAndRemove(int employeeId, int projectId)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            if (emp == null)
                return Json(new { success = false, message = "Employee not found." });

            emp.IsActive = false;
            emp.ProjectId = null;
            await _db.SaveChangesAsync();

            return Json(new
            {
                success = true,
                employee = new
                {
                    id = emp.EmployeeId,
                    name = emp.FullName,
                    job = emp.JobClassification
                }
            });
        }
    }
}