using FluentAssertions;
using ShopApp.Domain.Logic;
using Xunit;

namespace ShopApp.Tests;

public class LandedCostTests
{
    [Fact]
    public void Apportions_charges_by_line_value()
    {
        var lines = new List<CostLine> { new(0, 100m, 20000m), new(1, 50m, 5000m) };
        var r = LandedCost.Apportion(lines, 5000m);

        r[0].ApportionedCharge.Should().Be(4000m);
        r[1].ApportionedCharge.Should().Be(1000m);
        r[0].UnitCost.Should().Be(240m);   // (20000 + 4000) / 100
    }

    [Fact]
    public void Total_always_reconciles_despite_rounding()
    {
        var lines = new List<CostLine> { new(0, 3m, 100m), new(1, 3m, 100m), new(2, 3m, 100m) };
        var r = LandedCost.Apportion(lines, 10m);
        r.Sum(x => x.ApportionedCharge).Should().Be(10m);
    }

    [Fact]
    public void Falls_back_to_quantity_when_lines_have_no_value()
    {
        var lines = new List<CostLine> { new(0, 10m, 0m), new(1, 30m, 0m) };
        var r = LandedCost.Apportion(lines, 400m);
        r[0].ApportionedCharge.Should().Be(100m);
        r[1].ApportionedCharge.Should().Be(300m);
    }
}
