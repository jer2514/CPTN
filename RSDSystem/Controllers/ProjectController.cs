using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Controllers
{
    /// <summary>
    /// Admin: construction projects. Assign payroll staff + employees here.
    /// Status filter (On Going / Finished / …) is applied in Index.
    /// After a project exists, Admin adds PayrollSchedule on the dashboard (PayrollController).
    /// </summary>
    public class ProjectController : Controller
    {
        private readonly PayrollDbContext _db;

        /// <summary>
        /// Receives the payroll database used for project CRUD and employee assignment.
        /// </summary>
        public ProjectController(PayrollDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Fill type-of-service, distribution, and active PayrollStaff dropdowns for Create and Edit.
        /// Staff names here are what Index and payroll to-do use for AssignedPayrollStaff.
        /// </summary>
        private void PopulateViewBag()
        {
            ViewBag.TypeOfServiceOptions = TypeOfServiceOptions.All;
            ViewBag.DistributionOptions = PayrollDistributionOptions.All;
            ViewBag.PayrollStaffList = _db.Users
                                              .Where(u => u.Role == "PayrollStaff" && u.IsActive)
                                              .AsEnumerable()
                                              .Select(u => u.FullName)
                                              .ToList();
        }

        /// <summary>
        /// GET /Project. Admin project list. Search matches name, location, or type.
        /// Status filter uses ProjectStatusOptions (On Going, Finished, and others). Add Project opens Create.
        /// </summary>
        /// <returns>The project list view for the chosen status.</returns>
        public async Task<IActionResult> Index(string? search, string? status)
        {
            var filter = ProjectStatusOptions.Normalize(status);
            var query = _db.Projects.AsNoTracking().WithStatus(filter);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(p =>
                    (p.ProjectName != null && p.ProjectName.Contains(s)) ||
                    (p.Location != null && p.Location.Contains(s)) ||
                    (p.TypeOfService != null && p.TypeOfService.Contains(s)));
            }

            ViewBag.Search = search;
            ViewBag.Status = filter;
            ViewBag.PageTitle = "Projects";

            return View(await query.OrderBy(p => p.ProjectName).ToListAsync());
        }

        /// <summary>
        /// GET /Project/Details/{id}. View Project page: monthly budgets, assigned employees, and unassigned actives to add.
        /// </summary>
        /// <returns>The details view, or 404 if the project does not exist.</returns>
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
                                     .Where(e => e.ProjectId == null && e.IsActive)
                                     .OrderBy(e => e.FirstName)
                                     .ToListAsync();

            ViewBag.Employees = employees;
            ViewBag.UnassignedEmployees = unassignedEmployees;
            ViewBag.PageTitle = "View Project";
            return View(project);
        }

        /// <summary>
        /// GET /Project/Create. Opens the Add Project form from the list page button.
        /// </summary>
        /// <returns>An empty Project form with staff and type dropdowns.</returns>
        public IActionResult Create()
        {
            ViewBag.PageTitle = "Add Project";
            PopulateViewBag();
            return View(new Project());
        }

        /// <summary>
        /// POST /Project/Create. Save on the Add Project form, including monthly budget rows for the date range.
        /// Duplicate names are rejected. Monthly totals cannot exceed PayrollBudget.
        /// After save, Admin adds a PayrollSchedule from the Home dashboard.
        /// </summary>
        /// <returns>The project list after save, or the form with validation errors.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Project project,
            List<string> MonthYears, List<decimal> MonthAmounts)
        {
            ModelState.Remove("MonthlyBudgets");
            NormalizeProject(project);
            await ValidateProjectAsync(project, MonthYears, MonthAmounts);

            if (!ModelState.IsValid)
            {
                ViewBag.PageTitle = "Add Project";
                ViewBag.MonthYears = MonthYears;
                ViewBag.MonthAmounts = MonthAmounts;
                PopulateViewBag();
                return View(project);
            }

            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            project.ProjectName = ti.ToTitleCase((project.ProjectName ?? string.Empty).Trim().ToLower());
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
            TempData["Success"] = "Project added successfully.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// GET /Project/Edit/{id}. Opens the Edit Project form with monthly budgets and currently assigned employees.
        /// </summary>
        /// <returns>The filled form, or 404 if the project does not exist.</returns>
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

        /// <summary>
        /// POST /Project/Edit. Save on the Edit Project form.
        /// Monthly budget rows are replaced from the posted MonthYears / MonthAmounts lists.
        /// Changing AssignedPayrollStaff is what puts this project on a staff member's to-do list.
        /// </summary>
        /// <returns>The project list after save, or the form with validation errors.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Project project,
            List<string> MonthYears, List<decimal> MonthAmounts)
        {
            ModelState.Remove("MonthlyBudgets");
            NormalizeProject(project);
            await ValidateProjectAsync(project, MonthYears, MonthAmounts, project.ProjectId);

            if (!ModelState.IsValid)
            {
                ViewBag.PageTitle = "Edit Project";
                ViewBag.MonthYears = MonthYears;
                ViewBag.MonthAmounts = MonthAmounts;
                ViewBag.Employees = await _db.Employees
                    .Where(e => e.ProjectId == project.ProjectId)
                    .ToListAsync();
                PopulateViewBag();
                return View(project);
            }

            var existing = await _db.Projects
                                    .Include(p => p.MonthlyBudgets)
                                    .FirstOrDefaultAsync(p => p.ProjectId == project.ProjectId);

            if (existing == null) return NotFound();

            var ti = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            existing.ProjectName = ti.ToTitleCase((project.ProjectName ?? string.Empty).Trim().ToLower());
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
            TempData["Success"] = "Project updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Trim text fields and normalize Status before Create/Edit validation.
        /// Blank optional fields become null so dropdowns can stay empty.
        /// </summary>
        private static void NormalizeProject(Project project)
        {
            project.ProjectName = project.ProjectName?.Trim() ?? string.Empty;
            project.Location = project.Location?.Trim();
            project.TypeOfService = string.IsNullOrWhiteSpace(project.TypeOfService) ? null : project.TypeOfService.Trim();
            project.PayrollDistribution = string.IsNullOrWhiteSpace(project.PayrollDistribution) ? null : project.PayrollDistribution.Trim();
            project.AssignedPayrollStaff = string.IsNullOrWhiteSpace(project.AssignedPayrollStaff) ? null : project.AssignedPayrollStaff.Trim();
            project.Status = ProjectStatusOptions.Normalize(project.Status);
        }

        /// <summary>
        /// Create/Edit rules: unique project name, monthly rows required when dates exist,
        /// amounts cannot be negative, and monthly totals cannot exceed PayrollBudget.
        /// </summary>
        private async Task ValidateProjectAsync(
            Project project,
            List<string> monthYears,
            List<decimal> monthAmounts,
            int excludeId = 0)
        {
            if (!string.IsNullOrWhiteSpace(project.ProjectName))
            {
                var name = project.ProjectName.Trim().ToLower();
                var nameTaken = await _db.Projects.AnyAsync(p =>
                    p.ProjectId != excludeId &&
                    p.ProjectName.ToLower() == name);
                if (nameTaken)
                    ModelState.AddModelError("ProjectName", "A project with this name already exists.");
            }

            if (project.StartingDate.HasValue && project.EstimateEndDate.HasValue)
            {
                if (monthYears == null || monthYears.Count == 0)
                {
                    ModelState.AddModelError("MonthlyBudget",
                        "Select starting and estimate end dates to generate monthly budget rows.");
                }
                else if (monthAmounts != null && monthAmounts.Any(a => a < 0))
                {
                    ModelState.AddModelError("MonthlyBudget", "Monthly budget amounts cannot be negative.");
                }
                else if (project.PayrollBudget.HasValue && monthAmounts != null)
                {
                    var monthlyTotal = monthAmounts.Sum();
                    if (monthlyTotal > project.PayrollBudget.Value)
                    {
                        ModelState.AddModelError("MonthlyBudget",
                            "Monthly budget total cannot exceed the payroll budget.");
                    }
                }
            }
        }

        /// <summary>
        /// POST /Project/Delete/{id}. Row Delete button. Removes the project and its monthly budget rows.
        /// </summary>
        /// <returns>A redirect back to the project list.</returns>
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

        /// <summary>
        /// POST /Project/RemoveEmployee. Unassign button on Edit Project.
        /// Sets Employee.ProjectId to null so the person can be assigned to another project.
        /// </summary>
        /// <returns>A redirect back to Edit for this project.</returns>
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


        /// <summary>
        /// POST /Project/AssignEmployee. Add employee from the Details/Edit picker.
        /// Sets ProjectId and forces IsActive so the person appears on Generate Payroll.
        /// </summary>
        /// <returns>JSON with the assigned employee row for the page to append.</returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignEmployee(int employeeId, int projectId)
        {
            var emp = await _db.Employees.FindAsync(employeeId);
            if (emp == null)
                return Json(new { success = false, message = "Employee not found." });

            emp.ProjectId = projectId;
            emp.IsActive = true;
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

        /// <summary>
        /// POST /Project/DeactivateAndRemove. Deactivate on the project employee list.
        /// Marks the employee Inactive and clears ProjectId in one step.
        /// </summary>
        /// <returns>JSON with the employee id and name so the row can be removed from the table.</returns>
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
