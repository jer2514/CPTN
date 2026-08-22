using Microsoft.AspNetCore.Http;
using RSDSystem.Helpers;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public class ActivityLogService
    {
        private readonly PayrollDbContext _db;
        private readonly IHttpContextAccessor _http;

        public ActivityLogService(PayrollDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        public Task LogAsync(
            string activity,
            string module,
            string description,
            int? projectId = null,
            int? relatedId = null,
            CancellationToken cancellationToken = default)
        {
            var http = _http.HttpContext;
            int? userId = null;
            if (int.TryParse(http?.Session.GetString("UserId"), out var id))
                userId = id;

            return LogAsync(
                userId,
                http?.Session.GetString("FullName"),
                http?.Session.GetString("Role"),
                activity,
                module,
                description,
                projectId,
                relatedId,
                cancellationToken);
        }

        public async Task LogAsync(
            int? userId,
            string? userName,
            string? role,
            string activity,
            string module,
            string description,
            int? projectId = null,
            int? relatedId = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _db.ActivityLogs.Add(new ActivityLog
                {
                    UserId = userId,
                    UserName = Clip(userName, 150) ?? "System",
                    Role = Clip(role, 30) ?? "System",
                    Activity = Clip(activity, 60) ?? "",
                    Module = Clip(module, 40) ?? "",
                    Description = Clip(description, 500) ?? "",
                    ProjectId = projectId,
                    RelatedId = relatedId,
                    CreatedAt = PhilippinesTime.Now
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Activity log error: " + ex.Message);
            }
        }

        private static string? Clip(string? value, int max)
        {
            var text = (value ?? "").Trim();
            if (text.Length == 0)
                return null;
            return text.Length <= max ? text : text[..max];
        }
    }
}
