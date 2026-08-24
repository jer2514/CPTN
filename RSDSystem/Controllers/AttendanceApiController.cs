using Microsoft.AspNetCore.Mvc;
using RSDSystem.Models;
using RSDSystem.Services;

namespace RSDSystem.Controllers
{
    /// <summary>
    /// Machine/API import: POST /api/attendance/import with an API key (not the website login).
    /// AuthCheckFilter allows this controller without a session. Used by scanners/scripts.
    /// Staff still use AttendanceController for the browser Import screen.
    /// </summary>
    [Route("api/attendance")]
    [IgnoreAntiforgeryToken]
    [ApiController]
    public class AttendanceApiController : ControllerBase
    {
        private static readonly string[] AllowedExtensions = { ".xls", ".xlsx", ".csv", ".txt" };

        private readonly AttendanceImportService _imports;
        private readonly IConfiguration _config;

        public AttendanceApiController(AttendanceImportService imports, IConfiguration config)
        {
            _imports = imports;
            _config = config;
        }

        [HttpGet("health")]
        public IActionResult Health()
        {
            if (!HasValidApiKey())
                return Unauthorized(new { success = false, message = "Invalid or missing X-Api-Key." });

            return Ok(new { success = true, service = "attendance-import" });
        }

        [HttpPost("import")]
        [RequestSizeLimit(20_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 20_000_000)]
        public async Task<IActionResult> Import(
            [FromForm] int? projectId,
            [FromForm] string? projectName,
            [FromForm] IFormFile? file)
        {
            if (!HasValidApiKey())
                return Unauthorized(new { success = false, message = "Invalid or missing X-Api-Key." });

            if (file == null || file.Length == 0)
                return BadRequest(new { success = false, message = "Attach the attendance file as form field 'file'." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
                return BadRequest(new { success = false, message = "Use an .xls, .xlsx, or .csv attendance file." });

            if (!projectId.HasValue && string.IsNullOrWhiteSpace(projectName))
                return BadRequest(new { success = false, message = "Provide projectId or projectName." });

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await _imports.ImportAsync(
                    projectId,
                    projectName,
                    stream,
                    file.FileName,
                    "n8n",
                    AttendanceImportSources.N8n,
                    assignedStaff: null,
                    overridesJson: null,
                    cancellationToken: HttpContext.RequestAborted);

                if (result.Error != null)
                    return BadRequest(new { success = false, message = result.Error });

                return Ok(new
                {
                    success = true,
                    message = ImportMessage(result),
                    importId = result.ImportId,
                    projectId = result.ProjectId,
                    projectName = result.ProjectName,
                    fileName = result.FileName,
                    format = result.Format,
                    periodStart = result.PeriodStart,
                    periodEnd = result.PeriodEnd,
                    rowCount = result.RowCount,
                    matchedCount = result.MatchedCount,
                    unmatchedCount = result.UnmatchedCount
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Could not import the attendance file. " + ex.GetBaseException().Message
                });
            }
        }

        private static string ImportMessage(AttendanceImportResult result)
        {
            var message = result.ReplacedPrevious
                ? $"Replaced previous attendance for these dates. Imported {result.RowCount} row(s) for {result.ProjectName}."
                : $"Imported {result.RowCount} row(s) for {result.ProjectName}.";

            if (result.UnmatchedCount > 0)
            {
                message += $" {result.UnmatchedCount} row(s) did not match an employee.";
            }

            return message;
        }

        private bool HasValidApiKey()
        {
            var expected = _config["Attendance:ApiKey"];
            if (string.IsNullOrWhiteSpace(expected))
                return false;

            if (!Request.Headers.TryGetValue("X-Api-Key", out var provided))
                return false;

            return string.Equals(provided.ToString(), expected, StringComparison.Ordinal);
        }
    }
}
