using RSDSystem.Models;

namespace RSDSystem.Helpers
{
    public static class AttendanceCorrectionRules
    {
        public const string ReplacedByImportReason =
            "Attendance was imported again, so this correction no longer applies.";

        public static bool IsOpen(string? status) =>
            status == CorrectionRequestStatuses.Pending
            || status == CorrectionRequestStatuses.Returned;

        public static IQueryable<AttendanceCorrectionRequest> PendingOnLiveRecords(
            IQueryable<AttendanceCorrectionRequest> requests,
            IQueryable<AttendanceRecord> records) =>
            requests.Where(c =>
                c.Status == CorrectionRequestStatuses.Pending
                && records.Any(r => r.AttendanceRecordId == c.AttendanceRecordId));
    }
}
