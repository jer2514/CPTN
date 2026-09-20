using RSDSystem.Helpers;
using Xunit;

namespace RSDSystem.Tests;

public class PayrollCashAdvanceTests
{
    [Fact]
    public void CappedCashAdvance_uses_the_full_pending_amount_when_gross_covers_it()
    {
        Assert.Equal(1500m, PayrollComputation.CappedCashAdvance(1500m, 8000m));
    }

    [Fact]
    public void CappedCashAdvance_never_exceeds_gross_pay()
    {
        Assert.Equal(2000m, PayrollComputation.CappedCashAdvance(5000m, 2000m));
    }

    [Fact]
    public void CappedCashAdvance_is_zero_when_nothing_is_pending_or_gross_is_zero()
    {
        Assert.Equal(0m, PayrollComputation.CappedCashAdvance(0m, 8000m));
        Assert.Equal(0m, PayrollComputation.CappedCashAdvance(1500m, 0m));
        Assert.Equal(0m, PayrollComputation.CappedCashAdvance(-10m, 8000m));
    }

    [Fact]
    public void Compute_reduces_net_pay_by_the_snapshotted_cash_advance()
    {
        var withAdvance = PayrollComputation.Compute(100m, 8m, 0m, 200m);
        var withoutAdvance = PayrollComputation.Compute(100m, 8m, 0m, 0m);

        Assert.Equal(800m, withAdvance.GrossPay);
        Assert.Equal(600m, withAdvance.NetPay);
        Assert.Equal(800m, withoutAdvance.NetPay);
    }
}
