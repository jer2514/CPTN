using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using RSDSystem.Helpers;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    /// <summary>
    /// Result of parsing a file before matching employees or saving.
    /// </summary>
    public class AttendanceParseResult
    {
        public string Format { get; set; } = AttendanceFormats.Daily;
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public List<AttendanceRecord> Rows { get; set; } = new();
        public string? Error { get; set; }
    }

    /// <summary>
    /// Turns .xls/.xlsx/.csv/.txt into AttendanceRecord rows (no database yet).
    /// Detects daily vs statistic layout from headers. AttendanceRules.Apply runs per row.
    /// </summary>
    public static class AttendanceFileParser
    {
        private static readonly Regex LabeledDateRange = new(
            @"(?:Attendance\s*date|Date)\s*:?\s*(\d{4}-\d{2}-\d{2})\s*[~\-–]\s*(\d{4}-\d{2}-\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BareDateRange = new(
            @"(\d{4}-\d{2}-\d{2})\s*[~\-–]\s*(\d{4}-\d{2}-\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CardDate = new(
            @"^(\d{1,2})[\s\u00A0\u3000]+([A-Za-z]{2,9})\.?$",
            RegexOptions.Compiled);

        private static readonly Regex BareDay = new(@"^(\d{1,2})$", RegexOptions.Compiled);

        private static readonly Regex WeekdayOnly = new(
            @"^(sun|mon|tue|wed|thu|fri|sat|su|mo|tu|we|th|fr|sa)\.?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InlineField = new(
            @"^(Name|User\s*ID|Employee\s*ID)\s*:?\s*(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static AttendanceParseResult Parse(Stream file, string fileName)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var ext = Path.GetExtension(fileName).ToLowerInvariant();

            if (ext is ".csv" or ".txt")
                return ParseTable(ReadCsv(file), fileName);

            try
            {
                using var reader = ExcelReaderFactory.CreateReader(file);
                var tables = ReadExcelTables(reader);
                if (tables.Count == 0)
                    return new AttendanceParseResult { Error = "The Excel file has no sheets." };

                var table = tables
                    .OrderByDescending(ScoreTimeCardSheet)
                    .First();
                return ParseTable(table, fileName);
            }
            catch (Exception ex)
            {
                return new AttendanceParseResult { Error = "Could not read the attendance file. " + ex.Message };
            }
        }

        public static string DeriveStatus(AttendanceRecord row)
        {
            AttendanceRules.Apply(row);
            return row.Status;
        }

        public static int? MatchEmployeeId(IEnumerable<Employee> employees, string externalUserId, string name)
        {
            var list = employees as IList<Employee> ?? employees.ToList();
            var seq = EmployeeIds.Sequence(externalUserId);
            if (seq.HasValue)
            {
                var byCode = list.FirstOrDefault(e => EmployeeIds.Sequence(e.EmployeeCode) == seq);
                if (byCode != null) return byCode.EmployeeId;

                var digits = new string((externalUserId ?? "").Where(char.IsDigit).ToArray());
                if (digits.Length > 5 && int.TryParse(digits[^5..], out var tail) && tail > 0)
                {
                    var byTail = list.FirstOrDefault(e => EmployeeIds.Sequence(e.EmployeeCode) == tail);
                    if (byTail != null) return byTail.EmployeeId;
                }
            }

            var needle = (name ?? "").Trim();
            if (needle.Length == 0) return null;

            var byName = list.FirstOrDefault(e =>
                string.Equals(e.FullName, needle, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.FirstName, needle, StringComparison.OrdinalIgnoreCase));
            return byName?.EmployeeId;
        }

        private static AttendanceParseResult ParseTable(DataTable table, string fileName)
        {
            if (LooksLikeEmployeeTimeCard(table))
                return ParseEmployeeTimeCard(table, fileName);

            return ParseFlatTable(table, fileName);
        }

        private static bool LooksLikeEmployeeTimeCard(DataTable table)
        {
            var scan = table.Rows.Count;
            for (var r = 0; r < scan; r++)
            {
                var joined = string.Join(" ", RowValues(table, r)).ToLowerInvariant();
                if (joined.Contains("employee attendance table") || joined.Contains("time card")
                    || joined.Contains("timecard"))
                    return true;
                if (joined.Contains("before noon") && joined.Contains("after noon"))
                    return true;
                if (joined.Contains("beforenoon") || (joined.Contains("before") && joined.Contains("noon")
                    && joined.Contains("after")))
                    return true;
            }

            return false;
        }

        private static AttendanceParseResult ParseEmployeeTimeCard(DataTable table, string fileName)
        {
            var result = new AttendanceParseResult { Format = AttendanceFormats.Daily };
            ApplyPeriod(table, result);

            var blocks = FindEmployeeBlocks(table);
            if (blocks.Count == 0)
            {
                return new AttendanceParseResult
                {
                    Error = "Could not find employee time cards (Name / User ID) in " + fileName + "."
                };
            }

            foreach (var block in blocks)
                ReadTimeCardBlock(table, block, result);

            if (result.Rows.Count == 0)
                result.Error = "No time-card rows were found in the file.";

            return result;
        }

        public static void ExpandToDateRange(AttendanceParseResult result, DateTime start, DateTime end)
        {
            if (result.Rows.Count == 0)
                return;
            if (start.Year < 1900 || end.Year < 1900)
                return;

            var rangeStart = start.Date;
            var rangeEnd = end.Date;
            if (rangeEnd < rangeStart)
                rangeEnd = rangeStart;

            result.PeriodStart = rangeStart;
            result.PeriodEnd = rangeEnd;

            var expanded = new List<AttendanceRecord>();
            foreach (var group in result.Rows.GroupBy(r => (r.ExternalUserId, r.EmployeeName)))
            {
                var byDate = new Dictionary<DateTime, AttendanceRecord>();
                foreach (var row in group.Where(r => r.WorkDate.HasValue))
                {
                    var day = row.WorkDate!.Value.Date;
                    if (day < rangeStart || day > rangeEnd)
                        continue;
                    byDate[day] = row;
                }

                var sample = group.First();
                for (var day = rangeStart; day <= rangeEnd; day = day.AddDays(1))
                {
                    if (byDate.TryGetValue(day, out var existing))
                    {
                        existing.PeriodStart = rangeStart;
                        existing.PeriodEnd = rangeEnd;
                        existing.WorkDate = day;
                        if (string.IsNullOrWhiteSpace(existing.Status))
                            existing.Status = DeriveStatus(existing);
                        expanded.Add(existing);
                        continue;
                    }

                    expanded.Add(new AttendanceRecord
                    {
                        ExternalUserId = sample.ExternalUserId,
                        EmployeeName = sample.EmployeeName,
                        WorkDate = day,
                        PeriodStart = rangeStart,
                        PeriodEnd = rangeEnd,
                        Status = AttendanceStatuses.Absent
                    });
                }
            }

            result.Rows = expanded;
        }

        private static List<EmployeeBlock> FindEmployeeBlocks(DataTable table)
        {
            var starts = new SortedSet<int>();
            var scan = Math.Min(table.Rows.Count, 20);
            for (var r = 0; r < scan; r++)
            {
                var values = RowValues(table, r);
                for (var c = 0; c < values.Length; c++)
                {
                    var key = NormalizeHeader(values[c]);
                    var inline = InlineField.Match(values[c]);
                    if (key is "name" or "userid" or "employeeid" || inline.Success)
                        starts.Add(SnapBlockStart(table, c));
                }
            }

            var edges = starts.ToList();
            var blocks = new List<EmployeeBlock>();
            for (var i = 0; i < edges.Count; i++)
            {
                var start = edges[i];
                var end = i + 1 < edges.Count ? edges[i + 1] - 1 : table.Columns.Count - 1;
                if (end - start < 6)
                    end = Math.Min(table.Columns.Count - 1, start + 15);

                var block = ReadEmployeeHeader(table, start, end);
                if (block != null)
                    blocks.Add(block);
            }

            return blocks;
        }

        private static int SnapBlockStart(DataTable table, int anchor)
        {
            var start = anchor;
            var foundEdge = false;
            for (var c = anchor; c >= 0; c--)
            {
                var keys = ColumnKeys(table, c);
                var isEdge = keys.Contains("dept") || keys.Contains("department")
                    || keys.Contains("timecard") || keys.Contains("date") || keys.Contains("dateweekday");
                var isSeparator = foundEdge && c < anchor && keys.Count == 0;
                if (isSeparator)
                    break;

                start = c;
                if (isEdge)
                    foundEdge = true;
            }

            return start;
        }

        private static HashSet<string> ColumnKeys(DataTable table, int col)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scan = Math.Min(table.Rows.Count, 25);
            for (var r = 0; r < scan; r++)
            {
                var values = RowValues(table, r);
                if (col >= values.Length || string.IsNullOrWhiteSpace(values[col]))
                    continue;
                keys.Add(NormalizeHeader(values[col]));
            }

            return keys;
        }

        private static EmployeeBlock? ReadEmployeeHeader(DataTable table, int colStart, int colEnd)
        {
            var block = new EmployeeBlock { ColStart = colStart, ColEnd = colEnd };
            var scan = Math.Min(table.Rows.Count, 20);

            for (var r = 0; r < scan; r++)
            {
                var values = RowValues(table, r);
                for (var c = colStart; c <= colEnd && c < values.Length; c++)
                {
                    var cell = values[c];
                    if (string.IsNullOrWhiteSpace(cell))
                        continue;

                    var inline = InlineField.Match(cell);
                    if (inline.Success)
                    {
                        AssignHeader(block, inline.Groups[1].Value, inline.Groups[2].Value.Trim());
                        continue;
                    }

                    var key = NormalizeHeader(cell);
                    if (key is "name" or "userid" or "employeeid")
                    {
                        var value = ValueBesideOrBelow(table, r, c, colEnd);
                        AssignHeader(block, cell, value);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(block.UserId) && string.IsNullOrWhiteSpace(block.Name))
                return null;

            return block;
        }

        private static void AssignHeader(EmployeeBlock block, string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || IsFieldLabel(value))
                return;

            var key = NormalizeHeader(label);
            if (key.Contains("name") && string.IsNullOrWhiteSpace(block.Name))
                block.Name = value.Trim();
            else if ((key.Contains("userid") || key.Contains("employeeid") || key == "id")
                     && string.IsNullOrWhiteSpace(block.UserId))
                block.UserId = value.Trim();
        }

        private static string ValueBesideOrBelow(DataTable table, int row, int col, int colEnd)
        {
            var values = RowValues(table, row);
            for (var c = col + 1; c <= Math.Min(colEnd, col + 4) && c < values.Length; c++)
            {
                if (!string.IsNullOrWhiteSpace(values[c]) && !IsFieldLabel(values[c]))
                    return values[c].Trim();
            }

            for (var r = row + 1; r <= Math.Min(table.Rows.Count - 1, row + 2); r++)
            {
                var next = RowValues(table, r);
                foreach (var c in new[] { col, col + 1 })
                {
                    if (c <= colEnd && c < next.Length && !string.IsNullOrWhiteSpace(next[c]) && !IsFieldLabel(next[c]))
                        return next[c].Trim();
                }
            }

            return "";
        }

        private static void ReadTimeCardBlock(DataTable table, EmployeeBlock block, AttendanceParseResult result)
        {
            var columns = MapTimeCardColumns(table, block.ColStart, block.ColEnd);

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var values = RowValues(table, r);
                var dateCol = columns?.DateCol;
                var dateText = dateCol.HasValue ? CellAt(values, dateCol) : "";
                if (string.IsNullOrWhiteSpace(dateText) || !LooksLikeCardDate(dateText))
                {
                    dateCol = FindDateColumn(values, block.ColStart, block.ColEnd);
                    dateText = dateCol.HasValue ? CellAt(values, dateCol) : "";
                }

                if (string.IsNullOrWhiteSpace(dateText) || IsSectionLabel(dateText) || IsPeriodText(dateText))
                    continue;

                var workDate = ResolveCardDate(dateText, result.PeriodStart, result.PeriodEnd, true);
                if (!workDate.HasValue)
                    continue;

                var row = new AttendanceRecord
                {
                    ExternalUserId = block.UserId,
                    EmployeeName = block.Name,
                    PeriodStart = result.PeriodStart,
                    PeriodEnd = result.PeriodEnd,
                    WorkDate = workDate,
                    TimeIn1 = NormalizeTime(CellAt(values, columns?.In1)),
                    TimeOut1 = NormalizeTime(CellAt(values, columns?.Out1)),
                    TimeIn2 = NormalizeTime(CellAt(values, columns?.In2)),
                    TimeOut2 = NormalizeTime(CellAt(values, columns?.Out2)),
                    OvertimeIn = NormalizeTime(CellAt(values, columns?.OtIn)),
                    OvertimeOut = NormalizeTime(CellAt(values, columns?.OtOut))
                };

                if (TimesEmpty(row))
                    ApplyTimesFromDateColumn(values, dateCol ?? block.ColStart, block.ColEnd, dateText, row);

                if (TimesEmpty(row))
                    ApplyFallbackTimes(values, block.ColStart, block.ColEnd, dateText, row);

                row.Status = DeriveStatus(row);
                result.Rows.Add(row);
            }
        }

        private static int? FindDateColumn(string[] values, int colStart, int colEnd)
        {
            for (var c = colStart; c <= colEnd && c < values.Length; c++)
            {
                if (LooksLikeCardDate(values[c]))
                    return c;
            }

            return null;
        }

        private static bool LooksLikeCardDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || IsSectionLabel(value) || IsPeriodText(value))
                return false;
            var text = value.Trim();
            if (CardDate.IsMatch(text))
                return true;
            if (text.Contains('-') || text.Contains('/') || text.Contains(','))
                return ParseDate(text).HasValue;
            return false;
        }

        private static TimeCardColumns? MapTimeCardColumns(DataTable table, int colStart, int colEnd)
        {
            for (var r = 0; r < table.Rows.Count; r++)
            {
                var values = RowValues(table, r);
                var next = r + 1 < table.Rows.Count ? RowValues(table, r + 1) : values;
                var joined = (string.Join(" ", Slice(values, colStart, colEnd)) + " "
                    + string.Join(" ", Slice(next, colStart, colEnd))).ToLowerInvariant();
                var hasInOutHeader = Slice(values, colStart, colEnd).Concat(Slice(next, colStart, colEnd))
                    .Any(v =>
                    {
                        var key = NormalizeHeader(v);
                        return key is "in" or "out";
                    });
                if (!hasInOutHeader)
                    continue;
                if (!(joined.Contains("noon") || joined.Contains("overtime") || joined.Contains("date")
                      || joined.Contains("weekday") || joined.Contains("time card")))
                    continue;
                var map = new TimeCardColumns { DateCol = colStart, DataStartRow = r + 1 };
                var group = "";

                for (var c = colStart; c <= colEnd && c < values.Length; c++)
                {
                    var parent = NormalizeHeader(values[c]);
                    var child = c < next.Length ? NormalizeHeader(next[c]) : "";

                    if (parent.Contains("date") || parent.Contains("weekday"))
                        map.DateCol = c;

                    if (parent.Contains("beforenoon") || parent == "am" || parent.Contains("morning"))
                        group = "am";
                    else if (parent.Contains("afternoon") || parent == "pm")
                        group = "pm";
                    else if (parent.Contains("overtime"))
                        group = "ot";

                    var inOut = child is "in" or "out" ? child
                        : parent is "in" or "out" ? parent
                        : "";
                    if (inOut == "in")
                        AssignIn(map, group, c);
                    else if (inOut == "out")
                        AssignOut(map, group, c);
                }

                if (map.In1 == null && map.In2 == null && map.Out1 == null && map.Out2 == null)
                    ApplyPositionalTimeColumns(map);

                if (map.In1 == null && map.In2 == null && map.Out1 == null && map.Out2 == null)
                    continue;

                var inOutOnNext = Slice(next, colStart, colEnd).Any(v =>
                {
                    var key = NormalizeHeader(v);
                    return key is "in" or "out";
                });
                map.DataStartRow = inOutOnNext ? r + 2 : r + 1;
                return map;
            }

            return null;
        }

        private static void AssignIn(TimeCardColumns map, string group, int col)
        {
            if (group == "am") map.In1 = col;
            else if (group == "pm") map.In2 = col;
            else if (group == "ot") map.OtIn = col;
            else if (map.In1 == null) map.In1 = col;
            else if (map.In2 == null) map.In2 = col;
            else map.OtIn ??= col;
        }

        private static void AssignOut(TimeCardColumns map, string group, int col)
        {
            if (group == "am") map.Out1 = col;
            else if (group == "pm") map.Out2 = col;
            else if (group == "ot") map.OtOut = col;
            else if (map.Out1 == null) map.Out1 = col;
            else if (map.Out2 == null) map.Out2 = col;
            else map.OtOut ??= col;
        }

        private static void ApplyPositionalTimeColumns(TimeCardColumns map)
        {
            var offset = 1;
            map.In1 ??= map.DateCol + offset;
            map.Out1 ??= map.DateCol + offset + 1;
            map.In2 ??= map.DateCol + offset + 2;
            map.Out2 ??= map.DateCol + offset + 3;
            map.OtIn ??= map.DateCol + offset + 4;
            map.OtOut ??= map.DateCol + offset + 5;
        }

        private static bool TimesEmpty(AttendanceRecord row) =>
            string.IsNullOrWhiteSpace(row.TimeIn1)
            && string.IsNullOrWhiteSpace(row.TimeOut1)
            && string.IsNullOrWhiteSpace(row.TimeIn2)
            && string.IsNullOrWhiteSpace(row.TimeOut2)
            && string.IsNullOrWhiteSpace(row.OvertimeIn)
            && string.IsNullOrWhiteSpace(row.OvertimeOut);

        private static void ApplyFallbackTimes(
            string[] values, int colStart, int colEnd, string dateText, AttendanceRecord row)
        {
            var times = new List<string>();
            for (var c = colStart; c <= colEnd && c < values.Length; c++)
            {
                if (string.Equals(values[c], dateText, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (WeekdayOnly.IsMatch(values[c] ?? ""))
                    continue;
                var time = NormalizeTime(values[c]);
                if (time != null)
                    times.Add(time);
            }

            AssignTimeList(row, times);
        }

        private static void ApplyTimesFromDateColumn(
            string[] values, int dateCol, int colEnd, string dateText, AttendanceRecord row)
        {
            var slots = new string?[6];
            var index = 0;
            for (var c = dateCol + 1; c <= colEnd && c < values.Length && index < slots.Length; c++)
            {
                if (string.Equals(values[c], dateText, StringComparison.OrdinalIgnoreCase)
                    || WeekdayOnly.IsMatch(values[c] ?? ""))
                    continue;

                slots[index++] = NormalizeTime(values[c]);
            }

            row.TimeIn1 = slots[0];
            row.TimeOut1 = slots[1];
            row.TimeIn2 = slots[2];
            row.TimeOut2 = slots[3];
            row.OvertimeIn = slots[4];
            row.OvertimeOut = slots[5];
        }

        private static void AssignTimeList(AttendanceRecord row, List<string> times)
        {
            if (times.Count > 0) row.TimeIn1 = times[0];
            if (times.Count > 1) row.TimeOut1 = times[1];
            if (times.Count > 2) row.TimeIn2 = times[2];
            if (times.Count > 3) row.TimeOut2 = times[3];
            if (times.Count > 4) row.OvertimeIn = times[4];
            if (times.Count > 5) row.OvertimeOut = times[5];
        }

        private static AttendanceParseResult ParseFlatTable(DataTable table, string fileName)
        {
            var result = new AttendanceParseResult();
            ApplyPeriod(table, result);
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var headerRow = -1;

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var values = RowValues(table, r);
                for (var c = 0; c < values.Length; c++)
                {
                    var key = NormalizeHeader(values[c]);
                    if (key.Length == 0) continue;
                    if (IsKnownHeader(key) && !headerMap.ContainsKey(key))
                        headerMap[key] = c;
                }

                if (headerMap.ContainsKey("userid") || headerMap.ContainsKey("employeeid"))
                {
                    headerRow = r;
                    if (r + 1 < table.Rows.Count)
                        MapSubHeaders(headerMap, values, RowValues(table, r + 1));
                    break;
                }
            }

            if (headerRow < 0)
            {
                return new AttendanceParseResult
                {
                    Error = "Could not find an Employee Attendance Table or User ID headers in " + fileName + "."
                };
            }

            var isDaily = headerMap.ContainsKey("date")
                || headerMap.ContainsKey("timein1")
                || headerMap.ContainsKey("timeout1");
            result.Format = isDaily ? AttendanceFormats.Daily : AttendanceFormats.Statistic;

            for (var r = headerRow + 1; r < table.Rows.Count; r++)
            {
                var values = RowValues(table, r);
                if (values.All(string.IsNullOrWhiteSpace))
                    continue;

                var userId = Cell(values, headerMap, "userid", "employeeid");
                var name = Cell(values, headerMap, "name", "employeename");
                if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(name))
                    continue;
                if (LooksLikeHeader(userId) || LooksLikeHeader(name))
                    continue;

                var row = new AttendanceRecord
                {
                    ExternalUserId = userId,
                    EmployeeName = name,
                    PeriodStart = result.PeriodStart,
                    PeriodEnd = result.PeriodEnd,
                    WorkDate = ParseDate(Cell(values, headerMap, "date")) ?? result.PeriodStart,
                    TimeIn1 = NormalizeTime(Cell(values, headerMap, "timein1", "timein")),
                    TimeOut1 = NormalizeTime(Cell(values, headerMap, "timeout1", "timeout")),
                    TimeIn2 = NormalizeTime(Cell(values, headerMap, "timein2")),
                    TimeOut2 = NormalizeTime(Cell(values, headerMap, "timeout2")),
                    OvertimeIn = NormalizeTime(Cell(values, headerMap, "overtimein")),
                    OvertimeOut = NormalizeTime(Cell(values, headerMap, "overtimeout")),
                    WorkHoursNormal = ParseDec(Cell(values, headerMap, "worktimenormal", "normal")),
                    WorkHoursActual = ParseDec(Cell(values, headerMap, "worktimeactual", "actual")),
                    LateMinutes = ParseInt(Cell(values, headerMap, "lateminute", "late")),
                    EarlyMinutes = ParseInt(Cell(values, headerMap, "earlyminute", "early")),
                    OvertimeHours = ParseDec(Cell(values, headerMap, "overtimenormal", "overtime")),
                    AbsenceDays = ParseDec(Cell(values, headerMap, "absence", "absenceday"))
                };

                var statusCell = Cell(values, headerMap, "status");
                row.Status = string.IsNullOrWhiteSpace(statusCell)
                    ? DeriveStatus(row)
                    : AttendanceStatuses.Display(statusCell.Trim());

                result.Rows.Add(row);
            }

            if (result.Rows.Count == 0)
                result.Error = "No attendance rows were found in the file.";

            return result;
        }

        private static void ApplyPeriod(DataTable table, AttendanceParseResult result)
        {
            for (var r = 0; r < Math.Min(table.Rows.Count, 20); r++)
            {
                var joined = string.Join(" ", RowValues(table, r));
                var match = LabeledDateRange.Match(joined);
                if (!match.Success)
                    match = BareDateRange.Match(joined);
                if (match.Success
                    && DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                    && DateTime.TryParseExact(match.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                {
                    result.PeriodStart = start;
                    result.PeriodEnd = end;
                    return;
                }
            }
        }

        private static DateTime? ResolveCardDate(string text, DateTime? periodStart, DateTime? periodEnd, bool allowBareDay)
        {
            if (string.IsNullOrWhiteSpace(text) || IsPeriodText(text) || IsSectionLabel(text))
                return null;

            var parsed = ParseDate(text);
            if (parsed.HasValue)
                return parsed;

            var match = CardDate.Match(text.Trim());
            if (!match.Success && allowBareDay)
                match = BareDay.Match(text.Trim());
            if (!match.Success || !periodStart.HasValue)
                return null;

            var day = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var cursor = periodStart.Value.Date;
            var end = (periodEnd ?? periodStart.Value.AddDays(45)).Date;
            while (cursor <= end)
            {
                if (cursor.Day == day)
                    return cursor;
                cursor = cursor.AddDays(1);
            }

            var daysInMonth = DateTime.DaysInMonth(periodStart.Value.Year, periodStart.Value.Month);
            if (day >= 1 && day <= daysInMonth)
                return new DateTime(periodStart.Value.Year, periodStart.Value.Month, day);

            return null;
        }

        private static void MapSubHeaders(Dictionary<string, int> map, string[] main, string[] sub)
        {
            for (var c = 0; c < Math.Max(main.Length, sub.Length); c++)
            {
                var parent = c < main.Length ? NormalizeHeader(main[c]) : "";
                var child = c < sub.Length ? NormalizeHeader(sub[c]) : "";
                if (child.Length == 0) continue;

                if (parent.Contains("worktime") || parent.Contains("work"))
                {
                    if (child == "normal") map["worktimenormal"] = c;
                    if (child == "actual") map["worktimeactual"] = c;
                }

                if (parent.Contains("late") && (child == "minute" || child == "minutes"))
                    map["lateminute"] = c;
                if (parent.Contains("early") && (child == "minute" || child == "minutes"))
                    map["earlyminute"] = c;
                if (parent.Contains("overtime") && child == "normal")
                    map["overtimenormal"] = c;
            }
        }

        private static bool IsKnownHeader(string key) =>
            key is "userid" or "employeeid" or "name" or "employeename" or "date"
                or "timein1" or "timeout1" or "timein2" or "timeout2"
                or "overtimein" or "overtimeout" or "status"
                or "worktime" or "late" or "early" or "overtime" or "absence" or "absenceday";

        private static bool LooksLikeHeader(string value)
        {
            var key = NormalizeHeader(value);
            return key is "userid" or "employeeid" or "name" or "normal" or "actual" or "times" or "minute";
        }

        private static bool IsFieldLabel(string value)
        {
            var key = NormalizeHeader(value);
            return key is "name" or "userid" or "employeeid" or "dept" or "department" or "company"
                or "date" or "absence" or "leave" or "trip" or "work" or "overtime" or "late" or "early"
                or "normal" or "special" or "times" or "minute" or "timecard";
        }

        private static bool IsSectionLabel(string value)
        {
            var key = NormalizeHeader(value);
            return key is "timecard" or "date" or "dateweekday" or "beforenoon" or "afternoon"
                or "overtime" or "in" or "out" or "name" or "userid";
        }

        private static string NormalizeHeader(string value)
        {
            var chars = (value ?? "").ToLowerInvariant()
                .Replace("(hrs.)", "")
                .Replace("(day)", "")
                .Where(ch => char.IsLetterOrDigit(ch))
                .ToArray();
            return new string(chars);
        }

        private static List<DataTable> ReadExcelTables(IExcelDataReader reader)
        {
            var tables = new List<DataTable>();
            do
            {
                var table = new DataTable(string.IsNullOrWhiteSpace(reader.Name) ? "Sheet" : reader.Name);
                while (reader.Read())
                {
                    var fields = Math.Max(reader.FieldCount, 0);
                    while (table.Columns.Count < fields)
                        table.Columns.Add("C" + table.Columns.Count, typeof(object));

                    var row = table.NewRow();
                    for (var i = 0; i < fields; i++)
                        row[i] = ReadExcelValue(reader, i);
                    table.Rows.Add(row);
                }

                if (table.Rows.Count > 0)
                    tables.Add(table);
            } while (reader.NextResult());

            return tables;
        }

        private static object ReadExcelValue(IExcelDataReader reader, int index)
        {
            if (index < 0 || index >= reader.FieldCount)
                return DBNull.Value;

            try
            {
                if (reader.IsDBNull(index))
                    return DBNull.Value;
            }
            catch (Exception)
            {
            }

            object? value = null;
            try
            {
                value = reader.GetValue(index);
            }
            catch (Exception)
            {
            }

            if (value != null && value != DBNull.Value)
                return value;

            try
            {
                return reader.GetDouble(index);
            }
            catch (Exception)
            {
            }

            try
            {
                return reader.GetDateTime(index);
            }
            catch (Exception)
            {
            }

            try
            {
                var text = reader.GetString(index);
                return string.IsNullOrWhiteSpace(text) ? DBNull.Value : text;
            }
            catch (Exception)
            {
                return DBNull.Value;
            }
        }

        private static int ScoreTimeCardSheet(DataTable table)
        {
            var score = table.Columns.Count;
            for (var r = 0; r < table.Rows.Count; r++)
            {
                var values = RowValues(table, r);
                var joined = string.Join(" ", values).ToLowerInvariant();
                if (joined.Contains("time card") || joined.Contains("timecard"))
                    score += 80;
                if (joined.Contains("employee attendance"))
                    score += 40;
                if (joined.Contains("before noon") || joined.Contains("after noon"))
                    score += 30;
                foreach (var cell in values)
                {
                    if (CardDate.IsMatch((cell ?? "").Trim()))
                        score += 10;
                    if ((cell ?? "").Contains(':'))
                        score += 2;
                }
            }

            return score;
        }

        private static DataTable ReadCsv(Stream file)
        {
            using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var table = new DataTable();
            while (reader.ReadLine() is { } line)
            {
                var cells = SplitCsv(line);
                if (table.Columns.Count == 0)
                {
                    for (var i = 0; i < cells.Length; i++)
                        table.Columns.Add("C" + i);
                }

                while (table.Columns.Count < cells.Length)
                    table.Columns.Add("C" + table.Columns.Count);

                var row = table.NewRow();
                for (var i = 0; i < cells.Length; i++)
                    row[i] = cells[i];
                table.Rows.Add(row);
            }

            return table;
        }

        private static string[] SplitCsv(string line)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            var quoted = false;
            foreach (var ch in line)
            {
                if (ch == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (ch == ',' && !quoted)
                {
                    values.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                current.Append(ch);
            }

            values.Add(current.ToString().Trim());
            return values.ToArray();
        }

        private static string[] RowValues(DataTable table, int row)
        {
            var values = new string[table.Columns.Count];
            for (var c = 0; c < table.Columns.Count; c++)
                values[c] = FormatCell(table.Rows[row][c]);
            return values;
        }

        private static string FormatCell(object? value)
        {
            if (value == null || value == DBNull.Value)
                return "";

            if (value is DateTime dt)
                return FormatDateTime(dt);

            if (value is TimeSpan span)
                return $"{(int)span.TotalHours:00}:{span.Minutes:00}";

            if (value is IConvertible && value is not string)
            {
                try
                {
                    var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    var formatted = FormatNumericCell(number);
                    if (formatted != null)
                        return formatted;
                }
                catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
                {
                }
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        }

        private static string FormatDateTime(DateTime dt)
        {
            if (dt.Year <= 1900)
                return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (dt.TimeOfDay == TimeSpan.Zero)
                return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private static string? FormatNumericCell(double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
                return null;

            var fraction = Math.Abs(number - Math.Truncate(number));
            if (number > 0 && number < 1)
            {
                var ts = TimeSpan.FromDays(number);
                return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
            }

            if (fraction > 0.0000001 && number > 60 && number < 2958466)
            {
                try
                {
                    var oa = DateTime.FromOADate(number);
                    if (oa.TimeOfDay > TimeSpan.Zero)
                        return oa.ToString("HH:mm", CultureInfo.InvariantCulture);
                    return oa.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                catch (ArgumentException)
                {
                    return null;
                }
            }

            return null;
        }

        private static string[] Slice(string[] values, int start, int end)
        {
            var list = new List<string>();
            for (var i = start; i <= end && i < values.Length; i++)
                list.Add(values[i]);
            return list.ToArray();
        }

        private static string CellAt(string[] values, int? index)
        {
            if (!index.HasValue || index.Value < 0 || index.Value >= values.Length)
                return "";
            return values[index.Value];
        }

        private static string Cell(string[] values, Dictionary<string, int> map, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (map.TryGetValue(key, out var index) && index >= 0 && index < values.Length)
                    return values[index];
            }

            return "";
        }

        private static string? NormalizeTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || IsFieldLabel(value) || IsSectionLabel(value) || IsPeriodText(value))
                return null;

            var text = value.Trim();
            if (CardDate.IsMatch(text) || BareDay.IsMatch(text))
                return null;

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                || DateTime.TryParse(text, out dt))
            {
                if (dt.TimeOfDay == TimeSpan.Zero && text.Length > 8)
                    return null;
                return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var ts))
                return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                var formatted = FormatNumericCell(number);
                if (formatted != null && formatted.Contains(':'))
                    return formatted;
                return null;
            }

            return text.Contains(':') ? text : null;
        }

        private static bool IsPeriodText(string value)
        {
            var text = (value ?? "").Trim();
            return text.Contains('~') || text.Contains("Attendance", StringComparison.OrdinalIgnoreCase)
                || BareDateRange.IsMatch(text);
        }

        private static decimal ParseDec(string value)
        {
            var text = (value ?? "").Replace(",", "").Trim();
            if (text.Contains('/'))
                text = text.Split('/')[0];
            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0;
        }

        private static int ParseInt(string value)
        {
            var text = (value ?? "").Replace(",", "").Trim();
            return int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0;
        }

        private static DateTime? ParseDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var text = value.Trim();
            if (Regex.IsMatch(text, @"^\d{1,2}:\d{2}"))
                return null;
            if (text.Contains(' ') && DateTime.TryParse(text.Split(' ')[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)
                && Regex.IsMatch(text.Split(' ')[0], @"^\d{4}-\d{2}-\d{2}$")
                && iso.Year >= 1900)
                return iso.Date;

            var formats = new[] { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "MMMM d, yyyy", "MMM d, yyyy" };
            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                && dt.Year >= 1900)
                return dt.Date;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)
                && dt.Year >= 1900)
                return dt.Date;
            return null;
        }

        private sealed class EmployeeBlock
        {
            public int ColStart { get; set; }
            public int ColEnd { get; set; }
            public string UserId { get; set; } = "";
            public string Name { get; set; } = "";
        }

        private sealed class TimeCardColumns
        {
            public int DateCol { get; set; }
            public int DataStartRow { get; set; }
            public int? In1 { get; set; }
            public int? Out1 { get; set; }
            public int? In2 { get; set; }
            public int? Out2 { get; set; }
            public int? OtIn { get; set; }
            public int? OtOut { get; set; }
        }
    }
}
