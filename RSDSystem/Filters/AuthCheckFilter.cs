using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using RSDSystem.Models;

namespace RSDSystem.Filters
{
    public class AuthCheckFilter : IActionFilter
    {
        private readonly PayrollDbContext _db;

        public AuthCheckFilter(PayrollDbContext db)
        {
            _db = db;
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var controllerName = context.RouteData.Values["controller"]?.ToString();
            var actionName = context.RouteData.Values["action"]?.ToString();

            if (IsPublic(controllerName, actionName))
                return;

            var userId = context.HttpContext.Session.GetString("UserId");
            var role = context.HttpContext.Session.GetString("Role");

            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(role))
            {
                Reject(context, "Your session expired. Refresh the page and sign in again.");
                return;
            }

            if (int.TryParse(userId, out var id))
            {
                var account = _db.Users.AsNoTracking()
                    .Where(u => u.UserId == id)
                    .Select(u => new { u.IsActive, u.MustChangePassword, u.Role })
                    .FirstOrDefault();

                if (account == null || !account.IsActive)
                {
                    context.HttpContext.Session.Clear();
                    Reject(context, "This account is inactive and cannot log in.", inactive: true);
                    return;
                }

                var mustChange = string.Equals(account.Role, "PayrollStaff", StringComparison.OrdinalIgnoreCase)
                    && account.MustChangePassword;
                var onChangePassword = string.Equals(controllerName, "Account", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(actionName, "ChangePassword", StringComparison.OrdinalIgnoreCase);
                var onLogout = string.Equals(controllerName, "Account", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(actionName, "Logout", StringComparison.OrdinalIgnoreCase);

                if (mustChange && !onChangePassword && !onLogout)
                {
                    if (WantsJson(context))
                    {
                        context.Result = new JsonResult(new
                        {
                            success = false,
                            message = "Change your password before continuing."
                        })
                        {
                            StatusCode = 401
                        };
                        return;
                    }

                    context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
                    return;
                }
            }

            var adminOnly = new[] { "Home", "UserManagement", "Employee", "Project", "Report", "Payroll", "CashAdvance", "ActivityLog" };
            if (role == "PayrollStaff" && controllerName != null && adminOnly.Contains(controllerName))
            {
                context.Result = new RedirectToActionResult("Index", "PayrollStaff", null);
            }
        }

        private static bool IsPublic(string? controllerName, string? actionName)
        {
            if (string.Equals(controllerName, "AttendanceApi", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.Equals(controllerName, "Account", StringComparison.OrdinalIgnoreCase))
                return false;

            return string.Equals(actionName, "Login", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionName, "Logout", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionName, "ForgotPassword", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionName, "CheckEmail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionName, "SetPassword", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionName, "VerifyResetCode", StringComparison.OrdinalIgnoreCase)
                || string.Equals(actionName, "ResetPassword", StringComparison.OrdinalIgnoreCase);
        }

        public void OnActionExecuted(ActionExecutedContext context) { }

        private static void Reject(ActionExecutingContext context, string message, bool inactive = false)
        {
            if (WantsJson(context))
            {
                context.Result = new JsonResult(new { success = false, message })
                {
                    StatusCode = 401
                };
                return;
            }

            context.Result = inactive
                ? new RedirectToActionResult("Login", "Account", new { inactive = 1 })
                : new RedirectToActionResult("Login", "Account", null);
        }

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
                        || string.Equals(action, "ApproveTask", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "ReturnTask", StringComparison.OrdinalIgnoreCase)))
                || (string.Equals(controller, "Report", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(action, "Periods", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "Generate", StringComparison.OrdinalIgnoreCase)))
                || (string.Equals(controller, "CashAdvance", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(action, "Add", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "Deduct", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(action, "DeductTotal", StringComparison.OrdinalIgnoreCase)));
        }
    }
}