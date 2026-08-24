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

        /// <summary>
        /// Receives the import service and app settings so this API can save files and check X-Api-Key.
        /// </summary>
        public AttendanceApiController(AttendanceImportService imports, IConfiguration config)
        {
            _imports = imports;
            _config = config;
        }

        /// <summary>
        /// GET /api/attendance/health. Scripts call this to verify the API key before uploading a file.
        /// </summary>
        /// <returns>200 with a service name, or 401 if X-Api-Key is missing or wrong.</returns>
        [HttpGet("health")]
        public IActionResult Health()
        {
            if (!HasValidApiKey())
                return Unauthorized(new { success = false, message = "Invalid or missing X-Api-Key." });

            return Ok(new { success = true, service = "attendance-import" });
        }

        /// <summary>
        /// POST /api/attendance/import. n8n or a scanner sends a spreadsheet plus projectId or projectName.
        /// Saves rows through AttendanceImportService and reports how many matched employees.
        /// </summary>
        /// <returns>JSON with import counts, or 400/401 when the key, file, or project is invalid.</returns>
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

            // Callers may identify the project by id or by name; one of the two is required.
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

        /// <summary>
        /// Build the success sentence for Import. Mentions a replace when the same dates were imported before,
        /// and appends unmatched row counts so the caller can fix employee names.
        /// </summary>
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

        /// <summary>
        /// Compare the X-Api-Key header to Attendance:ApiKey in configuration.
        /// A blank config key always fails so the endpoint cannot run unsecured.
        /// </summary>
        /// <returns>True only when the header matches the configured key exactly.</returns>
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
