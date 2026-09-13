using FluentAssertions;
using ShopApp.Domain.Logic;
using Xunit;

namespace ShopApp.Tests;

public class ProfitCalculationTests
{
    private static List<ProfitLine> TwoLines() => new()
    {
        new(1, "Milk Powder", 500m, 1200m, 1000m),   // rev 600000, cost 500000
        new(2, "Till",        500m,  500m,  420m),   // rev 250000, cost 210000
    };

    [Fact]
    public void Calculates_revenue_cost_and_profit_per_line()
    {
        var p = ProfitCalculations.ForInvoice(TwoLines(), 0m);
        p[0].Revenue.Should().Be(600000m);
        p[0].Cost.Should().Be(500000m);
        p[0].Profit.Should().Be(100000m);
        p[1].Profit.Should().Be(40000m);
    }

    [Fact]
    public void Spreads_invoice_discount_across_lines_by_value()
    {
        // 850,000 gross with an 8,500 discount splits 6,000 / 2,500.
        var d = ProfitCalculations.ForInvoice(TwoLines(), 8500m);
        d[0].Revenue.Should().Be(594000m);
        d[1].Revenue.Should().Be(247500m);
    }

    [Fact]
    public void Discount_does_not_change_cost()
    {
        var d = ProfitCalculations.ForInvoice(TwoLines(), 8500m);
        d[0].Cost.Should().Be(500000m);
        d[1].Cost.Should().Be(210000m);
    }

    [Fact]
    public void Profit_falls_by_exactly_the_discount()
    {
        var before = ProfitCalculations.ForInvoice(TwoLines(), 0m).Sum(x => x.Profit);
        var after = ProfitCalculations.ForInvoice(TwoLines(), 8500m).Sum(x => x.Profit);
        (before - after).Should().Be(8500m);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(33)]
    [InlineData(99.99)]
    [InlineData(7.77)]
    public void Awkward_discounts_still_reconcile_exactly(decimal discount)
    {
        // Three equal lines force a rounding remainder. If it is not absorbed,
        // item-wise reports stop agreeing with the profit and loss statement.
        var lines = new List<ProfitLine>
        {
            new(1, "A", 3m, 100m, 60m),
            new(2, "B", 3m, 100m, 60m),
            new(3, "C", 3m, 100m, 60m),
        };
        ProfitCalculations.ForInvoice(lines, discount)
            .Sum(x => x.Revenue).Should().Be(Money.Round(900m - discount));
    }

    [Fact]
    public void Free_goods_show_a_loss_equal_to_their_cost()
    {
        var r = ProfitCalculations.ForInvoice(
            new List<ProfitLine> { new(1, "Sample", 5m, 0m, 50m) }, 0m);
        r[0].Profit.Should().Be(-250m);
    }

    [Fact]
    public void Groups_the_same_item_across_invoices()
    {
        var lines = new List<LineProfit>
        {
            new(1, "Milk Powder", 100m, 120000m, 100000m, 20000m),
            new(1, "Milk Powder", 200m, 240000m, 200000m, 40000m),
            new(2, "Till", 50m, 25000m, 21000m, 4000m),
        };
        var g = ProfitCalculations.GroupByItem(lines);
        g.Should().HaveCount(2);
        g.Single(x => x.ItemId == 1).Qty.Should().Be(300m);
        g.Single(x => x.ItemId == 1).Profit.Should().Be(60000m);
        g[0].ItemId.Should().Be(1);   // sorted by profit
    }

    [Fact]
    public void Profit_and_loss_does_not_subtract_purchases()
    {
        // Stock bought but not yet sold is an asset, not an expense.
        var pl = ProfitCalculations.ProfitAndLoss(951250m, 800000m, 50000m);
        pl.GrossProfit.Should().Be(151250m);
        pl.NetProfit.Should().Be(101250m);
    }

    [Fact]
    public void Reports_a_loss_as_negative()
        => ProfitCalculations.ProfitAndLoss(100000m, 90000m, 30000m)
            .NetProfit.Should().Be(-20000m);

    [Fact]
    public void Margin_on_zero_revenue_is_zero_not_an_error()
        => ProfitCalculations.MarginPercent(0m, 0m).Should().Be(0m);
}

public class ReportFilterTests
{
    [Fact]
    public void Range_includes_both_end_dates()
    {
        var f = new ReportFilter(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31));
        f.Covers(new DateTime(2026, 8, 1)).Should().BeTrue();
        f.Covers(new DateTime(2026, 8, 31)).Should().BeTrue();
        f.Covers(new DateTime(2026, 7, 31)).Should().BeFalse();
        f.Covers(new DateTime(2026, 9, 1)).Should().BeFalse();
    }

    [Fact]
    public void Range_text_is_day_month_year()
        => new ReportFilter(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31))
            .RangeText.Should().Be("01-08-2026 to 31-08-2026");
}
