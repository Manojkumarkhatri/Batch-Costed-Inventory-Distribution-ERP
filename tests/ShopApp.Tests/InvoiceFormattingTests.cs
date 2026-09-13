using FluentAssertions;
using ShopApp.Domain.Logic;
using Xunit;

namespace ShopApp.Tests;

/// <summary>Locked against the invoice he approved. If these fail, the printed bill changed.</summary>
public class InvoiceFormattingTests
{
    [Theory]
    [InlineData(951250, "Rs 951,250")]
    [InlineData(600000, "Rs 600,000")]
    [InlineData(1200, "Rs 1,200")]
    [InlineData(500, "Rs 500")]
    [InlineData(0, "Rs 0")]
    [InlineData(3329650, "Rs 3,329,650")]
    public void Money_matches_his_printed_invoice(decimal amount, string expected)
        => InvoiceFormatting.Rs(amount, showDecimals: false, groupDigits: true)
            .Should().Be(expected);

    [Fact]
    public void Decimal_toggle_adds_paisa()
        => InvoiceFormatting.Rs(951250m, true, true).Should().Be("Rs 951,250.00");

    [Fact]
    public void Grouping_toggle_removes_separators()
        => InvoiceFormatting.Rs(951250m, false, false).Should().Be("Rs 951250");

    [Theory]
    [InlineData(500, "500")]
    [InlineData(10, "10")]
    [InlineData(12.5, "12.5")]
    [InlineData(1500, "1,500")]
    public void Quantities_drop_trailing_zeros(decimal qty, string expected)
        => InvoiceFormatting.Qty(qty).Should().Be(expected);

    [Fact]
    public void Date_is_day_month_year()
        => InvoiceFormatting.Date(new DateTime(2026, 8, 27)).Should().Be("27-08-2026");
}

public class NumberToWordsTests
{
    [Fact]
    public void Matches_the_words_line_on_his_invoice()
        => NumberToWords.RupeesInWords(951250m)
            .Should().Be("Nine Hundred Fifty One Thousand Two Hundred Fifty Rupees only");

    [Theory]
    [InlineData(0, "Zero Rupees only")]
    [InlineData(15, "Fifteen Rupees only")]
    [InlineData(21, "Twenty One Rupees only")]
    [InlineData(100, "One Hundred Rupees only")]
    [InlineData(1000, "One Thousand Rupees only")]
    [InlineData(1000000, "One Million Rupees only")]
    public void Converts_boundary_values(decimal amount, string expected)
        => NumberToWords.RupeesInWords(amount).Should().Be(expected);

    [Fact]
    public void Includes_paisa_when_present()
        => NumberToWords.RupeesInWords(100.50m)
            .Should().Be("One Hundred Rupees and Fifty Paisa only");

    [Fact]
    public void Rounds_paisa_up_into_rupees()
        => NumberToWords.RupeesInWords(99.999m).Should().Be("One Hundred Rupees only");

    [Fact]
    public void Indian_format_uses_lakh()
        => NumberToWords.RupeesInWords(951250m, indianFormat: true)
            .Should().Be("Nine Lakh Fifty One Thousand Two Hundred Fifty Rupees only");

    [Fact]
    public void Never_produces_double_spaces()
    {
        for (int i = 0; i <= 2000; i++)
            NumberToWords.RupeesInWords(i).Should().NotContain("  ");
    }
}
