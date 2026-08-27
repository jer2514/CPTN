using RSDSystem.Helpers;
using RSDSystem.Models;
using Xunit;

namespace RSDSystem.Tests
{
    public class PayrollSubmitRulesTests
    {
        [Fact]
        public void BlockReason_stops_submit_when_an_attendance_correction_is_still_pending()
        {
            var reason = PayrollSubmitRules.BlockReason(PayrollStatusOptions.Draft, hasPendingCorrection: true);

            Assert.Equal(PayrollSubmitRules.PendingCorrectionMessage, reason);
        }

        [Fact]
        public void BlockReason_stops_resubmit_from_correction_when_attendance_is_still_pending()
        {
            var reason = PayrollSubmitRules.BlockReason(
                PayrollStatusOptions.Correction, hasPendingCorrection: true);

            Assert.Equal(PayrollSubmitRules.PendingCorrectionMessage, reason);
        }

        [Fact]
        public void BlockReason_allows_draft_submit_when_no_correction_is_pending()
        {
            Assert.Null(PayrollSubmitRules.BlockReason(PayrollStatusOptions.Draft, hasPendingCorrection: false));
        }

        [Fact]
        public void BlockReason_keeps_submitted_and_approved_payroll_immutable()
        {
            Assert.Equal(
                PayrollSubmitRules.AlreadyClosedMessage,
                PayrollSubmitRules.BlockReason(PayrollStatusOptions.Submitted, hasPendingCorrection: false));
            Assert.Equal(
                PayrollSubmitRules.AlreadyClosedMessage,
                PayrollSubmitRules.BlockReason(PayrollStatusOptions.Approved, hasPendingCorrection: true));
        }
    }
}
