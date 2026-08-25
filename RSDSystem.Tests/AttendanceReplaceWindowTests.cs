using RSDSystem.Helpers;
using RSDSystem.Models;
using Xunit;

namespace RSDSystem.Tests;

public class AttendanceReplaceWindowTests
{
    [Fact]
    public void ReimportOfLaterPeriod_KeepsEarlierUnlockedDays()
    {
        var julyTenth = new AttendanceRecord { EmployeeId = 1, WorkDate = new DateTime(2026, 7, 10) };
        var augustFifth = new AttendanceRecord { EmployeeId = 1, WorkDate = new DateTime(2026, 8, 5) };
        var closed = Array.Empty<ClosedPayrollWindow>();

        var replaceable = AttendanceReplaceWindow.UnlockedInWindow(
            [julyTenth, augustFifth],
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 15),
            closed);

        Assert.Single(replaceable);
        Assert.Equal(augustFifth, replaceable[0]);
    }

    [Fact]
    public void ApprovedPayrollDaysStayLockedInsideTheWindow()
    {
        var augustFifth = new AttendanceRecord { EmployeeId = 1, WorkDate = new DateTime(2026, 8, 5) };
        var closed = new[]
        {
            new ClosedPayrollWindow
            {
                EmployeeId = 1,
                Start = new DateTime(2026, 8, 1),
                End = new DateTime(2026, 8, 15)
            }
        };

        var replaceable = AttendanceReplaceWindow.UnlockedInWindow(
            [augustFifth],
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 15),
            closed);

        Assert.Empty(replaceable);
    }

    [Fact]
    public void DummyDatesAreReplacedWithTheOverlappingImport()
    {
        Assert.True(AttendanceReplaceWindow.Contains(new DateTime(1899, 1, 1), new DateTime(2026, 8, 1), new DateTime(2026, 8, 15)));
        Assert.False(AttendanceReplaceWindow.Contains(new DateTime(2026, 7, 31), new DateTime(2026, 8, 1), new DateTime(2026, 8, 15)));
        Assert.True(AttendanceReplaceWindow.Contains(new DateTime(2026, 8, 1), new DateTime(2026, 8, 1), new DateTime(2026, 8, 15)));
        Assert.True(AttendanceReplaceWindow.Contains(new DateTime(2026, 8, 15), new DateTime(2026, 8, 1), new DateTime(2026, 8, 15)));
    }
}
