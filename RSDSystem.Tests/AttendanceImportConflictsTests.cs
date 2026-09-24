using RSDSystem.Helpers;
using Xunit;

namespace RSDSystem.Tests;

public class AttendanceImportConflictsTests
{
    [Fact]
    public void BelongsToProject_MatchesRecordProjectId()
    {
        Assert.True(AttendanceImportConflicts.BelongsToProject(4, 9, 4));
        Assert.False(AttendanceImportConflicts.BelongsToProject(4, 9, 5));
    }

    [Fact]
    public void BelongsToProject_MatchesImportProjectWhenRecordIdDiffers()
    {
        Assert.True(AttendanceImportConflicts.BelongsToProject(0, 5, 5));
        Assert.False(AttendanceImportConflicts.BelongsToProject(3, 5, 8));
    }

    [Fact]
    public void BelongsToProject_TreatsUnsetLegacyRowsAsReplaceable()
    {
        Assert.True(AttendanceImportConflicts.BelongsToProject(0, null, 2));
        Assert.False(AttendanceImportConflicts.BelongsToProject(7, null, 2));
    }

    [Fact]
    public void IncomingConflicts_SkipsDaysAlreadyOwnedByAnotherProject()
    {
        var keys = new HashSet<string>
        {
            AttendanceImportConflicts.EmployeeDateKey(12, new DateTime(2026, 1, 15))
        };

        Assert.True(AttendanceImportConflicts.IncomingConflictsWithOtherProject(
            12, new DateTime(2026, 1, 15), keys));
        Assert.False(AttendanceImportConflicts.IncomingConflictsWithOtherProject(
            12, new DateTime(2026, 1, 16), keys));
        Assert.False(AttendanceImportConflicts.IncomingConflictsWithOtherProject(
            13, new DateTime(2026, 1, 15), keys));
        Assert.False(AttendanceImportConflicts.IncomingConflictsWithOtherProject(
            null, new DateTime(2026, 1, 15), keys));
    }

    [Fact]
    public void EmployeeDateKey_UsesDateOnlyAndCollapsesUnusableDates()
    {
        var morning = AttendanceImportConflicts.EmployeeDateKey(
            3, new DateTime(2026, 2, 1, 9, 30, 0));
        var midnight = AttendanceImportConflicts.EmployeeDateKey(
            3, new DateTime(2026, 2, 1));
        var invalid = AttendanceImportConflicts.EmployeeDateKey(
            3, new DateTime(1, 1, 1));

        Assert.Equal(midnight, morning);
        Assert.Equal("3:2026-02-01", midnight);
        Assert.Equal("3:none", invalid);
    }
}
