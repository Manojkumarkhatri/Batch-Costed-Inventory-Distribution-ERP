using FluentAssertions;
using ShopApp.Domain.Logic;
using Xunit;

namespace ShopApp.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("100.555", "100.56")]
    [InlineData("2.675", "2.68")]
    [InlineData("0.005", "0.01")]
    public void Rounds_away_from_zero_not_bankers(string input, string expected)
        => Money.Round(decimal.Parse(input)).Should().Be(decimal.Parse(expected));

    [Theory]
    [InlineData("1234.56")]
    [InlineData("0.01")]
    [InlineData("50000000.99")]
    public void Survives_paisa_round_trip(string value)
    {
        var d = decimal.Parse(value);
        Money.FromPaisa(Money.ToPaisa(d)).Should().Be(d);
    }

    [Fact]
    public void Decimal_has_no_floating_point_error()
        => (0.1m + 0.2m).Should().Be(0.3m);

    [Fact]
    public void Quantity_keeps_three_decimals_for_grams()
        => Money.FromMilli(Money.ToMilli(12.345m)).Should().Be(12.345m);
}
