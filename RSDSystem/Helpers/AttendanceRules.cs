using RSDSystem.Models;

namespace RSDSystem.Helpers
{
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

        public static void Apply(AttendanceRecord row)
        {
            if (row == null)
                return;

            if (HasAnyPunch(row))
            {
                row.WorkHoursActual = RegularHours(row.TimeIn1, row.TimeOut1, row.TimeIn2, row.TimeOut2);
                row.LateMinutes = LateMinutes(row.TimeIn1);
                row.EarlyMinutes = EarlyMinutes(row.TimeOut1, row.TimeOut2);
            }

            ApplyOvertimeDecision(row);
            row.Status = Status(row);
        }

        public static decimal RegularHours(string? in1, string? out1, string? in2, string? out2)
        {
            var morningIn = CountedSessionIn(in1, MorningStart);
            var afternoonIn = CountedSessionIn(in2, AfternoonStart);
            var hours = 0;
            foreach (var (timeIn, timeOut) in new[] { (morningIn, out1), (afternoonIn, out2) })
            {
                hours += CountedWindowHours(timeIn, timeOut, MorningStart, MorningEnd);
                hours += CountedWindowHours(timeIn, timeOut, AfternoonStart, ShiftEnd);
            }

            return hours;
        }

        /// <summary>
        /// In by 8:30 counts from 8:00 (grace). After 8:30 the 8–9 hour is unpaid.
        /// Later arrivals snap up to the next full hour.
        /// </summary>
        public static string? CountedMorningIn(string? timeIn) =>
            CountedSessionIn(timeIn, MorningStart);

        /// <summary>
        /// Hours after 5:00 from Overtime In/Out, or from a regular clock-out after 5:00.
        /// These hours are not paid until an admin authorizes the overtime.
        /// </summary>
        public static decimal ClaimedOvertimeHours(
            string? in1, string? out1, string? in2, string? out2, string? overtimeIn, string? overtimeOut)
        {
            var dayEnd = TimeSpan.FromDays(1);
            var punched = CountedWindowHours(overtimeIn, overtimeOut, ShiftEnd, dayEnd);
            if (punched > 0)
                return punched;

            return CountedWindowHours(in1, out1, ShiftEnd, dayEnd)
                + CountedWindowHours(in2, out2, ShiftEnd, dayEnd);
        }

        public static decimal ClaimedOvertimeHours(AttendanceRecord row) =>
            ClaimedOvertimeHours(row.TimeIn1, row.TimeOut1, row.TimeIn2, row.TimeOut2, row.OvertimeIn, row.OvertimeOut);

        /// <summary>
        /// Overtime punches after 17:00. Linger clock-outs are not included.
        /// Paid overtime uses <see cref="PaidOvertimeHours"/>.
        /// </summary>
        public static decimal OvertimeHours(
            string? in1, string? out1, string? in2, string? out2, string? overtimeIn, string? overtimeOut)
        {
            _ = (in1, out1, in2, out2);
            return CountedWindowHours(overtimeIn, overtimeOut, ShiftEnd, TimeSpan.FromDays(1));
        }

        public static decimal PaidOvertimeHours(AttendanceRecord row)
        {
            if (!OvertimeDecisions.IsApproved(row.OvertimeDecision))
                return 0;
            return ClaimedOvertimeHours(row);
        }

        public static string? LastRegularOutClock(string? timeOut1, string? timeOut2)
        {
            if (!string.IsNullOrWhiteSpace(timeOut2))
                return timeOut2;
            if (AttendanceDisplay.TryParseTime(timeOut1, out var out1) && out1 >= AfternoonStart)
                return timeOut1;
            return null;
        }

