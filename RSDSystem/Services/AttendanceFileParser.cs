using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using RSDSystem.Helpers;
using RSDSystem.Models;

namespace RSDSystem.Services
{
    public class AttendanceParseResult
    {
        public string Format { get; set; } = AttendanceFormats.Daily;
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public List<AttendanceRecord> Rows { get; set; } = new();
        public string? Error { get; set; }
    }

    public static class AttendanceFileParser
    {
        private static readonly Regex LabeledDateRange = new(
            @"(?:Attendance\s*date|Date)\s*:?\s*(\d{4}-\d{2}-\d{2})\s*[~\-–]\s*(\d{4}-\d{2}-\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BareDateRange = new(
            @"(\d{4}-\d{2}-\d{2})\s*[~\-–]\s*(\d{4}-\d{2}-\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CardDate = new(
            @"^(\d{1,2})\s+([A-Za-z]{2,9})\.?$",
            RegexOptions.Compiled);

        private static readonly Regex BareDay = new(@"^(\d{1,2})$", RegexOptions.Compiled);

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
                var data = reader.AsDataSet(new ExcelDataSetConfiguration
                {
                    ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
                });

                if (data.Tables.Count == 0)
                    return new AttendanceParseResult { Error = "The Excel file has no sheets." };

                return ParseTable(data.Tables[0], fileName);
            }
            catch (Exception ex)
            {
                return new AttendanceParseResult { Error = "Could not read the attendance file. " + ex.Message };
            }
        }

        public static string DeriveStatus(AttendanceRecord row)
        {
            var hasIn1 = !string.IsNullOrWhiteSpace(row.TimeIn1);
            var hasOut1 = !string.IsNullOrWhiteSpace(row.TimeOut1);
            var hasIn2 = !string.IsNullOrWhiteSpace(row.TimeIn2);
            var hasOut2 = !string.IsNullOrWhiteSpace(row.TimeOut2);
            var hasOtIn = !string.IsNullOrWhiteSpace(row.OvertimeIn);
            var hasOtOut = !string.IsNullOrWhiteSpace(row.OvertimeOut);
            var anyPunch = hasIn1 || hasOut1 || hasIn2 || hasOut2 || hasOtIn || hasOtOut;

            if (!anyPunch && row.WorkHoursActual <= 0)
                return AttendanceStatuses.Absent;

            if (row.LateMinutes > 0)
                return AttendanceStatuses.Late;

            if ((hasIn1 ^ hasOut1) || (hasIn2 ^ hasOut2) || (hasOtIn && !hasOtOut))
                return AttendanceStatuses.Incomplete;

            if (row.WorkHoursNormal > 0 && row.WorkHoursActual < row.WorkHoursNormal * 0.9m)
                return AttendanceStatuses.Incomplete;

            if (row.WorkHoursActual > 0 || (hasIn1 && hasOut1) || (hasIn2 && hasOut2))
                return AttendanceStatuses.Complete;

            return AttendanceStatuses.Incomplete;
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
            var scan = Math.Min(table.Rows.Count, 25);
            for (var r = 0; r < scan; r++)
            {
                var joined = string.Join(" ", RowValues(table, r)).ToLowerInvariant();
                if (joined.Contains("employee attendance table") || joined.Contains("time card"))
                    return true;
                if (joined.Contains("before noon") && joined.Contains("after noon"))
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

        private static List<EmployeeBlock> FindEmployeeBlocks(DataTable table)
        {
            var labelCols = new List<int>();
            var scan = Math.Min(table.Rows.Count, 20);
            for (var r = 0; r < scan; r++)
            {
                var values = RowValues(table, r);
                for (var c = 0; c < values.Length; c++)
                {
                    var key = NormalizeHeader(values[c]);
                    var inline = InlineField.Match(values[c]);
                    if (key is "name" or "userid" or "employeeid" || inline.Success)
                    {
                        if (!labelCols.Any(existing => Math.Abs(existing - c) <= 1))
                            labelCols.Add(c);
                    }
                }
            }

            labelCols.Sort();
            var starts = new List<int>();
            foreach (var col in labelCols)
            {
                if (starts.Count == 0 || col - starts[^1] >= 10)
                    starts.Add(col);
            }

            var blocks = new List<EmployeeBlock>();
            for (var i = 0; i < starts.Count; i++)
            {
                var start = starts[i];
                var end = i + 1 < starts.Count ? starts[i + 1] - 1 : table.Columns.Count - 1;
                if (end - start < 6)
                    end = Math.Min(table.Columns.Count - 1, start + 15);

                var block = ReadEmployeeHeader(table, start, end);
                if (block != null)
                    blocks.Add(block);
            }

            return blocks;
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
            var startRow = columns?.DataStartRow ?? 0;
            var emptyStreak = 0;

            for (var r = startRow; r < table.Rows.Count; r++)
            {
                var values = RowValues(table, r);
                var dateText = columns != null
                    ? CellAt(values, columns.DateCol)
                    : FirstNonEmpty(values, block.ColStart, block.ColEnd);

                if (string.IsNullOrWhiteSpace(dateText))
                {
                    emptyStreak++;
                    if (emptyStreak >= 3)
                        break;
                    continue;
                }

                if (IsSectionLabel(dateText))
                    continue;

                var workDate = ResolveCardDate(dateText, result.PeriodStart, result.PeriodEnd, columns != null);
                if (!workDate.HasValue)
                {
                    emptyStreak++;
                    if (emptyStreak >= 3)
                        break;
                    continue;
                }

                emptyStreak = 0;
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

                if (columns == null)
                    ApplyFallbackTimes(values, block.ColStart, block.ColEnd, dateText, row);

                row.Status = DeriveStatus(row);
                result.Rows.Add(row);
            }
        }

        private static TimeCardColumns? MapTimeCardColumns(DataTable table, int colStart, int colEnd)
        {
            for (var r = 0; r < table.Rows.Count; r++)
            {
                var values = RowValues(table, r);
                var next = r + 1 < table.Rows.Count ? RowValues(table, r + 1) : values;
                var joined = (string.Join(" ", Slice(values, colStart, colEnd)) + " "
                    + string.Join(" ", Slice(next, colStart, colEnd))).ToLowerInvariant();
                if (!joined.Contains("in"))
                    continue;
                if (!(joined.Contains("noon") || joined.Contains("overtime") || joined.Contains("date")
                      || joined.Contains("weekday")))
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

                    var inOut = child is "in" or "out" ? child : parent is "in" or "out" ? parent : "";
                    if (inOut == "in")
                        AssignIn(map, group, c);
                    else if (inOut == "out")
                        AssignOut(map, group, c);
                }

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

        private static void ApplyFallbackTimes(
            string[] values, int colStart, int colEnd, string dateText, AttendanceRecord row)
        {
            var times = new List<string>();
            for (var c = colStart; c <= colEnd && c < values.Length; c++)
            {
                if (string.Equals(values[c], dateText, StringComparison.OrdinalIgnoreCase))
                    continue;
                var time = NormalizeTime(values[c]);
                if (time != null)
                    times.Add(time);
            }

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
                    : statusCell.Trim();

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
            {
                if (dt.Year <= 1900)
                    return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
                if (dt.TimeOfDay == TimeSpan.Zero)
                    return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return dt.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            if (value is double number && number > 0 && number < 1)
            {
                var ts = TimeSpan.FromDays(number);
                return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
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

        private static string FirstNonEmpty(string[] values, int start, int end)
        {
            for (var i = start; i <= end && i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                    return values[i];
            }

            return "";
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
            if (string.IsNullOrWhiteSpace(value) || IsFieldLabel(value) || IsSectionLabel(value))
                return null;

            var text = value.Trim();
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                || DateTime.TryParse(text, out dt))
                return dt.ToString("HH:mm", CultureInfo.InvariantCulture);

            if (TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out var ts))
                return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}";

            return text;
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
            if (text.Contains(' ') && DateTime.TryParse(text.Split(' ')[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso)
                && Regex.IsMatch(text.Split(' ')[0], @"^\d{4}-\d{2}-\d{2}$"))
                return iso.Date;

            var formats = new[] { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "MMMM d, yyyy", "MMM d, yyyy" };
            if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.Date;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ? dt.Date : null;
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
