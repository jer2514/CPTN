using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Controllers
{
    public class ActivityLogController : Controller
    {
        private const int PageSize = 10;
        private readonly PayrollDbContext _db;

        public ActivityLogController(PayrollDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index(string? search, string? sortBy, int page = 1)
        {
            if (HttpContext.Session.GetString("Role") != "Admin")
                return RedirectToAction("Index", "PayrollStaff");

            var query = _db.ActivityLogs.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(l =>
                    l.UserName.Contains(term)
                    || l.Role.Contains(term)
                    || l.Activity.Contains(term)
                    || l.Module.Contains(term)
                    || l.Description.Contains(term));
            }

            query = sortBy switch
            {
                "user" => query.OrderBy(l => l.UserName).ThenByDescending(l => l.CreatedAt),
                "role" => query.OrderBy(l => l.Role).ThenByDescending(l => l.CreatedAt),
                "activity" => query.OrderBy(l => l.Activity).ThenByDescending(l => l.CreatedAt),
                "module" => query.OrderBy(l => l.Module).ThenByDescending(l => l.CreatedAt),
                _ => query.OrderByDescending(l => l.CreatedAt)
            };

            var total = await query.CountAsync();
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
            page = Math.Clamp(page, 1, totalPages);
            var rows = await query.Skip((page - 1) * PageSize).Take(PageSize).ToListAsync();

            ViewBag.PageTitle = "Activity Logs";
            ViewBag.Search = search ?? "";
            ViewBag.SortBy = sortBy ?? "";
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            return View(rows);
        }
    }
}
