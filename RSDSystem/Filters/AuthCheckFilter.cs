using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RSDSystem.Filters
{
    public class AuthCheckFilter : IActionFilter
    {
        // Controllers reachable without being logged in
        private static readonly string[] PublicControllers = { "Account", "AttendanceApi" };

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var controllerName = context.RouteData.Values["controller"]?.ToString();

            if (controllerName != null && PublicControllers.Contains(controllerName))
                return;

            var userId = context.HttpContext.Session.GetString("UserId");
            var role = context.HttpContext.Session.GetString("Role");

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role))
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // Keep PayrollStaff out of Admin-only areas
            var adminOnly = new[] { "Home", "UserManagement", "Employee", "Project" };
            if (role == "PayrollStaff" && controllerName != null && adminOnly.Contains(controllerName))
            {
                context.Result = new RedirectToActionResult("Index", "PayrollStaff", null);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context) { }
    }
}