        public static void FillAuthorizedOvertimePunches(AttendanceRecord row)
        {
            if (!string.IsNullOrWhiteSpace(row.OvertimeIn) && !string.IsNullOrWhiteSpace(row.OvertimeOut))
                return;

            var lastOut = LastRegularOutClock(row.TimeOut1, row.TimeOut2);
            if (string.IsNullOrWhiteSpace(lastOut))
                return;

            row.OvertimeIn = FormatClock(ShiftEnd);
            row.OvertimeOut = lastOut;
            if (AttendanceDisplay.TryParseTime(row.TimeOut2, out var out2) && out2 > ShiftEnd)
                row.TimeOut2 = FormatClock(ShiftEnd);
        }

        private static void ApplyOvertimeDecision(AttendanceRecord row)
        {
            var claimed = ClaimedOvertimeHours(row);
            var previousClaim = row.OvertimeClaimHours;
            var previousDecision = OvertimeDecisions.Normalize(row.OvertimeDecision);
            row.OvertimeClaimHours = claimed;

            if (claimed <= 0)
            {
                row.OvertimeDecision = OvertimeDecisions.None;
                row.OvertimeHours = 0;
                if (previousDecision != OvertimeDecisions.None)
                {
                    row.OvertimeReviewedBy = null;
                    row.OvertimeReviewedAt = null;
                    row.OvertimeReviewNote = null;
                }
                return;
            }

            if (OvertimeDecisions.IsFinal(previousDecision) && previousClaim == claimed)
                row.OvertimeDecision = previousDecision;
            else
                row.OvertimeDecision = OvertimeDecisions.Pending;

            row.OvertimeHours = OvertimeDecisions.IsApproved(row.OvertimeDecision) ? claimed : 0;
        }

        public static IReadOnlyList<AttendanceIssue> DetectIssues(AttendanceRecord row) =>
            DetectIssues(row.TimeIn1, row.TimeOut1, row.TimeIn2, row.TimeOut2, row.OvertimeIn, row.OvertimeOut);

        public static IReadOnlyList<AttendanceIssue> DetectIssues(
            string? in1, string? out1, string? in2, string? out2, string? overtimeIn, string? overtimeOut)
        {
            var issues = new List<AttendanceIssue>();
            var hasOtIn = !string.IsNullOrWhiteSpace(overtimeIn);
            var hasOtOut = !string.IsNullOrWhiteSpace(overtimeOut);
            var paidOt = OvertimeHours(in1, out1, in2, out2, overtimeIn, overtimeOut);
            var lastRegularOut = LastRegularOut(out1, out2);

            if (hasOtIn ^ hasOtOut)
            {
                issues.Add(new AttendanceIssue(
                    AttendanceIssueCodes.IncompleteOvertime,
                    "Overtime in and overtime out must both be filled. Incomplete overtime is not paid."));
            }

            if (hasOtIn
                && AttendanceDisplay.TryParseTime(overtimeIn, out var otIn)
                && otIn < ShiftEnd)
            {
                issues.Add(new AttendanceIssue(
                    AttendanceIssueCodes.OvertimeBeforeFive,
                    "Overtime in is before 5:00. Overtime only starts after the regular shift."));
            }

            if (lastRegularOut > ShiftEnd)
            {
                if (paidOt > 0)
                {
                    issues.Add(new AttendanceIssue(
                        AttendanceIssueCodes.AfternoonOutConflictsWithOt,
                        "Afternoon time-out is after 5:00 while overtime punches also exist. Afternoon out should be 5:00; extra time belongs in Overtime In/Out."));
                }
                else
                {
                    issues.Add(new AttendanceIssue(
                        AttendanceIssueCodes.LingerAfterShift,
                        "Timed out after 5:00 without overtime in/out. Admin must authorize this as overtime or reject it as staying late."));
                }
            }

            AddReversedSession(issues, in1, out1, "Before noon");
            AddReversedSession(issues, in2, out2, "After noon");

            if (AttendanceDisplay.TryParseTime(out1, out var morningOut)
                && AttendanceDisplay.TryParseTime(in2, out var afternoonIn)
                && morningOut > afternoonIn)
            {
                issues.Add(new AttendanceIssue(
                    AttendanceIssueCodes.OverlappingSessions,
                    "Before-noon out is after after-noon in. The two sessions overlap."));
            }

            return issues;
        }

