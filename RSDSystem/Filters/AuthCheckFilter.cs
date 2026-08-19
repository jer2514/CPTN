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
                if (WantsJson(context))
                {
                    context.Result = new JsonResult(new
                    {
                        success = false,
                        message = "Your session expired. Refresh the page and sign in again."
                    })
                    { StatusCode = 401 };
                    return;
                }

                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // Keep PayrollStaff out of Admin-only areas
            var adminOnly = new[] { "Home", "UserManagement", "Employee", "Project", "Report" };
            if (role == "PayrollStaff" && controllerName != null && adminOnly.Contains(controllerName))
            {
                context.Result = new RedirectToActionResult("Index", "PayrollStaff", null);
            }
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        private static bool WantsJson(ActionExecutingContext context)
        {
            var request = context.HttpContext.Request;
            var accept = request.Headers["Accept"].ToString();
            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return true;

            var controller = context.RouteData.Values["controller"]?.ToString();
            var action = context.RouteData.Values["action"]?.ToString();
            return string.Equals(controller, "Attendance", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(action, "Preview", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "ImportFile", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "UpdateRecord", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "RequestCorrection", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "GetRecords", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "GetPeriods", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "GetSummary", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(action, "DeletePeriod", StringComparison.OrdinalIgnoreCase))
                || (string.Equals(controller, "PayrollStaff", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(action, "GetAttendanceTotals", StringComparison.OrdinalIgnoreCase))
                || (string.Equals(controller, "Payroll", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(action, "GetPrediction", StringComparison.OrdinalIgnoreCase))
                || (string.Equals(controller, "PayrollPredictionApi", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(action, "Predict", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "Health", StringComparison.OrdinalIgnoreCase)))
                || (string.Equals(controller, "Notification", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(action, "Recent", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "UnreadCount", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "MarkRead", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "MarkAllRead", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "GetCorrection", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "ApproveCorrection", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "ReturnCorrection", StringComparison.OrdinalIgnoreCase)))
                || (string.Equals(controller, "Report", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(action, "Periods", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "Generate", StringComparison.OrdinalIgnoreCase)));
        }
    }
}