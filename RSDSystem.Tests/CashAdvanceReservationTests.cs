using RSDSystem.Helpers;
using RSDSystem.Models;
using Xunit;

namespace RSDSystem.Tests;

public class CashAdvanceReservationTests
{
    [Fact]
    public void Uses_full_pending_when_no_other_payroll_has_reserved_it()
    {
        var available = CashAdvanceReservation.AvailableForPayroll(2000m, Array.Empty<(string, decimal)>());

        Assert.Equal(2000m, available);
    }

    [Fact]
    public void Holds_back_pending_already_on_another_draft_slip()
    {
        var available = CashAdvanceReservation.AvailableForPayroll(
            2000m,
            new[] { (PayrollStatusOptions.Draft, 2000m) });

        Assert.Equal(0m, available);
    }

    [Fact]
    public void Holds_back_pending_on_submitted_or_correction_slips()
    {
        var submitted = CashAdvanceReservation.AvailableForPayroll(
            5000m,
            new[] { (PayrollStatusOptions.Submitted, 2000m) });
        var correction = CashAdvanceReservation.AvailableForPayroll(
            5000m,
            new[] { (PayrollStatusOptions.Correction, 3000m) });

        Assert.Equal(3000m, submitted);
        Assert.Equal(2000m, correction);
    }

    [Fact]
    public void Ignores_approved_slips_because_pending_rows_are_consumed_on_approve()
    {
        var available = CashAdvanceReservation.AvailableForPayroll(
            2000m,
            new[] { (PayrollStatusOptions.Approved, 2000m) });

        Assert.Equal(2000m, available);
    }

    [Fact]
    public void Leaves_remainder_for_the_next_period_when_the_first_slip_could_not_cover_all()
    {
        var available = CashAdvanceReservation.AvailableForPayroll(
            5000m,
            new[] { (PayrollStatusOptions.Draft, 3000m) });

        Assert.Equal(2000m, available);
    }

    [Fact]
    public void Returns_zero_when_nothing_is_pending()
    {
        var available = CashAdvanceReservation.AvailableForPayroll(
            0m,
            new[] { (PayrollStatusOptions.Draft, 1000m) });

        Assert.Equal(0m, available);
    }
}