        public static int LateMinutes(string? timeIn)
        {
            if (!AttendanceDisplay.TryParseTime(timeIn, out var firstIn) || firstIn <= LateAfter)
                return 0;

            return Math.Max(0, (int)(firstIn - MorningStart).TotalMinutes);
        }

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

        private static TimeSpan LastRegularOut(string? timeOut1, string? timeOut2)
        {
            if (AttendanceDisplay.TryParseTime(timeOut2, out var out2))
                return out2;
            if (AttendanceDisplay.TryParseTime(timeOut1, out var out1) && out1 >= AfternoonStart)
                return out1;
            return TimeSpan.Zero;
        }

        private static void AddReversedSession(
            List<AttendanceIssue> issues, string? timeIn, string? timeOut, string label)
        {
            if (!AttendanceDisplay.TryParseTime(timeIn, out var start)
                || !AttendanceDisplay.TryParseTime(timeOut, out var end)
                || end >= start)
                return;

            issues.Add(new AttendanceIssue(
                AttendanceIssueCodes.SessionOutBeforeIn,
                $"{label} time-out is earlier than time-in."));
        }

        private static bool HasAnyPunch(AttendanceRecord row) =>
            !string.IsNullOrWhiteSpace(row.TimeIn1)
            || !string.IsNullOrWhiteSpace(row.TimeOut1)
            || !string.IsNullOrWhiteSpace(row.TimeIn2)
            || !string.IsNullOrWhiteSpace(row.TimeOut2)
            || !string.IsNullOrWhiteSpace(row.OvertimeIn)
            || !string.IsNullOrWhiteSpace(row.OvertimeOut);

        /// <summary>
        /// In within 30 minutes of the session start counts from that hour.
        /// Later arrivals lose the current hour and start on the next one.
        /// </summary>
        private static string? CountedSessionIn(string? timeIn, TimeSpan sessionStart)
        {
            if (!AttendanceDisplay.TryParseTime(timeIn, out var firstIn))
                return timeIn;

            if (firstIn <= sessionStart + LateGrace)
                return FormatClock(sessionStart);

            var hourStart = TimeSpan.FromHours(Math.Floor(firstIn.TotalHours));
            if (firstIn == hourStart)
                return FormatClock(firstIn);

            return FormatClock(hourStart + TimeSpan.FromHours(1));
        }

        private static int CountedWindowHours(
            string? timeIn, string? timeOut, TimeSpan windowStart, TimeSpan windowEnd)
        {
            if (!AttendanceDisplay.TryParseTime(timeIn, out var start)
                || !AttendanceDisplay.TryParseTime(timeOut, out var end))
                return 0;

            if (end < start)
                end += TimeSpan.FromDays(1);

            var paidMinutes = 60 - EarlyGrace.TotalMinutes;
            var hours = 0;
            for (var slot = windowStart; slot < windowEnd; slot += TimeSpan.FromHours(1))
            {
                var slotEnd = slot + TimeSpan.FromHours(1);
                var from = start > slot ? start : slot;
                var to = end < slotEnd ? end : slotEnd;
                if (to <= from)
                    continue;
                if ((to - from).TotalMinutes >= paidMinutes)
                    hours++;
            }

            return hours;
        }

        private static string FormatClock(TimeSpan value) =>
            DateTime.Today.Add(value).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static class AttendanceIssueCodes
    {
        public const string LingerAfterShift = "linger_after_shift";
        public const string IncompleteOvertime = "incomplete_overtime";
        public const string OvertimeBeforeFive = "overtime_before_five";
        public const string AfternoonOutConflictsWithOt = "afternoon_out_conflicts_with_ot";
        public const string SessionOutBeforeIn = "session_out_before_in";
        public const string OverlappingSessions = "overlapping_sessions";
    }

    public sealed class AttendanceIssue
    {
        public AttendanceIssue(string code, string message)
        {
            Code = code;
            Message = message;
        }

        public string Code { get; }
        public string Message { get; }
    }
}
