using RSDSystem.Helpers;
using RSDSystem.Models;
using Xunit;

namespace RSDSystem.Tests
{
    public class AttendanceCorrectionRulesTests
    {
        [Fact]
        public void PendingOnLiveRecords_ignores_corrections_whose_attendance_row_was_replaced()
        {
            var live = new AttendanceRecord { AttendanceRecordId = 20 };
            var orphan = new AttendanceCorrectionRequest
            {
                AttendanceCorrectionRequestId = 1,
                AttendanceRecordId = 10,
                ProjectId = 5,
                EmployeeId = 3,
                Status = CorrectionRequestStatuses.Pending
            };
            var current = new AttendanceCorrectionRequest
            {
                AttendanceCorrectionRequestId = 2,
                AttendanceRecordId = 20,
                ProjectId = 5,
                EmployeeId = 3,
                Status = CorrectionRequestStatuses.Pending
            };

            var pending = AttendanceCorrectionRules.PendingOnLiveRecords(
                    new[] { orphan, current }.AsQueryable(),
                    new[] { live }.AsQueryable())
                .ToList();

            Assert.Single(pending);
            Assert.Equal(2, pending[0].AttendanceCorrectionRequestId);
        }

        [Fact]
        public void PendingOnLiveRecords_does_not_treat_returned_or_approved_requests_as_blocking()
        {
            var live = new AttendanceRecord { AttendanceRecordId = 20 };
            var returned = new AttendanceCorrectionRequest
            {
                AttendanceRecordId = 20,
                Status = CorrectionRequestStatuses.Returned
            };
            var approved = new AttendanceCorrectionRequest
            {
                AttendanceRecordId = 20,
                Status = CorrectionRequestStatuses.Approved
            };

            var pending = AttendanceCorrectionRules.PendingOnLiveRecords(
                    new[] { returned, approved }.AsQueryable(),
                    new[] { live }.AsQueryable())
                .ToList();

            Assert.Empty(pending);
        }

        [Fact]
        public void IsOpen_covers_pending_and_returned_so_reimport_can_close_them()
        {
            Assert.True(AttendanceCorrectionRules.IsOpen(CorrectionRequestStatuses.Pending));
            Assert.True(AttendanceCorrectionRules.IsOpen(CorrectionRequestStatuses.Returned));
            Assert.False(AttendanceCorrectionRules.IsOpen(CorrectionRequestStatuses.Approved));
        }
    }
}
