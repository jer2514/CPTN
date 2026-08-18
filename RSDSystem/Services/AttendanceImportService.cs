using Microsoft.EntityFrameworkCore;
using RSDSystem.Helpers;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public class AttendanceImportService
    {
        private readonly PayrollDbContext _db;

        public AttendanceImportService(PayrollDbContext db)
        {
            _db = db;
        }

        public async Task<AttendancePreviewResult> PreviewAsync(
            int? projectId,
            string? projectName,
            Stream file,
            string fileName,
            string? assignedStaff,
            CancellationToken cancellationToken = default)
        {
            var project = await ResolveProjectAsync(projectId, projectName, assignedStaff, cancellationToken);
            if (project == null)
            {
                return new AttendancePreviewResult
                {
                    Error = "Project not found. Type a project name and click Load first."
                };
            }

            var employees = await LoadMatchPoolAsync(project.ProjectId, cancellationToken);
            var parsed = AttendanceFileParser.Parse(file, fileName);
            if (parsed.Error != null)
            {
                return new AttendancePreviewResult { Error = parsed.Error, Project = project };
            }

            await ApplyAdminTaskWindowAsync(project.ProjectId, parsed, cancellationToken);
            var rows = MapRows(parsed, employees);
            return new AttendancePreviewResult
            {
                Project = project,
                FileName = Path.GetFileName(fileName),
                Format = parsed.Format,
                PeriodStart = parsed.PeriodStart,
                PeriodEnd = parsed.PeriodEnd,
                Rows = rows,
                MatchedCount = rows.Count(r => r.Matched),
                UnmatchedCount = rows.Count(r => !r.Matched)
            };
        }

        public async Task<AttendanceImportResult> ImportAsync(
            int? projectId,
            string? projectName,
            Stream file,
            string fileName,
            string importedBy,
            string source,
            string? assignedStaff,
            string? overridesJson = null,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            var preview = await PreviewAsync(projectId, projectName, buffer, fileName, assignedStaff, cancellationToken);
            ApplyOverrides(preview.Rows, overridesJson);
            if (preview.Error != null || preview.Project == null)
            {
                return new AttendanceImportResult { Error = preview.Error ?? "Import failed." };
            }

            if (preview.Rows.Count == 0)
            {
                return new AttendanceImportResult { Error = "The file did not contain any attendance rows." };
            }

            try
            {
                await AttendanceSchema.EnsureAsync(_db, cancellationToken);
            }
            catch (Exception ex)
            {
                return new AttendanceImportResult
                {
                    Error = "Could not prepare the attendance tables. " + DescribeSaveError(ex)
                };
            }

            var batch = new AttendanceImport
            {
                ProjectId = preview.Project.ProjectId,
                FileName = Clip(Path.GetFileName(fileName), 260) ?? "attendance.xls",
                Source = Clip(source, 20) ?? AttendanceImportSources.Manual,
                Format = Clip(preview.Format, 30) ?? AttendanceFormats.Daily,
                PeriodStart = preview.PeriodStart,
                PeriodEnd = preview.PeriodEnd,
                ImportedBy = Clip(importedBy, 150),
                ImportedAt = DateTime.Now,
                RowCount = preview.Rows.Count
            };

            var replaced = await ReplaceOverlappingImportsAsync(
                preview.Project.ProjectId,
                preview.PeriodStart,
                preview.PeriodEnd,
                cancellationToken);

            foreach (var row in preview.Rows)
            {
                batch.Records.Add(new AttendanceRecord
                {
                    EmployeeId = row.EmployeeId,
                    ExternalUserId = Clip(row.ExternalUserId, 40) ?? "",
                    EmployeeName = Clip(row.EmployeeName, 150) ?? "",
                    WorkDate = row.WorkDate,
                    PeriodStart = preview.PeriodStart,
                    PeriodEnd = preview.PeriodEnd,
                    TimeIn1 = ClipTime(row.TimeIn1),
                    TimeOut1 = ClipTime(row.TimeOut1),
                    TimeIn2 = ClipTime(row.TimeIn2),
                    TimeOut2 = ClipTime(row.TimeOut2),
                    OvertimeIn = ClipTime(row.OvertimeIn),
                    OvertimeOut = ClipTime(row.OvertimeOut),
                    WorkHoursNormal = row.WorkHoursNormal,
                    WorkHoursActual = row.WorkHoursActual,
                    LateMinutes = row.LateMinutes,
                    EarlyMinutes = row.EarlyMinutes,
                    OvertimeHours = row.OvertimeHours,
                    AbsenceDays = row.AbsenceDays,
                    Status = Clip(row.Status, 20) ?? AttendanceStatuses.Incomplete,
                    Matched = row.Matched
                });
            }

            _db.AttendanceImports.Add(batch);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                return new AttendanceImportResult { Error = DescribeSaveError(ex) };
            }

            return new AttendanceImportResult
            {
                ImportId = batch.AttendanceImportId,
                ProjectId = preview.Project.ProjectId,
                ProjectName = preview.Project.ProjectName ?? "",
                FileName = batch.FileName,
                Format = preview.Format,
                PeriodStart = preview.PeriodStart,
                PeriodEnd = preview.PeriodEnd,
                RowCount = preview.Rows.Count,
                MatchedCount = preview.MatchedCount,
                UnmatchedCount = preview.UnmatchedCount,
                ReplacedPrevious = replaced
            };
        }

        public async Task<(List<AttendanceRecord> Rows, int Total)> QueryRecordsAsync(
            int projectId,
            string? search,
            string? status,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var query = _db.AttendanceRecords
                .AsNoTracking()
                .Include(r => r.Import)
                .Include(r => r.Employee)
                .Where(r => r.Import != null && r.Import.ProjectId == projectId);

            if (!string.IsNullOrWhiteSpace(status) &&
                !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => r.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(r =>
                    r.EmployeeName.Contains(term) ||
                    r.ExternalUserId.Contains(term) ||
                    (r.Employee != null && r.Employee.EmployeeCode.Contains(term)));
            }

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 100);
            var all = await query
                .OrderByDescending(r => r.Import!.ImportedAt)
                .ThenByDescending(r => r.AttendanceRecordId)
                .ThenBy(r => r.EmployeeName)
                .ThenBy(r => r.WorkDate)
                .ToListAsync(cancellationToken);

            var rows = all
                .GroupBy(r => (
                    r.EmployeeId,
                    User: (r.ExternalUserId ?? "").Trim().ToLowerInvariant(),
                    Date: r.WorkDate?.Date
                ))
                .Select(g => g.First())
                .OrderByDescending(r => r.Import!.ImportedAt)
                .ThenBy(r => r.EmployeeName)
                .ThenBy(r => r.WorkDate)
                .ToList();

            var total = rows.Count;
            return (rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), total);
        }

        private async Task<bool> ReplaceOverlappingImportsAsync(
            int projectId,
            DateTime? periodStart,
            DateTime? periodEnd,
            CancellationToken cancellationToken)
        {
            var start = (periodStart ?? periodEnd)?.Date;
            var end = (periodEnd ?? periodStart)?.Date;

            var existing = await _db.AttendanceImports
                .Include(i => i.Records)
                .Where(i => i.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            List<AttendanceImport> toRemove;
            if (!start.HasValue || !end.HasValue)
            {
                toRemove = existing;
            }
            else
            {
                toRemove = existing.Where(i => PeriodsOverlap(i, start.Value, end.Value)).ToList();
            }

            if (toRemove.Count == 0)
                return false;

            _db.AttendanceImports.RemoveRange(toRemove);
            return true;
        }

        private static bool PeriodsOverlap(AttendanceImport import, DateTime start, DateTime end)
        {
            var importStart = (import.PeriodStart ?? import.PeriodEnd)?.Date
                ?? import.Records.Where(r => r.WorkDate.HasValue).Select(r => r.WorkDate!.Value.Date).DefaultIfEmpty(start).Min();
            var importEnd = (import.PeriodEnd ?? import.PeriodStart)?.Date
                ?? import.Records.Where(r => r.WorkDate.HasValue).Select(r => r.WorkDate!.Value.Date).DefaultIfEmpty(end).Max();
            return importStart <= end && importEnd >= start;
        }

        public async Task<string?> UpdateRecordAsync(
            int recordId,
            string? timeIn1,
            string? timeOut1,
            string? timeIn2,
            string? timeOut2,
            string? overtimeIn,
            string? overtimeOut,
            string? status,
            CancellationToken cancellationToken = default)
        {
            var record = await _db.AttendanceRecords.FirstOrDefaultAsync(
                r => r.AttendanceRecordId == recordId, cancellationToken);
            if (record == null)
            {
                return "Attendance row not found.";
            }

            record.TimeIn1 = EmptyToNull(timeIn1);
            record.TimeOut1 = EmptyToNull(timeOut1);
            record.TimeIn2 = EmptyToNull(timeIn2);
            record.TimeOut2 = EmptyToNull(timeOut2);
            record.OvertimeIn = EmptyToNull(overtimeIn);
            record.OvertimeOut = EmptyToNull(overtimeOut);
            record.WorkHoursActual = AttendanceDisplay.RegularHours(
                record.TimeIn1, record.TimeOut1, record.TimeIn2, record.TimeOut2);
            record.OvertimeHours = AttendanceDisplay.OvertimeHours(record.OvertimeIn, record.OvertimeOut);
            record.LateMinutes = LateMinutesFrom(record.TimeIn1);

            if (!string.IsNullOrWhiteSpace(status) &&
                AttendanceStatuses.All.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                record.Status = AttendanceStatuses.All.First(s =>
                    s.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                record.Status = AttendanceFileParser.DeriveStatus(record);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        public async Task<AttendanceMonthEdit?> GetMonthEditAsync(int recordId, CancellationToken cancellationToken = default)
        {
            var focus = await _db.AttendanceRecords
                .Include(r => r.Import)
                .Include(r => r.Employee)
                .FirstOrDefaultAsync(r => r.AttendanceRecordId == recordId, cancellationToken);
            if (focus?.Import == null || !focus.WorkDate.HasValue)
                return null;

            var periodStart = focus.Import.PeriodStart?.Date ?? focus.WorkDate.Value.Date;
            var periodEnd = focus.Import.PeriodEnd?.Date ?? focus.WorkDate.Value.Date;
            if (periodEnd < periodStart)
                periodEnd = periodStart;

            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.ProjectId == focus.Import.ProjectId, cancellationToken);

            var existing = await _db.AttendanceRecords
                .Where(r => r.AttendanceImportId == focus.AttendanceImportId
                    && r.WorkDate >= periodStart
                    && r.WorkDate <= periodEnd
                    && ((focus.EmployeeId.HasValue && r.EmployeeId == focus.EmployeeId)
                        || r.ExternalUserId == focus.ExternalUserId))
                .ToListAsync(cancellationToken);

            var byDate = existing
                .Where(r => r.WorkDate.HasValue)
                .GroupBy(r => r.WorkDate!.Value.Date)
                .ToDictionary(g => g.Key, g => g.First());

            var edit = new AttendanceMonthEdit
            {
                FocusRecordId = focus.AttendanceRecordId,
                AttendanceImportId = focus.AttendanceImportId,
                ProjectId = focus.Import.ProjectId,
                ProjectName = project?.ProjectName ?? "",
                EmployeeId = focus.EmployeeId,
                ExternalUserId = focus.ExternalUserId,
                DisplayId = AttendanceDisplay.EmployeeId(focus.Employee?.EmployeeCode ?? focus.ExternalUserId),
                EmployeeName = focus.Employee?.FullName ?? focus.EmployeeName,
                MonthLabel = AttendanceDisplay.LongDate(periodStart) + " – " + AttendanceDisplay.LongDate(periodEnd),
                MonthStart = periodStart,
                Matched = focus.Matched
            };

            for (var day = periodStart; day <= periodEnd; day = day.AddDays(1))
            {
                byDate.TryGetValue(day, out var row);
                edit.Days.Add(new AttendanceDayEdit
                {
                    RecordId = row?.AttendanceRecordId ?? 0,
                    WorkDate = day,
                    TimeIn1 = AttendanceDisplay.HtmlTime(row?.TimeIn1),
                    TimeOut1 = AttendanceDisplay.HtmlTime(row?.TimeOut1),
                    TimeIn2 = AttendanceDisplay.HtmlTime(row?.TimeIn2),
                    TimeOut2 = AttendanceDisplay.HtmlTime(row?.TimeOut2),
                    OvertimeIn = AttendanceDisplay.HtmlTime(row?.OvertimeIn),
                    OvertimeOut = AttendanceDisplay.HtmlTime(row?.OvertimeOut),
                    Status = row?.Status ?? AttendanceStatuses.Absent,
                    RegularHours = AttendanceDisplay.RegularHours(row?.TimeIn1, row?.TimeOut1, row?.TimeIn2, row?.TimeOut2),
                    OvertimeHours = AttendanceDisplay.OvertimeHours(row?.OvertimeIn, row?.OvertimeOut)
                });
            }

            edit.DaysWorked = edit.Days.Count(d => d.Status is AttendanceStatuses.Complete or AttendanceStatuses.Late);
            edit.DaysAbsent = edit.Days.Count(d => d.Status == AttendanceStatuses.Absent);
            edit.DaysLate = edit.Days.Count(d => d.Status == AttendanceStatuses.Late);
            edit.DaysIncomplete = edit.Days.Count(d => d.Status == AttendanceStatuses.Incomplete);
            edit.RegularHours = edit.Days.Sum(d => d.RegularHours);
            edit.OvertimeHours = edit.Days.Sum(d => d.OvertimeHours);
            return edit;
        }

        public async Task<string?> SaveMonthEditAsync(AttendanceMonthEdit model, CancellationToken cancellationToken = default)
        {
            var import = await _db.AttendanceImports
                .FirstOrDefaultAsync(i => i.AttendanceImportId == model.AttendanceImportId, cancellationToken);
            if (import == null)
                return "Attendance import not found.";

            var periodStart = model.Days.Count > 0
                ? model.Days.Min(d => d.WorkDate).Date
                : model.MonthStart.Date;
            var periodEnd = model.Days.Count > 0
                ? model.Days.Max(d => d.WorkDate).Date
                : periodStart;
            import.PeriodStart = periodStart;
            import.PeriodEnd = periodEnd;

            foreach (var day in model.Days.OrderBy(d => d.WorkDate))
            {
                var workDate = day.WorkDate.Date;
                AttendanceRecord? record = null;
                if (day.RecordId > 0)
                {
                    record = await _db.AttendanceRecords.FirstOrDefaultAsync(
                        r => r.AttendanceRecordId == day.RecordId, cancellationToken);
                }

                if (record == null)
                {
                    record = new AttendanceRecord
                    {
                        AttendanceImportId = model.AttendanceImportId,
                        EmployeeId = model.EmployeeId,
                        ExternalUserId = model.ExternalUserId,
                        EmployeeName = model.EmployeeName,
                        WorkDate = workDate,
                        PeriodStart = periodStart,
                        PeriodEnd = periodEnd,
                        Matched = model.Matched
                    };
                    _db.AttendanceRecords.Add(record);
                }

                record.WorkDate = workDate;
                record.PeriodStart = periodStart;
                record.PeriodEnd = periodEnd;
                record.TimeIn1 = EmptyToNull(day.TimeIn1);
                record.TimeOut1 = EmptyToNull(day.TimeOut1);
                record.TimeIn2 = EmptyToNull(day.TimeIn2);
                record.TimeOut2 = EmptyToNull(day.TimeOut2);
                record.OvertimeIn = EmptyToNull(day.OvertimeIn);
                record.OvertimeOut = EmptyToNull(day.OvertimeOut);
                record.WorkHoursActual = AttendanceDisplay.RegularHours(
                    record.TimeIn1, record.TimeOut1, record.TimeIn2, record.TimeOut2);
                record.OvertimeHours = AttendanceDisplay.OvertimeHours(record.OvertimeIn, record.OvertimeOut);
                record.LateMinutes = LateMinutesFrom(record.TimeIn1);
                record.Status = AttendanceFileParser.DeriveStatus(record);
            }

            await _db.SaveChangesAsync(cancellationToken);
            import.RowCount = await _db.AttendanceRecords.CountAsync(
                r => r.AttendanceImportId == model.AttendanceImportId, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return null;
        }

        private async Task ApplyAdminTaskWindowAsync(
            int projectId,
            AttendanceParseResult parsed,
            CancellationToken cancellationToken)
        {
            if (parsed.Rows.Count == 0)
                return;

            var window = await ResolveTaskWindowAsync(projectId, parsed.PeriodStart, parsed.PeriodEnd, cancellationToken);
            if (window == null)
                return;

            AttendanceFileParser.ExpandToDateRange(parsed, window.Value.Start, window.Value.End);
        }

        private async Task<(DateTime Start, DateTime End)?> ResolveTaskWindowAsync(
            int projectId,
            DateTime? fileStart,
            DateTime? fileEnd,
            CancellationToken cancellationToken)
        {
            var schedules = await _db.PayrollSchedules
                .AsNoTracking()
                .Where(s => s.ProjectId == projectId)
                .OrderBy(s => s.StartingDate)
                .ThenBy(s => s.EndDate)
                .ToListAsync(cancellationToken);

            if (schedules.Count == 0)
                return null;

            var open = schedules.Where(s => !s.TaskCompleted).ToList();
            var pool = open.Count > 0 ? open : schedules;

            if (fileStart.HasValue)
            {
                var end = (fileEnd ?? fileStart).Value.Date;
                var start = fileStart.Value.Date;
                var overlap = pool
                    .Where(s => s.StartingDate.Date <= end && s.EndDate.Date >= start)
                    .ToList();
                if (overlap.Count > 0)
                    pool = overlap;
            }

            return (pool.Min(s => s.StartingDate.Date), pool.Max(s => s.EndDate.Date));
        }

        private async Task<Project?> ResolveProjectAsync(
            int? projectId,
            string? projectName,
            string? assignedStaff,
            CancellationToken cancellationToken)
        {
            IQueryable<Project> projects = _db.Projects.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(assignedStaff))
            {
                var staff = assignedStaff.Trim();
                var scoped = projects.Where(p =>
                    p.AssignedPayrollStaff != null &&
                    p.AssignedPayrollStaff.Trim() == staff);
                if (await scoped.AnyAsync(cancellationToken))
                {
                    projects = scoped;
                }
            }

            if (projectId.HasValue && projectId.Value > 0)
            {
                var byId = await projects.FirstOrDefaultAsync(p => p.ProjectId == projectId.Value, cancellationToken);
                if (byId != null)
                {
                    return byId;
                }
            }

            var name = (projectName ?? "").Trim();
            if (name.Length == 0)
            {
                return null;
            }

            return await projects.FirstOrDefaultAsync(
                p => p.ProjectName != null && p.ProjectName.ToLower() == name.ToLower(),
                cancellationToken);
        }

        private async Task<List<Employee>> LoadMatchPoolAsync(int projectId, CancellationToken cancellationToken)
        {
            var projectEmployees = await _db.Employees
                .AsNoTracking()
                .Where(e => e.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            return projectEmployees.Count > 0
                ? projectEmployees
                : await _db.Employees.AsNoTracking().ToListAsync(cancellationToken);
        }

        private static List<AttendancePreviewRow> MapRows(AttendanceParseResult parsed, IReadOnlyList<Employee> employees)
        {
            var rows = new List<AttendancePreviewRow>();
            foreach (var row in parsed.Rows)
            {
                var employeeId = AttendanceFileParser.MatchEmployeeId(employees, row.ExternalUserId, row.EmployeeName);
                var employee = employeeId.HasValue
                    ? employees.FirstOrDefault(e => e.EmployeeId == employeeId.Value)
                    : null;

                rows.Add(new AttendancePreviewRow
                {
                    EmployeeId = employeeId,
                    DisplayId = AttendanceDisplay.EmployeeId(
                        employee?.EmployeeCode ?? row.ExternalUserId),
                    ExternalUserId = row.ExternalUserId,
                    EmployeeName = employee?.FullName ?? row.EmployeeName,
                    WorkDate = row.WorkDate ?? parsed.PeriodStart,
                    TimeIn1 = row.TimeIn1,
                    TimeOut1 = row.TimeOut1,
                    TimeIn2 = row.TimeIn2,
                    TimeOut2 = row.TimeOut2,
                    OvertimeIn = row.OvertimeIn,
                    OvertimeOut = row.OvertimeOut,
                    WorkHoursNormal = row.WorkHoursNormal,
                    WorkHoursActual = row.WorkHoursActual,
                    LateMinutes = row.LateMinutes,
                    EarlyMinutes = row.EarlyMinutes,
                    OvertimeHours = row.OvertimeHours,
                    AbsenceDays = row.AbsenceDays,
                    Status = string.IsNullOrWhiteSpace(row.Status)
                        ? AttendanceFileParser.DeriveStatus(row)
                        : row.Status,
                    Matched = employeeId.HasValue,
                    Note = employeeId.HasValue ? null : "No matching employee on this project."
                });
            }

            return rows;
        }

        private static void ApplyOverrides(List<AttendancePreviewRow> rows, string? overridesJson)
        {
            if (string.IsNullOrWhiteSpace(overridesJson) || rows.Count == 0)
                return;

            List<AttendanceDayOverride>? edits;
            try
            {
                edits = System.Text.Json.JsonSerializer.Deserialize<List<AttendanceDayOverride>>(
                    overridesJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Text.Json.JsonException)
            {
                return;
            }

            if (edits == null || edits.Count == 0)
                return;

            foreach (var edit in edits)
            {
                var date = edit.WorkDate.Date;
                var row = rows.FirstOrDefault(r =>
                    r.WorkDate?.Date == date &&
                    (string.Equals(r.ExternalUserId, edit.ExternalUserId, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(r.EmployeeName, edit.EmployeeName, StringComparison.OrdinalIgnoreCase)));
                if (row == null)
                    continue;

                row.TimeIn1 = EmptyToNull(edit.TimeIn1);
                row.TimeOut1 = EmptyToNull(edit.TimeOut1);
                row.TimeIn2 = EmptyToNull(edit.TimeIn2);
                row.TimeOut2 = EmptyToNull(edit.TimeOut2);
                row.OvertimeIn = EmptyToNull(edit.OvertimeIn);
                row.OvertimeOut = EmptyToNull(edit.OvertimeOut);
                row.WorkHoursActual = AttendanceDisplay.RegularHours(
                    row.TimeIn1, row.TimeOut1, row.TimeIn2, row.TimeOut2);
                row.OvertimeHours = AttendanceDisplay.OvertimeHours(row.OvertimeIn, row.OvertimeOut);
                row.LateMinutes = LateMinutesFrom(row.TimeIn1);
                row.Status = AttendanceFileParser.DeriveStatus(new AttendanceRecord
                {
                    TimeIn1 = row.TimeIn1,
                    TimeOut1 = row.TimeOut1,
                    TimeIn2 = row.TimeIn2,
                    TimeOut2 = row.TimeOut2,
                    OvertimeIn = row.OvertimeIn,
                    OvertimeOut = row.OvertimeOut,
                    WorkHoursActual = row.WorkHoursActual,
                    LateMinutes = row.LateMinutes
                });
            }
        }

        private static string? EmptyToNull(string? value)
        {
            var text = (value ?? "").Trim();
            if (text.Length == 0 || text == "—" || text == "——")
                return null;
            return text;
        }

        private static int LateMinutesFrom(string? timeIn)
        {
            if (!AttendanceDisplay.TryParseTime(timeIn, out var firstIn) || firstIn <= new TimeSpan(8, 15, 0))
                return 0;

            return Math.Max(0, (int)(firstIn - new TimeSpan(8, 0, 0)).TotalMinutes);
        }

        private static string DescribeSaveError(Exception ex)
        {
            var sql = ex.GetBaseException().Message;
            if (sql.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
            {
                return "The attendance tables are missing. Restart the app and import again.";
            }

            if (sql.Contains("truncated", StringComparison.OrdinalIgnoreCase))
            {
                return "A time or name was too long for the database. Edit the row and try again.";
            }

            if (sql.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("conflicted with the FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
            {
                return "A project or employee reference was invalid. Reload the project and try again.";
            }

            if (sql.Contains("multiple cascade paths", StringComparison.OrdinalIgnoreCase))
            {
                return "The attendance tables could not be created because of a database constraint. Restart the app and try again.";
            }

            return sql;
        }

        private static string? Clip(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var text = value.Trim();
            return text.Length <= max ? text : text[..max];
        }

        private static string? ClipTime(string? value)
        {
            var clock = AttendanceDisplay.Clock(value);
            if (string.IsNullOrWhiteSpace(clock))
                return null;

            return Clip(clock, 40);
        }
    }

    public class AttendancePreviewResult
    {
        public string? Error { get; set; }
        public Project? Project { get; set; }
        public string FileName { get; set; } = "";
        public string Format { get; set; } = AttendanceFormats.Daily;
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public List<AttendancePreviewRow> Rows { get; set; } = new();
        public int MatchedCount { get; set; }
        public int UnmatchedCount { get; set; }
    }

    public class AttendancePreviewRow
    {
        public int? EmployeeId { get; set; }
        public string DisplayId { get; set; } = "";
        public string ExternalUserId { get; set; } = "";
        public string EmployeeName { get; set; } = "";
        public DateTime? WorkDate { get; set; }
        public string? TimeIn1 { get; set; }
        public string? TimeOut1 { get; set; }
        public string? TimeIn2 { get; set; }
        public string? TimeOut2 { get; set; }
        public string? OvertimeIn { get; set; }
        public string? OvertimeOut { get; set; }
        public decimal WorkHoursNormal { get; set; }
        public decimal WorkHoursActual { get; set; }
        public int LateMinutes { get; set; }
        public int EarlyMinutes { get; set; }
        public decimal OvertimeHours { get; set; }
        public decimal AbsenceDays { get; set; }
        public string Status { get; set; } = AttendanceStatuses.Incomplete;
        public bool Matched { get; set; }
        public string? Note { get; set; }
    }

    public class AttendanceDayOverride
    {
        public string? ExternalUserId { get; set; }
        public string? EmployeeName { get; set; }
        public DateTime WorkDate { get; set; }
        public string? TimeIn1 { get; set; }
        public string? TimeOut1 { get; set; }
        public string? TimeIn2 { get; set; }
        public string? TimeOut2 { get; set; }
        public string? OvertimeIn { get; set; }
        public string? OvertimeOut { get; set; }
    }

    public class AttendanceImportResult
    {
        public string? Error { get; set; }
        public int ImportId { get; set; }
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Format { get; set; } = "";
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public int RowCount { get; set; }
        public int MatchedCount { get; set; }
        public int UnmatchedCount { get; set; }
        public bool ReplacedPrevious { get; set; }
    }
}
