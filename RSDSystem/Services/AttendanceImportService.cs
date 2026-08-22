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

            // Extract the raw rows first, unmatched — matching is applied on top of that raw data.
            var rows = MapRows(parsed, employees);
            rows = DeduplicateRows(rows);

            return new AttendancePreviewResult
            {
                Project = project,
                FileName = Path.GetFileName(fileName),
                Format = parsed.Format,
                PeriodStart = parsed.PeriodStart,
                PeriodEnd = parsed.PeriodEnd,
                Rows = rows,
                CandidateEmployees = employees,
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
            string? manualMatchesJson = null,
            CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            var preview = await PreviewAsync(projectId, projectName, buffer, fileName, assignedStaff, cancellationToken);

            // Apply staff-chosen matches for rows the auto-matcher could not resolve,
            // then apply any punch-time edits made in the preview.
            ApplyManualMatches(preview.Rows, manualMatchesJson, preview.CandidateEmployees);
            ApplyOverrides(preview.Rows, overridesJson);
            preview.Rows = DeduplicateRows(preview.Rows);

            if (preview.Error != null || preview.Project == null)
            {
                return new AttendanceImportResult { Error = preview.Error ?? "Import failed." };
            }

            var closedPayrolls = await PayrollAttendanceLock.LoadClosedAsync(
                _db, preview.Project.ProjectId, cancellationToken);
            var skippedLocked = preview.Rows.Count(r =>
                PayrollAttendanceLock.IsLocked(closedPayrolls, r.EmployeeId, r.WorkDate));
            preview.Rows = preview.Rows
                .Where(r => !PayrollAttendanceLock.IsLocked(closedPayrolls, r.EmployeeId, r.WorkDate))
                .ToList();

            if (preview.Rows.Count == 0)
            {
                return new AttendanceImportResult
                {
                    Error = skippedLocked > 0
                        ? "Payroll for these employees is already approved. Attendance cannot be imported again for that period."
                        : "The file did not contain any attendance rows."
                };
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
                PeriodStart = AttendanceDisplay.UsableDate(preview.PeriodStart),
                PeriodEnd = AttendanceDisplay.UsableDate(preview.PeriodEnd),
                ImportedBy = Clip(importedBy, 150),
                ImportedAt = PhilippinesTime.Now,
                RowCount = preview.Rows.Count
            };

            var replaced = await ReplaceOverlappingImportsAsync(
                preview.Project.ProjectId,
                AttendanceDisplay.UsableDate(preview.PeriodStart),
                AttendanceDisplay.UsableDate(preview.PeriodEnd),
                closedPayrolls,
                cancellationToken);

            await RemoveConflictingRecordsAsync(
                preview.Project.ProjectId, preview.Rows, closedPayrolls, cancellationToken);

            foreach (var row in preview.Rows)
            {
                var workDate = AttendanceDisplay.UsableDate(row.WorkDate);
                batch.Records.Add(new AttendanceRecord
                {
                    ProjectId = preview.Project.ProjectId,
                    EmployeeId = row.EmployeeId,
                    ExternalUserId = Clip(row.ExternalUserId, 40) ?? "",
                    // Store the matched system name when we have one; otherwise keep the raw file name
                    // so unmatched rows are still traceable in Attendance Records.
                    EmployeeName = Clip(row.MatchedEmployeeName ?? row.EmployeeName, 150) ?? "",
                    WorkDate = workDate,
                    PeriodStart = AttendanceDisplay.UsableDate(preview.PeriodStart),
                    PeriodEnd = AttendanceDisplay.UsableDate(preview.PeriodEnd),
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
                    Status = Clip(AttendanceStatuses.Display(row.Status), 20) ?? AttendanceStatuses.HalfDay,
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
                MatchedCount = preview.Rows.Count(r => r.Matched),
                UnmatchedCount = preview.Rows.Count(r => !r.Matched),
                ReplacedPrevious = replaced,
                SkippedLockedCount = skippedLocked
            };
        }

        public async Task<(List<AttendanceRecord> Rows, int Total)> QueryRecordsAsync(
            int projectId,
            string? search,
            string? status,
            int page,
            int pageSize,
            DateTime? periodStart = null,
            DateTime? periodEnd = null,
            CancellationToken cancellationToken = default)
        {
            var query = RecordsForProject(projectId);
            query = ApplyRecordFilters(query, search, null, periodStart, periodEnd);

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 100);
            var all = await query
                .OrderByDescending(r => r.Import!.ImportedAt)
                .ThenByDescending(r => r.AttendanceRecordId)
                .ThenBy(r => r.EmployeeName)
                .ThenBy(r => r.WorkDate)
                .ToListAsync(cancellationToken);

            var rows = DeduplicateStoredRows(all);
            foreach (var row in rows)
                AttendanceRules.Apply(row);

            if (!string.IsNullOrWhiteSpace(status) &&
                !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(status, "Unmatched", StringComparison.OrdinalIgnoreCase))
                    rows = rows.Where(r => !r.Matched).ToList();
                else
                    rows = rows.Where(r => AttendanceStatuses.MatchesFilter(r.Status, status)).ToList();
            }

            var total = rows.Count;
            return (rows.Skip((page - 1) * pageSize).Take(pageSize).ToList(), total);
        }

        public async Task<List<AttendancePeriodOption>> ListPeriodsAsync(
            int projectId,
            CancellationToken cancellationToken = default)
        {
            var imports = await _db.AttendanceImports
                .AsNoTracking()
                .Where(i => i.ProjectId == projectId)
                .Select(i => new
                {
                    i.AttendanceImportId,
                    i.PeriodStart,
                    i.PeriodEnd,
                    i.ImportedBy,
                    i.ImportedAt
                })
                .ToListAsync(cancellationToken);

            var bounds = await _db.AttendanceRecords
                .AsNoTracking()
                .Where(r => r.Import != null && r.Import.ProjectId == projectId)
                .GroupBy(r => r.AttendanceImportId)
                .Select(g => new
                {
                    ImportId = g.Key,
                    MinDate = g.Min(r => r.WorkDate),
                    MaxDate = g.Max(r => r.WorkDate)
                })
                .ToListAsync(cancellationToken);

            var boundMap = bounds.ToDictionary(b => b.ImportId);
            var periods = new Dictionary<string, AttendancePeriodOption>();

            foreach (var import in imports.OrderByDescending(i => i.ImportedAt))
            {
                boundMap.TryGetValue(import.AttendanceImportId, out var bound);
                var start = AttendanceDisplay.UsableDate(import.PeriodStart)
                    ?? AttendanceDisplay.UsableDate(bound?.MinDate);
                var end = AttendanceDisplay.UsableDate(import.PeriodEnd)
                    ?? AttendanceDisplay.UsableDate(bound?.MaxDate)
                    ?? start;
                if (!start.HasValue || !end.HasValue)
                    continue;
                if (end.Value < start.Value)
                    end = start;

                var key = PeriodKey(start.Value, end.Value);
                if (periods.ContainsKey(key))
                    continue;

                periods[key] = new AttendancePeriodOption
                {
                    Key = key,
                    Start = start.Value,
                    End = end.Value,
                    Label = PeriodLabel(start.Value, end.Value),
                    ImportedBy = import.ImportedBy
                };
            }

            return periods.Values
                .OrderByDescending(p => p.Start)
                .ThenByDescending(p => p.End)
                .ToList();
        }

        public async Task<AttendanceSummaryResult> QuerySummaryAsync(
            int projectId,
            DateTime? periodStart,
            DateTime? periodEnd,
            string? search,
            string? status,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var query = ApplyRecordFilters(
                RecordsForProject(projectId), search, null, periodStart, periodEnd);
            var all = DeduplicateStoredRows(await query.ToListAsync(cancellationToken));
            foreach (var row in all)
                AttendanceRules.Apply(row);

            var groups = all
                .GroupBy(r => r.EmployeeId.HasValue
                    ? "e:" + r.EmployeeId.Value
                    : "u:" + (r.ExternalUserId ?? "").Trim().ToLowerInvariant()
                        + ":" + (r.EmployeeName ?? "").Trim().ToLowerInvariant())
                .Select(g => ToEmployeeSummary(g.ToList()))
                .OrderBy(r => r.EmployeeName)
                .ToList();

            if (!string.IsNullOrWhiteSpace(status) &&
                !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                groups = status.Trim() switch
                {
                    "Unmatched" => groups.Where(r => !r.Matched).ToList(),
                    "Absent" => groups.Where(r => r.DaysAbsent > 0).ToList(),
                    "Late" => groups.Where(r => r.DaysLate > 0).ToList(),
                    "Incomplete" => groups.Where(r => r.DaysIncomplete > 0).ToList(),
                    "Half-day" => groups.Where(r => r.DaysIncomplete > 0).ToList(),
                    _ => groups
                };
            }

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 100);
            var start = AttendanceDisplay.UsableDate(periodStart);
            var end = AttendanceDisplay.UsableDate(periodEnd);

            return new AttendanceSummaryResult
            {
                Total = groups.Count,
                DaysWorked = groups.Sum(r => r.DaysWorked),
                DaysAbsent = groups.Sum(r => r.DaysAbsent),
                DaysLate = groups.Sum(r => r.DaysLate),
                DaysIncomplete = groups.Sum(r => r.DaysIncomplete),
                RegularHours = groups.Sum(r => r.RegularHours),
                OvertimeHours = groups.Sum(r => r.OvertimeHours),
                UnmatchedCount = groups.Count(r => !r.Matched),
                ImportedBy = all.Select(r => r.Import?.ImportedBy)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
                PeriodStart = start,
                PeriodEnd = end,
                Rows = groups.Skip((page - 1) * pageSize).Take(pageSize).ToList()
            };
        }

        public async Task<bool> HasImportedAttendanceAsync(
            int projectId,
            DateTime? periodStart,
            DateTime? periodEnd,
            CancellationToken cancellationToken = default)
        {
            return await ApplyRecordFilters(
                    RecordsForProject(projectId), null, null, periodStart, periodEnd)
                .AnyAsync(cancellationToken);
        }

        public async Task<AttendanceEmployeeSummary?> GetEmployeePeriodTotalsAsync(
            int projectId,
            int employeeId,
            DateTime? periodStart,
            DateTime? periodEnd,
            CancellationToken cancellationToken = default)
        {
            var query = ApplyRecordFilters(
                RecordsForProject(projectId), null, null, periodStart, periodEnd)
                .Where(r => r.EmployeeId == employeeId);

            var rows = DeduplicateStoredRows(await query.ToListAsync(cancellationToken));
            if (rows.Count == 0)
                return null;

            foreach (var row in rows)
                AttendanceRules.Apply(row);

            return ToEmployeeSummary(rows);
        }

        public async Task<(int Deleted, string? Error)> DeletePeriodAsync(
            int projectId,
            DateTime? periodStart,
            DateTime? periodEnd,
            CancellationToken cancellationToken = default)
        {
            var start = AttendanceDisplay.UsableDate(periodStart);
            var end = AttendanceDisplay.UsableDate(periodEnd);
            if (!start.HasValue || !end.HasValue)
                return (0, "Select a valid period first.");

            var from = start.Value.Date;
            var toExclusive = end.Value.Date.AddDays(1);
            var records = await _db.AttendanceRecords
                .Include(r => r.Import)
                .Where(r => r.Import != null && r.Import.ProjectId == projectId)
                .Where(r => r.WorkDate != null && r.WorkDate >= from && r.WorkDate < toExclusive)
                .ToListAsync(cancellationToken);

            if (records.Count == 0)
                return (0, "No attendance rows in this period.");

            var importIds = records.Select(r => r.AttendanceImportId).Distinct().ToList();
            _db.AttendanceRecords.RemoveRange(records);
            await _db.SaveChangesAsync(cancellationToken);

            var emptyImports = await _db.AttendanceImports
                .Where(i => importIds.Contains(i.AttendanceImportId)
                    && !_db.AttendanceRecords.Any(r => r.AttendanceImportId == i.AttendanceImportId))
                .ToListAsync(cancellationToken);
            if (emptyImports.Count > 0)
            {
                _db.AttendanceImports.RemoveRange(emptyImports);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return (records.Count, null);
        }

        private IQueryable<AttendanceRecord> RecordsForProject(int projectId) =>
            _db.AttendanceRecords
                .AsNoTracking()
                .Include(r => r.Import)
                .Include(r => r.Employee)
                .Where(r => r.Import != null && r.Import.ProjectId == projectId);

        private static IQueryable<AttendanceRecord> ApplyRecordFilters(
            IQueryable<AttendanceRecord> query,
            string? search,
            string? status,
            DateTime? periodStart,
            DateTime? periodEnd)
        {
            var start = AttendanceDisplay.UsableDate(periodStart);
            var end = AttendanceDisplay.UsableDate(periodEnd);
            if (start.HasValue && end.HasValue)
            {
                var from = start.Value.Date;
                var toExclusive = end.Value.Date.AddDays(1);
                query = query.Where(r =>
                    (r.WorkDate != null && r.WorkDate >= from && r.WorkDate < toExclusive)
                    || (r.WorkDate == null && r.Import != null
                        && r.Import.PeriodStart != null && r.Import.PeriodEnd != null
                        && r.Import.PeriodStart < toExclusive
                        && r.Import.PeriodEnd >= from));
            }

            if (!string.IsNullOrWhiteSpace(status) &&
                !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(status, "Unmatched", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(r => !r.Matched);
                else if (AttendanceStatuses.IsHalfDay(status))
                    query = query.Where(r => r.Status == AttendanceStatuses.HalfDay
                        || r.Status == AttendanceStatuses.Incomplete);
                else
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

            return query;
        }

        private static List<AttendanceRecord> DeduplicateStoredRows(List<AttendanceRecord> rows) =>
            rows
                .GroupBy(r => (
                    r.EmployeeId,
                    User: (r.ExternalUserId ?? "").Trim().ToLowerInvariant(),
                    Date: AttendanceDisplay.UsableDate(r.WorkDate)
                ))
                .Select(g => g.First())
                .OrderBy(r => r.EmployeeName)
                .ThenBy(r => r.WorkDate)
                .ToList();

        private static AttendanceEmployeeSummary ToEmployeeSummary(List<AttendanceRecord> rows)
        {
            var first = rows[0];
            return new AttendanceEmployeeSummary
            {
                EmployeeId = first.EmployeeId,
                DisplayId = AttendanceDisplay.EmployeeId(first.Employee?.EmployeeCode ?? first.ExternalUserId),
                EmployeeName = first.Employee?.FullName ?? first.EmployeeName,
                Matched = rows.Any(r => r.Matched),
                DaysWorked = rows.Count(r => AttendanceStatuses.CountsAsFullDay(r.Status)),
                DaysPresent = rows.Count(r => AttendanceStatuses.CountsAsWorked(r.Status)),
                DaysAbsent = rows.Count(r => r.Status == AttendanceStatuses.Absent),
                DaysLate = rows.Count(r => AttendanceStatuses.CountsAsLate(r.Status)),
                DaysIncomplete = rows.Count(r => AttendanceStatuses.IsHalfDay(r.Status)),
                RegularHours = rows.Sum(DayRegularHours),
                OvertimeHours = rows.Sum(DayOvertimeHours)
            };
        }

        private static decimal DayRegularHours(AttendanceRecord row)
        {
            if (row.WorkHoursActual > 0)
                return row.WorkHoursActual;
            return AttendanceRules.RegularHours(row.TimeIn1, row.TimeOut1, row.TimeIn2, row.TimeOut2);
        }

        private static decimal DayOvertimeHours(AttendanceRecord row)
        {
            if (row.OvertimeHours > 0)
                return row.OvertimeHours;
            return AttendanceRules.OvertimeHours(
                row.TimeIn1, row.TimeOut1, row.TimeIn2, row.TimeOut2, row.OvertimeIn, row.OvertimeOut);
        }

        private static string PeriodKey(DateTime start, DateTime end) =>
            start.ToString("yyyy-MM-dd") + "|" + end.ToString("yyyy-MM-dd");

        private static string PeriodLabel(DateTime start, DateTime end) =>
            AttendanceDisplay.LongDate(start) + " - " + AttendanceDisplay.LongDate(end);

        private async Task<bool> ReplaceOverlappingImportsAsync(
            int projectId,
            DateTime? periodStart,
            DateTime? periodEnd,
            List<ClosedPayrollWindow> closedPayrolls,
            CancellationToken cancellationToken)
        {
            var start = AttendanceDisplay.UsableDate(periodStart ?? periodEnd);
            var end = AttendanceDisplay.UsableDate(periodEnd ?? periodStart);

            // A missing period must not wipe every import on the project.
            if (!start.HasValue || !end.HasValue)
                return false;

            var existing = await _db.AttendanceImports
                .Include(i => i.Records)
                .Where(i => i.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            var toRemove = existing.Where(i => PeriodsOverlap(i, start.Value, end.Value)).ToList();

            if (toRemove.Count == 0)
                return false;

            var removedUnlocked = false;
            foreach (var import in toRemove)
            {
                var locked = import.Records
                    .Where(r => PayrollAttendanceLock.IsLocked(closedPayrolls, r.EmployeeId, r.WorkDate))
                    .ToList();
                var unlocked = import.Records
                    .Where(r => !PayrollAttendanceLock.IsLocked(closedPayrolls, r.EmployeeId, r.WorkDate))
                    .ToList();

                if (unlocked.Count > 0)
                {
                    _db.AttendanceRecords.RemoveRange(unlocked);
                    removedUnlocked = true;
                }

                if (locked.Count == 0)
                    _db.AttendanceImports.Remove(import);
            }

            return removedUnlocked;
        }

        private async Task RemoveConflictingRecordsAsync(
            int projectId,
            List<AttendancePreviewRow> rows,
            List<ClosedPayrollWindow> closedPayrolls,
            CancellationToken cancellationToken)
        {
            var employeeIds = rows
                .Where(r => r.EmployeeId.HasValue)
                .Select(r => r.EmployeeId!.Value)
                .Distinct()
                .ToList();

            var dates = rows
                .Select(r => AttendanceDisplay.UsableDate(r.WorkDate))
                .Where(d => d.HasValue)
                .Select(d => d!.Value.Date)
                .Distinct()
                .ToList();

            var incomingNullEmployeeIds = rows
                .Where(r => r.EmployeeId.HasValue && !AttendanceDisplay.UsableDate(r.WorkDate).HasValue)
                .Select(r => r.EmployeeId!.Value)
                .Distinct()
                .ToList();

            // The unique employee+date index is global, so conflicts are not limited to this project.
            var existing = await _db.AttendanceRecords
                .Where(r =>
                    r.EmployeeId != null && employeeIds.Contains(r.EmployeeId.Value) &&
                    (
                        (r.WorkDate != null && r.WorkDate.Value.Year < 1900) ||
                        (r.WorkDate != null && dates.Contains(r.WorkDate.Value.Date)) ||
                        (incomingNullEmployeeIds.Contains(r.EmployeeId.Value) &&
                         (r.WorkDate == null || r.WorkDate.Value.Year < 1900))
                    ))
                .ToListAsync(cancellationToken);

            existing = existing
                .Where(r => !PayrollAttendanceLock.IsLocked(closedPayrolls, r.EmployeeId, r.WorkDate))
                .ToList();

            if (existing.Count > 0)
                _db.AttendanceRecords.RemoveRange(existing);
        }

        private static List<AttendancePreviewRow> DeduplicateRows(List<AttendancePreviewRow> rows)
        {
            foreach (var row in rows)
                row.WorkDate = AttendanceDisplay.UsableDate(row.WorkDate);

            return rows
                .GroupBy(r => r.EmployeeId.HasValue
                    ? "e:" + r.EmployeeId.Value + ":" + (r.WorkDate?.ToString("yyyy-MM-dd") ?? "none")
                    : "u:" + (r.ExternalUserId ?? "").Trim().ToLowerInvariant()
                        + ":" + (r.EmployeeName ?? "").Trim().ToLowerInvariant()
                        + ":" + (r.WorkDate?.ToString("yyyy-MM-dd") ?? "none"))
                .Select(g => g
                    .OrderByDescending(r => PunchScore(r))
                    .ThenByDescending(r => r.Matched)
                    .First())
                .ToList();
        }

        private static int PunchScore(AttendancePreviewRow row)
        {
            var score = 0;
            if (!string.IsNullOrWhiteSpace(row.TimeIn1)) score++;
            if (!string.IsNullOrWhiteSpace(row.TimeOut1)) score++;
            if (!string.IsNullOrWhiteSpace(row.TimeIn2)) score++;
            if (!string.IsNullOrWhiteSpace(row.TimeOut2)) score++;
            if (!string.IsNullOrWhiteSpace(row.OvertimeIn)) score++;
            if (!string.IsNullOrWhiteSpace(row.OvertimeOut)) score++;
            return score;
        }

        private static bool PeriodsOverlap(AttendanceImport import, DateTime start, DateTime end)
        {
            var recordDates = import.Records
                .Select(r => AttendanceDisplay.UsableDate(r.WorkDate))
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToList();

            var importStart = AttendanceDisplay.UsableDate(import.PeriodStart)
                ?? AttendanceDisplay.UsableDate(import.PeriodEnd)
                ?? (recordDates.Count > 0 ? recordDates.Min() : (DateTime?)null);
            var importEnd = AttendanceDisplay.UsableDate(import.PeriodEnd)
                ?? AttendanceDisplay.UsableDate(import.PeriodStart)
                ?? (recordDates.Count > 0 ? recordDates.Max() : (DateTime?)null);

            if (!importStart.HasValue || !importEnd.HasValue)
                return false;

            return importStart.Value <= end && importEnd.Value >= start;
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

            if (await PayrollAttendanceLock.IsLockedAsync(
                    _db, record.ProjectId, record.EmployeeId, record.WorkDate, cancellationToken))
            {
                return "Payroll for this employee is already approved. Attendance cannot be edited.";
            }

            record.TimeIn1 = EmptyToNull(timeIn1);
            record.TimeOut1 = EmptyToNull(timeOut1);
            record.TimeIn2 = EmptyToNull(timeIn2);
            record.TimeOut2 = EmptyToNull(timeOut2);
            record.OvertimeIn = EmptyToNull(overtimeIn);
            record.OvertimeOut = EmptyToNull(overtimeOut);
            AttendanceRules.Apply(record);

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
                if (row != null)
                    AttendanceRules.Apply(row);
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
                    Status = row == null
                        ? AttendanceStatuses.Absent
                        : AttendanceStatuses.Display(row.Status),
                    RegularHours = AttendanceRules.RegularHours(row?.TimeIn1, row?.TimeOut1, row?.TimeIn2, row?.TimeOut2),
                    OvertimeHours = AttendanceRules.OvertimeHours(
                        row?.TimeIn1, row?.TimeOut1, row?.TimeIn2, row?.TimeOut2, row?.OvertimeIn, row?.OvertimeOut)
                });
            }

            edit.DaysWorked = edit.Days.Count(d => AttendanceStatuses.CountsAsFullDay(d.Status));
            edit.DaysAbsent = edit.Days.Count(d => d.Status == AttendanceStatuses.Absent);
            edit.DaysLate = edit.Days.Count(d => AttendanceStatuses.CountsAsLate(d.Status));
            edit.DaysIncomplete = edit.Days.Count(d => AttendanceStatuses.IsHalfDay(d.Status));
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
                        ProjectId = import.ProjectId,
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
                AttendanceRules.Apply(record);
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

            fileStart = AttendanceDisplay.UsableDate(fileStart);
            fileEnd = AttendanceDisplay.UsableDate(fileEnd);

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

        // Extracts every row from the file exactly as it appears (raw name / raw ID),
        // and separately records whether the auto-matcher found a system employee for it.
        private static List<AttendancePreviewRow> MapRows(AttendanceParseResult parsed, IReadOnlyList<Employee> employees)
        {
            var rows = new List<AttendancePreviewRow>();
            foreach (var row in parsed.Rows)
            {
                var employeeId = AttendanceFileParser.MatchEmployeeId(employees, row.ExternalUserId, row.EmployeeName);
                var employee = employeeId.HasValue
                    ? employees.FirstOrDefault(e => e.EmployeeId == employeeId.Value)
                    : null;

                AttendanceRules.Apply(row);

                rows.Add(new AttendancePreviewRow
                {
                    EmployeeId = employeeId,
                    // Raw, unfiltered values exactly as extracted from the uploaded file.
                    DisplayId = string.IsNullOrWhiteSpace(row.ExternalUserId) ? "" : row.ExternalUserId.Trim(),
                    ExternalUserId = row.ExternalUserId,
                    EmployeeName = string.IsNullOrWhiteSpace(row.EmployeeName) ? "" : row.EmployeeName.Trim(),
                    // Populated only when the auto-matcher (or a manual match) resolves a system employee.
                    MatchedEmployeeName = employee?.FullName,
                    WorkDate = AttendanceDisplay.UsableDate(row.WorkDate)
                        ?? AttendanceDisplay.UsableDate(parsed.PeriodStart),
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
                    Status = row.Status,
                    Matched = employeeId.HasValue,
                    Note = employeeId.HasValue
                        ? null
                        : "This name does not match any employee in the system. Select the correct employee to match this record."
                });
            }

            return rows;
        }

        // Applies staff-selected matches (from the "Select employee" dropdown in the preview)
        // onto the raw rows before saving.
        private static void ApplyManualMatches(
            List<AttendancePreviewRow> rows, string? manualMatchesJson, IReadOnlyList<Employee> employees)
        {
            if (string.IsNullOrWhiteSpace(manualMatchesJson) || rows.Count == 0)
                return;

            List<AttendanceManualMatch>? matches;
            try
            {
                matches = System.Text.Json.JsonSerializer.Deserialize<List<AttendanceManualMatch>>(
                    manualMatchesJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (System.Text.Json.JsonException)
            {
                return;
            }

            if (matches == null || matches.Count == 0)
                return;

            foreach (var m in matches)
            {
                var employee = employees.FirstOrDefault(e => e.EmployeeId == m.EmployeeId);
                if (employee == null)
                    continue;

                var date = AttendanceDisplay.UsableDate(m.WorkDate);
                var row = rows.FirstOrDefault(r =>
                    AttendanceDisplay.UsableDate(r.WorkDate) == date &&
                    string.Equals(r.ExternalUserId, m.ExternalUserId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(r.EmployeeName, m.EmployeeName, StringComparison.OrdinalIgnoreCase));
                if (row == null)
                    continue;

                row.EmployeeId = employee.EmployeeId;
                row.Matched = true;
                row.MatchedEmployeeName = employee.FullName;
                row.Note = null;
            }
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
                var date = AttendanceDisplay.UsableDate(edit.WorkDate);
                var row = rows.FirstOrDefault(r =>
                    AttendanceDisplay.UsableDate(r.WorkDate) == date &&
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
                var computed = new AttendanceRecord
                {
                    TimeIn1 = row.TimeIn1,
                    TimeOut1 = row.TimeOut1,
                    TimeIn2 = row.TimeIn2,
                    TimeOut2 = row.TimeOut2,
                    OvertimeIn = row.OvertimeIn,
                    OvertimeOut = row.OvertimeOut,
                    WorkHoursActual = row.WorkHoursActual
                };
                AttendanceRules.Apply(computed);
                row.WorkHoursActual = computed.WorkHoursActual;
                row.OvertimeHours = computed.OvertimeHours;
                row.LateMinutes = computed.LateMinutes;
                row.EarlyMinutes = computed.EarlyMinutes;
                row.Status = computed.Status;
            }
        }

        private static string? EmptyToNull(string? value)
        {
            var text = (value ?? "").Trim();
            if (text.Length == 0 || text == "—" || text == "——")
                return null;
            return text;
        }

        private static string DescribeSaveError(Exception ex)
        {
            var sql = ex.GetBaseException().Message;
            if (sql.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
            {
                return "The attendance tables are missing. Restart the app and import again.";
            }

            if (sql.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase))
            {
                return "The attendance tables are missing a required column. Restart the app so the database can update, then import again.";
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

            if (sql.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("UNIQUE KEY", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("unique index", StringComparison.OrdinalIgnoreCase))
            {
                return "This file has more than one row for the same employee on the same date, or that day was already imported. Check the dates in the file and try again.";
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
}
