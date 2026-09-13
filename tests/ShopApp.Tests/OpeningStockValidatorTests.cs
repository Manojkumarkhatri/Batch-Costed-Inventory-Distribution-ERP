using FluentAssertions;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using Xunit;

namespace ShopApp.Tests;

public class OpeningStockValidatorTests
{
    private static readonly DateTime Today = DateTime.Today;

    private static OpeningStockDraft Row(
        int n = 1, int itemId = 1, decimal qty = 100m, decimal cost = 200m,
        string? batch = "OPEN-1", DateTime? mfg = null, DateTime? exp = null,
        ItemCategory cat = ItemCategory.Solid, bool already = false)
        => new(n, itemId, "Milk Powder", batch, qty, cost, mfg, exp, cat, already);

    [Fact]
    public void Accepts_a_well_formed_row()
        => OpeningStockValidator.Validate(Today, new[] { Row() }).HasErrors.Should().BeFalse();

    [Fact]
    public void Requires_at_least_one_row()
        => OpeningStockValidator.Validate(Today, Array.Empty<OpeningStockDraft>())
            .HasErrors.Should().BeTrue();

    [Fact]
    public void Rejects_a_future_opening_date()
        => OpeningStockValidator.Validate(Today.AddDays(3), new[] { Row() })
            .HasErrors.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Rejects_non_positive_quantity(decimal qty)
        => OpeningStockValidator.Validate(Today, new[] { Row(qty: qty) })
            .HasErrors.Should().BeTrue();

    [Fact]
    public void Zero_cost_warns_about_false_profit_but_does_not_block()
    {
        // The most common opening-stock mistake: zero cost makes every future
        // sale of this batch look like 100% margin.
        var r = OpeningStockValidator.Validate(Today, new[] { Row(cost: 0m) });
        r.HasErrors.Should().BeFalse();
        r.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void Re_entering_opening_stock_warns_about_doubling()
    {
        var r = OpeningStockValidator.Validate(Today, new[] { Row(already: true) });
        r.HasErrors.Should().BeFalse();
        r.HasWarnings.Should().BeTrue();
    }

    [Theory]
    [InlineData(ItemCategory.Frozen)]
    [InlineData(ItemCategory.Liquid)]
    public void Perishables_without_expiry_warn(ItemCategory category)
    {
        var r = OpeningStockValidator.Validate(Today, new[] { Row(exp: null, cat: category) });
        r.HasErrors.Should().BeFalse();
        r.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void Dry_goods_without_expiry_are_silent()
    {
        var r = OpeningStockValidator.Validate(Today,
            new[] { Row(exp: null, cat: ItemCategory.Solid) });
        r.HasErrors.Should().BeFalse();
        r.HasWarnings.Should().BeFalse();
    }

    [Fact]
    public void Rejects_expiry_on_or_before_manufacture()
        => OpeningStockValidator.Validate(Today, new[] { Row(mfg: Today, exp: Today) })
            .HasErrors.Should().BeTrue();

    [Fact]
    public void Rejects_manufacture_after_the_opening_date()
        => OpeningStockValidator.Validate(Today, new[] { Row(mfg: Today.AddDays(2)) })
            .HasErrors.Should().BeTrue();

    [Theory]
    [InlineData("A", "A")]
    [InlineData("a", " A ")]
    public void Rejects_duplicate_batch_for_the_same_item(string a, string b)
        => OpeningStockValidator.Validate(Today, new[] { Row(1, batch: a), Row(2, batch: b) })
            .HasErrors.Should().BeTrue();

    [Fact]
    public void Allows_the_same_batch_label_on_different_items()
        => OpeningStockValidator.Validate(Today,
                new[] { Row(1, itemId: 1, batch: "A"), Row(2, itemId: 2, batch: "A") })
            .HasErrors.Should().BeFalse();

    [Fact]
    public void Names_the_offending_row_in_the_message()
    {
        var r = OpeningStockValidator.Validate(Today,
            new[] { Row(1), Row(2, qty: 0m) });
        r.ErrorText.Should().Contain("Row 2");
    }
}
