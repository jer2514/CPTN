using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace RSDSystem.Filters
{
    /// <summary>
    /// Global gate for every MVC action (registered in Program.cs).
    ///
    /// Order of checks:
    /// 1. Account and AttendanceApi are public (login page + machine import API).
    /// 2. Everyone else needs Session UserId + Role or they are sent to Login
    ///    (JSON callers get HTTP 401 instead of a redirect).
    /// 3. PayrollStaff cannot open Admin controllers (Home, Users, Employees, Projects, Reports).
    ///    They are bounced to /PayrollStaff.
    ///
    /// Admin screens that PayrollStaff CAN open (Payroll, Attendance, Notification)
    /// still do a second Role check inside the controller when needed.
    /// </summary>
    public class AuthCheckFilter : IActionFilter
    {
        // Controllers reachable without being logged in
        private static readonly string[] PublicControllers = { "Account", "AttendanceApi" };

        /// <summary>
        /// Runs before every MVC action. Input is the current request (controller name + session UserId/Role).
        /// Output is either continue, HTTP 401 JSON, redirect to Login, or bounce PayrollStaff off Admin-only controllers.
        /// </summary>
        public void OnActionExecuting(ActionExecutingContext context)
        {
            var controllerName = context.RouteData.Values["controller"]?.ToString();

            // Step 1: login page and biometric AttendanceApi stay public (no session required).
            if (controllerName != null && PublicControllers.Contains(controllerName))
                return;

            var userId = context.HttpContext.Session.GetString("UserId");
            var role = context.HttpContext.Session.GetString("Role");

            // Step 2: everyone else needs a logged-in session; JSON callers get 401 instead of an HTML redirect.
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role))
            {
                // Fetch/XHR endpoints must not redirect to the HTML login page.
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

            // Step 3: Keep PayrollStaff out of Admin-only areas (Home, Users, Employees, Projects, Reports).
            var adminOnly = new[] { "Home", "UserManagement", "Employee", "Project", "Report" };
            if (role == "PayrollStaff" && controllerName != null && adminOnly.Contains(controllerName))
            {
                context.Result = new RedirectToActionResult("Index", "PayrollStaff", null);
            }
        }

        /// <summary>
        /// Required by <see cref="IActionFilter"/>; this app does not post-process actions here.
        /// </summary>
        public void OnActionExecuted(ActionExecutedContext context) { }

        /// <summary>
        /// True when the request should receive JSON (Accept header or known AJAX actions for attendance import,
        /// payroll prediction, notifications, and reports). Used so an expired session returns 401 instead of the login HTML page.
        /// </summary>
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
                        || string.Equals(action, "ReturnCorrection", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "GetTask", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "ApproveTask", StringComparison.OrdinalIgnoreCase)))
                || (string.Equals(controller, "Report", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(action, "Periods", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "Generate", StringComparison.OrdinalIgnoreCase)));
        }
    }
}