using RSDSystem.Helpers;
using Xunit;

namespace RSDSystem.Tests;

public class AttendanceRulesTests
{
    [Fact]
    public void StandardShift_HasEightRegularHoursAndNoOvertime()
    {
        var regular = AttendanceRules.RegularHours("08:00", "12:00", "13:00", "17:00");
        var overtime = AttendanceRules.OvertimeHours("08:00", "12:00", "13:00", "17:00", null, null);

        Assert.Equal(8, regular);
        Assert.Equal(0, overtime);
        Assert.Empty(AttendanceRules.DetectIssues("08:00", "12:00", "13:00", "17:00", null, null));
    }

    [Fact]
    public void LingerTimeoutAtSeven_IsNotPaidOvertime()
    {
        var regular = AttendanceRules.RegularHours("08:00", "12:00", "13:00", "19:00");
        var overtime = AttendanceRules.OvertimeHours("08:00", "12:00", "13:00", "19:00", null, null);
        var issues = AttendanceRules.DetectIssues("08:00", "12:00", "13:00", "19:00", null, null);

        Assert.Equal(8, regular);
        Assert.Equal(0, overtime);
        Assert.Contains(issues, i => i.Code == AttendanceIssueCodes.LingerAfterShift);
    }

    [Fact]
    public void ExplicitOvertimePunches_ArePaid()
    {
        var overtime = AttendanceRules.OvertimeHours(
            "08:00", "12:00", "13:00", "17:00", "17:00", "19:00");
        var issues = AttendanceRules.DetectIssues(
            "08:00", "12:00", "13:00", "17:00", "17:00", "19:00");

        Assert.Equal(2, overtime);
        Assert.Empty(issues);
    }

    [Fact]
    public void LingerPlusOvertimePunches_PaysOtAndFlagsConflict()
    {
        var overtime = AttendanceRules.OvertimeHours(
            "08:00", "12:00", "13:00", "19:00", "17:00", "19:00");
        var issues = AttendanceRules.DetectIssues(
            "08:00", "12:00", "13:00", "19:00", "17:00", "19:00");

        Assert.Equal(2, overtime);
        Assert.Contains(issues, i => i.Code == AttendanceIssueCodes.AfternoonOutConflictsWithOt);
        Assert.DoesNotContain(issues, i => i.Code == AttendanceIssueCodes.LingerAfterShift);
    }

    [Fact]
    public void IncompleteOvertime_IsNotPaid()
    {
        var overtime = AttendanceRules.OvertimeHours(
            "08:00", "12:00", "13:00", "17:00", "17:00", null);
        var issues = AttendanceRules.DetectIssues(
            "08:00", "12:00", "13:00", "17:00", "17:00", null);

        Assert.Equal(0, overtime);
        Assert.Contains(issues, i => i.Code == AttendanceIssueCodes.IncompleteOvertime);
    }

    [Fact]
    public void OvertimeInBeforeFive_IsFlagged()
    {
        var issues = AttendanceRules.DetectIssues(
            "08:00", "12:00", "13:00", "17:00", "16:00", "19:00");

        Assert.Contains(issues, i => i.Code == AttendanceIssueCodes.OvertimeBeforeFive);
        Assert.Equal(2, AttendanceRules.OvertimeHours(
            "08:00", "12:00", "13:00", "17:00", "16:00", "19:00"));
    }

    [Fact]
    public void SessionOutBeforeIn_IsFlagged()
    {
        var issues = AttendanceRules.DetectIssues("10:00", "08:00", "13:00", "17:00", null, null);

        Assert.Contains(issues, i => i.Code == AttendanceIssueCodes.SessionOutBeforeIn);
    }

    [Fact]
    public void OverlappingSessions_AreFlagged()
    {
        var issues = AttendanceRules.DetectIssues("08:00", "14:00", "13:00", "17:00", null, null);

        Assert.Contains(issues, i => i.Code == AttendanceIssueCodes.OverlappingSessions);
    }
}
