using RSDSystem.Models;

namespace RSDSystem.Helpers
{
    /// <summary>
    /// Shift rules for a punch row: 8:00–12:00 morning, 13:00–17:00 afternoon,
    /// 30 min late grace, 15 min early-out grace. Apply() sets hours + Status.
    /// Called while parsing/importing attendance, before payroll uses the totals.
    /// </summary>
    public static class AttendanceRules
    {
        public static readonly TimeSpan MorningStart = new(8, 0, 0);
        public static readonly TimeSpan MorningEnd = new(12, 0, 0);
        public static readonly TimeSpan AfternoonStart = new(13, 0, 0);
        public static readonly TimeSpan ShiftEnd = new(17, 0, 0);
        public static readonly TimeSpan LateGrace = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan EarlyGrace = TimeSpan.FromMinutes(15);

        public static TimeSpan LateAfter => MorningStart + LateGrace;
        public static TimeSpan EarlyBefore => ShiftEnd - EarlyGrace;

        /// <summary>
        /// Fills hours, late/early minutes, and <see cref="AttendanceRecord.Status"/> for one punch row.
        /// Called during file parse, import preview, records list, and month-edit save so payroll later totals the same Status values.
        /// </summary>
        public static void Apply(AttendanceRecord row)
        {
            if (row == null)
                return;

            if (HasAnyPunch(row))
            {
                row.WorkHoursActual = RegularHours(row.TimeIn1, row.TimeOut1, row.TimeIn2, row.TimeOut2);
                row.OvertimeHours = OvertimeHours(
                    row.TimeIn1, row.TimeOut1, row.TimeIn2, row.TimeOut2, row.OvertimeIn, row.OvertimeOut);
                row.LateMinutes = LateMinutes(row.TimeIn1);
                row.EarlyMinutes = EarlyMinutes(row.TimeOut1, row.TimeOut2);
            }

            row.Status = Status(row);
        }

        /// <summary>
        /// Paid regular hours: overlap of the four punch times with 8:00–12:00 and 13:00–17:00 only.
        /// Punches outside those windows do not count here; they may count as overtime instead.
        /// </summary>
        public static decimal RegularHours(string? in1, string? out1, string? in2, string? out2)
        {
            decimal hours = 0;
            foreach (var (timeIn, timeOut) in new[] { (in1, out1), (in2, out2) })
            {
                hours += OverlapHours(timeIn, timeOut, MorningStart, MorningEnd);
                hours += OverlapHours(timeIn, timeOut, AfternoonStart, ShiftEnd);
            }

            return Math.Round(hours, 2);
        }

        /// <summary>
        /// Overtime hours after 17:00. Prefers dedicated OT in/out punches; otherwise uses any regular punches past shift end.
        /// </summary>
        public static decimal OvertimeHours(
            string? in1, string? out1, string? in2, string? out2, string? overtimeIn, string? overtimeOut)
        {
            var dayEnd = TimeSpan.FromDays(1);
            var punchedOt = OverlapHours(overtimeIn, overtimeOut, ShiftEnd, dayEnd);
            if (punchedOt > 0)
                return punchedOt;

            return Math.Round(
                OverlapHours(in1, out1, ShiftEnd, dayEnd)
                + OverlapHours(in2, out2, ShiftEnd, dayEnd), 2);
        }

        /// <summary>
        /// Minutes past the 8:00 start when the first punch is after the 30-minute grace (8:30).
        /// Returns 0 when on time or when time-in is missing. Feeds Late / LateEarlyOff status.
        /// </summary>
        public static int LateMinutes(string? timeIn)
        {
            if (!AttendanceDisplay.TryParseTime(timeIn, out var firstIn) || firstIn <= LateAfter)
                return 0;

            return Math.Max(0, (int)(firstIn - MorningStart).TotalMinutes);
        }

        /// <summary>
        /// Minutes before 17:00 when the last afternoon punch is before the 15-minute early-out grace (16:45).
        /// Uses TimeOut2 if present, otherwise TimeOut1 when it is an afternoon punch.
        /// </summary>
        public static int EarlyMinutes(string? timeOut1, string? timeOut2)
        {
            TimeSpan? lastOut = null;
            if (AttendanceDisplay.TryParseTime(timeOut2, out var out2))
                lastOut = out2;
            else if (AttendanceDisplay.TryParseTime(timeOut1, out var out1) && out1 >= AfternoonStart)
                lastOut = out1;

            if (!lastOut.HasValue || lastOut.Value >= EarlyBefore)
                return 0;

            return Math.Max(0, (int)(ShiftEnd - lastOut.Value).TotalMinutes);
        }

        /// <summary>
        /// Classifies the day as Absent, Half-day, Late, Early Off, Late/Early Off, or Complete from which punches exist.
        /// Payroll generation later counts Complete/Late days as worked and Half-day as incomplete.
        /// </summary>
        public static string Status(AttendanceRecord row)
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

            var morningComplete = hasIn1 && hasOut1;
            var afternoonComplete = hasIn2 && hasOut2;
            var morningOpen = hasIn1 ^ hasOut1;
            var afternoonOpen = hasIn2 ^ hasOut2;
            var overtimeOpen = hasOtIn && !hasOtOut;

            // Half-day: only one complete session, or a session with a missing punch.
            if (morningOpen || afternoonOpen || overtimeOpen || (morningComplete != afternoonComplete))
                return AttendanceStatuses.HalfDay;

            var late = LateMinutes(row.TimeIn1) > 0;
            var early = EarlyMinutes(row.TimeOut1, row.TimeOut2) > 0;
            if (late && early)
                return AttendanceStatuses.LateEarlyOff;
            if (late)
                return AttendanceStatuses.Late;
            if (early)
                return AttendanceStatuses.EarlyOff;

            if (row.WorkHoursActual > 0 || morningComplete || afternoonComplete)
                return AttendanceStatuses.Complete;

            return AttendanceStatuses.HalfDay;
        }

        /// <summary>
        /// True when the row has at least one of the six punch fields filled. Apply skips hour math when this is false.
        /// </summary>
        private static bool HasAnyPunch(AttendanceRecord row) =>
            !string.IsNullOrWhiteSpace(row.TimeIn1)
            || !string.IsNullOrWhiteSpace(row.TimeOut1)
            || !string.IsNullOrWhiteSpace(row.TimeIn2)
            || !string.IsNullOrWhiteSpace(row.TimeOut2)
            || !string.IsNullOrWhiteSpace(row.OvertimeIn)
            || !string.IsNullOrWhiteSpace(row.OvertimeOut);

        /// <summary>
        /// Hours where a punch pair overlaps a shift window (morning, afternoon, or after 17:00).
        /// Overnight punch-out is shifted +1 day. Returns 0 if either punch cannot be parsed.
        /// </summary>
        private static decimal OverlapHours(string? timeIn, string? timeOut, TimeSpan windowStart, TimeSpan windowEnd)
        {
            if (!AttendanceDisplay.TryParseTime(timeIn, out var start)
                || !AttendanceDisplay.TryParseTime(timeOut, out var end))
                return 0;

            if (end < start)
                end += TimeSpan.FromDays(1);

            var from = start > windowStart ? start : windowStart;
            var to = end < windowEnd ? end : windowEnd;
            if (to <= from)
                return 0;

            return Math.Round((decimal)(to - from).TotalHours, 2);
        }
    }
}
