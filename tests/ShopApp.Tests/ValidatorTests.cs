using FluentAssertions;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using Xunit;

namespace ShopApp.Tests;

public class ItemValidatorTests
{
    private static Item Good() => new()
    {
        Name = "Basmati Rice", BaseUnit = "Kg", ConversionFactor = 1m,
        DefaultPurchasePrice = 200m, DefaultSalePrice = 240m,
        Category = ItemCategory.Solid, NearExpiryDays = 30
    };

    [Fact]
    public void Accepts_a_well_formed_item()
        => ItemValidator.Validate(Good(), false).HasErrors.Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("R")]
    public void Rejects_bad_names(string name)
    {
        var item = Good();
        item.Name = name;
        ItemValidator.Validate(item, false).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Rejects_duplicate_name()
        => ItemValidator.Validate(Good(), nameAlreadyExists: true).HasErrors.Should().BeTrue();

    [Fact]
    public void Rejects_pack_unit_with_zero_conversion()
    {
        var item = Good();
        item.AltUnit = "Sack";
        item.ConversionFactor = 0m;
        ItemValidator.Validate(item, false).HasErrors.Should().BeTrue();
    }

    [Theory]
    [InlineData("Kg")]
    [InlineData("kg")]
    [InlineData(" KG ")]
    public void Rejects_pack_unit_equal_to_base_unit(string alt)
    {
        var item = Good();
        item.AltUnit = alt;
        item.ConversionFactor = 50m;
        ItemValidator.Validate(item, false).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Rejects_variable_weight_without_a_pack_unit()
    {
        var item = Good();
        item.IsVariableWeight = true;
        item.AltUnit = null;
        ItemValidator.Validate(item, false).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Selling_below_cost_warns_but_does_not_block()
    {
        var item = Good();
        item.DefaultSalePrice = 150m;   // cost is 200
        var r = ItemValidator.Validate(item, false);
        r.HasErrors.Should().BeFalse();
        r.HasWarnings.Should().BeTrue();
    }

    [Theory]
    [InlineData(ItemCategory.Frozen)]
    [InlineData(ItemCategory.Liquid)]
    public void Perishable_category_without_expiry_tracking_warns(ItemCategory category)
    {
        var item = Good();
        item.Category = category;
        item.NearExpiryDays = 0;
        var r = ItemValidator.Validate(item, false);
        r.HasErrors.Should().BeFalse();
        r.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public void Collects_every_error_not_just_the_first()
    {
        var item = new Item { Name = "", BaseUnit = "", DefaultSalePrice = -1m };
        ItemValidator.Validate(item, false)
            .Issues.Count(i => !i.IsWarning).Should().BeGreaterThanOrEqualTo(3);
    }
}

public class PartyValidatorTests
{
    private static Party Good() => new()
    {
        Name = "Karachi Foods",
        Phone = "0300-1234567",
        Email = "info@karachifoods.pk",
        GroupName = "Wholesale",
        OpeningBalance = 45000m,
        OpeningIsReceivable = true,
        CreditLimit = 50000m
    };

    [Fact]
    public void Accepts_a_well_formed_party()
        => PartyValidator.Validate(Good(), false).HasErrors.Should().BeFalse();

    [Fact]
    public void Rejects_negative_credit_limit()
    {
        var p = Good();
        p.CreditLimit = -1m;
        PartyValidator.Validate(p, false).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Rejects_a_negative_opening_balance()
    {
        // Direction comes from the To Receive / To Pay radio, so the figure
        // itself must always be entered positive.
        var p = Good();
        p.OpeningBalance = -5000m;
        PartyValidator.Validate(p, false).HasErrors.Should().BeTrue();
    }

    [Fact]
    public void To_pay_is_stored_positive_and_signed_on_read()
    {
        var p = Good();
        p.OpeningBalance = 80000m;
        p.OpeningIsReceivable = false;

        PartyValidator.Validate(p, false).HasErrors.Should().BeFalse();
        p.SignedOpeningBalance.Should().Be(-80000m);
    }

    [Fact]
    public void To_receive_reads_back_positive()
    {
        var p = Good();
        p.SignedOpeningBalance.Should().Be(45000m);
    }

    [Theory]
    [InlineData("0300-1234567")]
    [InlineData("+92 300 1234567")]
    [InlineData("021 34567890")]
    [InlineData("(021) 3456-7890")]
    public void Accepts_common_pakistani_phone_formats(string phone)
        => PartyValidator.LooksLikePhone(phone).Should().BeTrue();

    [Theory]
    [InlineData("12345")]
    [InlineData("not a phone")]
    public void Rejects_nonsense_phone_numbers(string phone)
        => PartyValidator.LooksLikePhone(phone).Should().BeFalse();

    [Theory]
    [InlineData("info@shop.pk")]
    [InlineData("a.b@sub.domain.com")]
    public void Accepts_reasonable_emails(string email)
        => PartyValidator.LooksLikeEmail(email).Should().BeTrue();

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("two@@at.com")]
    [InlineData("nodot@domain")]
    [InlineData("has space@x.com")]
    public void Flags_malformed_emails(string email)
        => PartyValidator.LooksLikeEmail(email).Should().BeFalse();

    [Fact]
    public void Bad_email_only_warns_it_does_not_block_saving()
    {
        var p = Good();
        p.Email = "not-an-email";
        var r = PartyValidator.Validate(p, false);
        r.HasErrors.Should().BeFalse();
        r.HasWarnings.Should().BeTrue();
    }
}
