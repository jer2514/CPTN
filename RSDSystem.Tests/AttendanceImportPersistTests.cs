using RSDSystem.Helpers;
using RSDSystem.Models;
using RSDSystem.Services;
using Xunit;

namespace RSDSystem.Tests;

public class AttendanceImportPersistTests
{
    [Fact]
    public void ExpandToDateRange_MarksScheduleFilledDaysAndKeepsFilePunches()
    {
        var fileDay = new DateTime(2026, 8, 16);
        var parsed = new AttendanceParseResult
        {
            PeriodStart = fileDay,
            PeriodEnd = fileDay,
            Rows =
            {
                new AttendanceRecord
                {
                    ExternalUserId = "1",
                    EmployeeName = "Jun",
                    WorkDate = fileDay,
                    TimeIn1 = "08:00",
                    TimeOut1 = "12:00",
                    TimeIn2 = "13:00",
                    TimeOut2 = "17:00"
                }
            }
        };

        AttendanceFileParser.ExpandToDateRange(parsed, fileDay, new DateTime(2026, 8, 23));

        Assert.Equal(8, parsed.Rows.Count);
        var punched = Assert.Single(parsed.Rows, r => r.WorkDate == fileDay);
        Assert.False(punched.FilledFromTaskWindow);
        Assert.Equal("08:00", punched.TimeIn1);

        Assert.All(
            parsed.Rows.Where(r => r.WorkDate != fileDay),
            row =>
            {
                Assert.True(row.FilledFromTaskWindow);
                Assert.Equal(AttendanceStatuses.Absent, row.Status);
                Assert.True(string.IsNullOrWhiteSpace(row.TimeIn1));
            });
    }

    [Fact]
    public void ScheduleFilledAbsentRows_AreNotPersisted()
    {
        var fileRow = new AttendancePreviewRow
        {
            WorkDate = new DateTime(2026, 8, 16),
            TimeIn1 = "08:00",
            TimeOut1 = "12:00",
            FilledFromTaskWindow = false
        };
        var filledAbsent = new AttendancePreviewRow
        {
            WorkDate = new DateTime(2026, 8, 17),
            Status = AttendanceStatuses.Absent,
            FilledFromTaskWindow = true
        };

        Assert.True(fileRow.ShouldPersist());
        Assert.False(filledAbsent.ShouldPersist());
    }

    [Fact]
    public void ScheduleFilledRow_IsPersistedAfterStaffEntersPunches()
    {
        var edited = new AttendancePreviewRow
        {
            WorkDate = new DateTime(2026, 8, 17),
            TimeIn2 = "13:00",
            TimeOut2 = "17:00",
            FilledFromTaskWindow = true
        };

        Assert.True(edited.ShouldPersist());
    }

    [Fact]
    public void ImportPeriod_UsesPersistedFileDatesNotTheTaskWindow()
    {
        var preview = new AttendancePreviewResult
        {
            PeriodStart = new DateTime(2026, 8, 16),
            PeriodEnd = new DateTime(2026, 8, 23),
            FilePeriodStart = new DateTime(2026, 8, 16),
            FilePeriodEnd = new DateTime(2026, 8, 16),
            Rows =
            {
                new AttendancePreviewRow
                {
                    WorkDate = new DateTime(2026, 8, 16),
                    TimeIn1 = "08:00",
                    FilledFromTaskWindow = false
                },
                new AttendancePreviewRow
                {
                    WorkDate = new DateTime(2026, 8, 17),
                    Status = AttendanceStatuses.Absent,
                    FilledFromTaskWindow = true
                }
            }
        };

        preview.Rows = preview.Rows.Where(r => r.ShouldPersist()).ToList();
        AttendanceImportService.RestrictImportPeriodToPersistedRows(preview);

        Assert.Single(preview.Rows);
        Assert.Equal(new DateTime(2026, 8, 16), preview.PeriodStart);
        Assert.Equal(new DateTime(2026, 8, 16), preview.PeriodEnd);
    }

    [Fact]
    public void FileCoverage_KeepsHeaderDatesWhenRowsAreASubset()
    {
        var parsed = new AttendanceParseResult
        {
            PeriodStart = new DateTime(2026, 8, 16),
            PeriodEnd = new DateTime(2026, 8, 31),
            Rows =
            {
                new AttendanceRecord { WorkDate = new DateTime(2026, 8, 16) },
                new AttendanceRecord { WorkDate = new DateTime(2026, 8, 17) }
            }
        };

        var (start, end) = AttendanceImportService.FileCoverage(parsed);

        Assert.Equal(new DateTime(2026, 8, 16), start);
        Assert.Equal(new DateTime(2026, 8, 31), end);
    }
}
