using FluentAssertions;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using Xunit;

namespace ShopApp.Tests;

public class PurchaseValidatorTests
{
    private static PurchaseLineDraft Line(
        int n = 1, decimal qty = 100m, decimal rate = 200m,
        DateTime? mfg = null, DateTime? exp = null, string? batch = "B-1",
        ItemCategory cat = ItemCategory.Solid, bool reqExp = false, int itemId = 1)
        => new(n, itemId, "Rice", batch, mfg, exp, qty, rate, cat, reqExp);

    private static ValidationResult Validate(
        int supplierId = 1, DateTime? date = null, decimal discount = 0m,
        decimal charges = 0m, params PurchaseLineDraft[] lines)
        => PurchaseValidator.Validate(
            supplierId, date ?? DateTime.Today, discount, charges,
            lines.Length == 0 ? new[] { Line() } : lines);

    [Fact]
    public void Accepts_a_well_formed_purchase()
        => Validate().HasErrors.Should().BeFalse();

    [Fact]
    public void Requires_a_supplier()
        => Validate(supplierId: 0).HasErrors.Should().BeTrue();

    [Fact]
    public void Requires_at_least_one_line()
        => PurchaseValidator.Validate(1, DateTime.Today, 0m, 0m,
            Array.Empty<PurchaseLineDraft>()).HasErrors.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_non_positive_quantity(decimal qty)
        => Validate(lines: Line(qty: qty)).HasErrors.Should().BeTrue();

    [Fact]
    public void Rejects_discount_greater_than_total()
        => Validate(discount: 25000m).HasErrors.Should().BeTrue();   // total is 20000

    [Fact]
    public void Allows_discount_equal_to_total()
        => Validate(discount: 20000m).HasErrors.Should().BeFalse();

    [Fact]
    public void Rejects_expiry_on_or_before_manufacture()
    {
        var today = DateTime.Today;
        Validate(lines: Line(mfg: today, exp: today)).HasErrors.Should().BeTrue();
        Validate(lines: Line(mfg: today, exp: today.AddDays(-1))).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Rejects_future_manufacture_date()
        => Validate(lines: Line(mfg: DateTime.Today.AddDays(5))).HasErrors.Should().BeTrue();

    [Fact]
    public void Buying_expired_stock_warns_but_does_not_block()
    {
        var r = Validate(lines: Line(exp: DateTime.Today.AddDays(-10)));
        r.HasErrors.Should().BeFalse();
        r.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void Perishable_without_expiry_warns_but_does_not_block()
    {
        var r = Validate(lines: Line(exp: null, cat: ItemCategory.Frozen, reqExp: true));
        r.HasErrors.Should().BeFalse();
        r.HasWarnings.Should().BeTrue();
    }

    [Theory]
    [InlineData("B-1", "B-1")]
    [InlineData("b-1", " B-1 ")]
    public void Rejects_duplicate_batch_for_the_same_item(string a, string b)
        => Validate(lines: new[] { Line(1, batch: a), Line(2, batch: b) })
            .HasErrors.Should().BeTrue();

    [Fact]
    public void Allows_the_same_batch_number_on_different_items()
        => Validate(lines: new[] { Line(1, batch: "B-1", itemId: 1),
                                   Line(2, batch: "B-1", itemId: 2) })
            .HasErrors.Should().BeFalse();

    [Fact]
    public void Blank_batch_numbers_are_not_duplicates()
        => Validate(lines: new[] { Line(1, batch: null), Line(2, batch: null) })
            .HasErrors.Should().BeFalse();
}

public class BatchNumberingTests
{
    [Fact]
    public void Formats_as_yymmdd_sequence()
        => BatchNumbering.Suggest(new DateTime(2026, 8, 26), 1).Should().Be("260826-01");

    [Fact]
    public void Starts_at_one_when_nothing_exists()
        => BatchNumbering.NextFor(new DateTime(2026, 8, 26), Array.Empty<string>())
            .Should().Be("260826-01");

    [Fact]
    public void Increments_past_existing_batches()
        => BatchNumbering.NextFor(new DateTime(2026, 8, 26), new[] { "260826-01", "260826-02" })
            .Should().Be("260826-03");

    [Fact]
    public void Ignores_other_dates_and_supplier_formats()
        => BatchNumbering.NextFor(new DateTime(2026, 8, 26),
                new[] { "260825-09", "LOT-XYZ", "260826-01" })
            .Should().Be("260826-02");
}

public class LandedCostWithDiscountTests
{
    [Fact]
    public void Negative_net_charge_reduces_unit_cost_and_still_reconciles()
    {
        var lines = new List<CostLine> { new(0, 100m, 20000m), new(1, 50m, 5000m) };
        var r = LandedCost.Apportion(lines, -2500m);

        r[0].ApportionedCharge.Should().Be(-2000m);
        r[1].ApportionedCharge.Should().Be(-500m);
        r[0].UnitCost.Should().Be(180m);                      // (20000 - 2000) / 100
        r.Sum(x => x.ApportionedCharge).Should().Be(-2500m);
    }
}
