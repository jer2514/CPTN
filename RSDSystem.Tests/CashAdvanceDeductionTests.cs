using RSDSystem.Helpers;
using Xunit;

namespace RSDSystem.Tests;

public class CashAdvanceDeductionTests
{
    [Fact]
    public void Split_when_one_pending_row_is_larger_than_payroll_cash_advance()
    {
        var steps = CashAdvanceDeduction.Plan(new[] { (1, 10000m) }, 8000m);

        var step = Assert.Single(steps);
        Assert.Equal(1, step.CashAdvanceId);
        Assert.Equal(8000m, step.DeductedAmount);
        Assert.Equal(2000m, step.LeftoverAmount);
        Assert.Equal(8000m, steps.Sum(s => s.DeductedAmount));
    }

    [Fact]
    public void Does_not_skip_a_larger_row_to_deduct_a_later_smaller_one()
    {
        var steps = CashAdvanceDeduction.Plan(new[] { (1, 4000m), (2, 2000m) }, 3000m);

        var step = Assert.Single(steps);
        Assert.Equal(1, step.CashAdvanceId);
        Assert.Equal(3000m, step.DeductedAmount);
        Assert.Equal(1000m, step.LeftoverAmount);
        Assert.DoesNotContain(steps, s => s.CashAdvanceId == 2);
    }

    [Fact]
    public void Deducts_whole_rows_then_splits_the_next()
    {
        var steps = CashAdvanceDeduction.Plan(new[] { (1, 2000m), (2, 5000m) }, 3000m);

        Assert.Equal(2, steps.Count);
        Assert.Equal(new CashAdvanceDeduction.Step(1, 2000m, 0m), steps[0]);
        Assert.Equal(new CashAdvanceDeduction.Step(2, 1000m, 4000m), steps[1]);
        Assert.Equal(3000m, steps.Sum(s => s.DeductedAmount));
    }

    [Fact]
    public void Deducts_every_row_when_payroll_covers_the_pending_total()
    {
        var steps = CashAdvanceDeduction.Plan(new[] { (1, 1500m), (2, 500m) }, 2000m);

        Assert.Equal(2, steps.Count);
        Assert.All(steps, s => Assert.Equal(0m, s.LeftoverAmount));
        Assert.Equal(2000m, steps.Sum(s => s.DeductedAmount));
    }

    [Fact]
    public void Deducts_nothing_when_payroll_cash_advance_is_zero()
    {
        var steps = CashAdvanceDeduction.Plan(new[] { (1, 1000m) }, 0m);

        Assert.Empty(steps);
    }
}
