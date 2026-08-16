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
        public string Format { get; set; } = AttendanceFormats.Statistic;
        public DateTime? PeriodStart { get; set; }
        public DateTime? PeriodEnd { get; set; }
        public List<AttendanceRecord> Rows { get; set; } = new();
        public string? Error { get; set; }
    }

    public static class AttendanceFileParser
    {
        private static readonly Regex DateRange = new(
            @"Date\s*:\s*(\d{4}-\d{2}-\d{2})\s*[~\-–]\s*(\d{4}-\d{2}-\d{2})",
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
            if (row.AbsenceDays > 0 && row.WorkHoursActual <= 0
                && string.IsNullOrWhiteSpace(row.TimeIn1) && string.IsNullOrWhiteSpace(row.TimeOut1))
                return AttendanceStatuses.Absent;

            if (row.LateMinutes > 0)
                return AttendanceStatuses.Late;

            if (!string.IsNullOrWhiteSpace(row.TimeIn1) && string.IsNullOrWhiteSpace(row.TimeOut1))
                return AttendanceStatuses.Incomplete;

            if (row.WorkHoursNormal > 0 && row.WorkHoursActual < row.WorkHoursNormal * 0.9m)
                return AttendanceStatuses.Incomplete;

            if (row.WorkHoursActual > 0
                || (!string.IsNullOrWhiteSpace(row.TimeIn1) && !string.IsNullOrWhiteSpace(row.TimeOut1)))
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

                // Device exports sometimes prefix a plant code (26000001 vs 00001).
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

        private static DataTable ReadCsv(Stream file)
        {
            using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var table = new DataTable();
            string? line;
            var rowIndex = 0;
            while ((line = reader.ReadLine()) != null)
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
                rowIndex++;
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

        private static AttendanceParseResult ParseTable(DataTable table, string fileName)
        {
            var result = new AttendanceParseResult();
            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var headerRow = -1;

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var values = RowValues(table, r);
                var joined = string.Join(" ", values);

                var dateMatch = DateRange.Match(joined);
                if (dateMatch.Success
                    && DateTime.TryParseExact(dateMatch.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
                    && DateTime.TryParseExact(dateMatch.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
                {
                    result.PeriodStart = start;
                    result.PeriodEnd = end;
                }

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
                    // Include the next row for sub-headers like Normal / Actual
                    if (r + 1 < table.Rows.Count)
                    {
                        var sub = RowValues(table, r + 1);
                        MapSubHeaders(headerMap, values, sub);
                    }
                    break;
                }
            }

            if (headerRow < 0)
                return new AttendanceParseResult { Error = "Could not find attendance headers (User ID / Employee ID) in " + fileName + "." };

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
                    TimeIn1 = EmptyToNull(Cell(values, headerMap, "timein1", "timein")),
                    TimeOut1 = EmptyToNull(Cell(values, headerMap, "timeout1", "timeout")),
                    TimeIn2 = EmptyToNull(Cell(values, headerMap, "timein2")),
                    TimeOut2 = EmptyToNull(Cell(values, headerMap, "timeout2")),
                    OvertimeIn = EmptyToNull(Cell(values, headerMap, "overtimein")),
                    OvertimeOut = EmptyToNull(Cell(values, headerMap, "overtimeout")),
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

        private static string NormalizeHeader(string value)
        {
            var chars = (value ?? "").ToLowerInvariant()
                .Replace("(hrs.)", "")
                .Replace("(day)", "")
                .Where(ch => char.IsLetterOrDigit(ch))
                .ToArray();
            return new string(chars);
        }

        private static string[] RowValues(DataTable table, int row)
        {
            var values = new string[table.Columns.Count];
            for (var c = 0; c < table.Columns.Count; c++)
                values[c] = Convert.ToString(table.Rows[row][c], CultureInfo.InvariantCulture)?.Trim() ?? "";
            return values;
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

        private static string? EmptyToNull(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
            var formats = new[] { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "MMMM d, yyyy", "MMM d, yyyy" };
            if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt.Date;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ? dt.Date : null;
        }
    }
}
