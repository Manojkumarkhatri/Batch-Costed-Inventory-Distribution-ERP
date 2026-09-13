using FluentAssertions;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using Xunit;

namespace ShopApp.Tests;

public class ItemCsvParserTests
{
    [Fact]
    public void Parses_a_basic_file()
    {
        var r = ItemCsvParser.Parse(
            "Name,Category,BaseUnit,AltUnit,ConversionFactor\n" +
            "Yeast,Solid,KG,Carton,10\n");

        r.Errors.Should().BeEmpty();
        r.Rows.Should().ContainSingle();
        r.Rows[0].Name.Should().Be("Yeast");
        r.Rows[0].BaseUnit.Should().Be("KG");
        r.Rows[0].AltUnit.Should().Be("Carton");
        r.Rows[0].ConversionFactor.Should().Be(10m);
    }

    [Theory]
    [InlineData("Name,BaseUnit")]
    [InlineData("Items,Primary Unit")]
    [InlineData("ItemName,Unit")]
    public void Accepts_alternative_column_names(string header)
        => ItemCsvParser.Parse(header + "\nRice,KG\n").Rows.Should().ContainSingle();

    [Fact]
    public void Rejects_pack_unit_without_a_conversion_factor()
    {
        // This is the bug that bit us in the UI: a pack unit with no factor
        // must never be silently imported as 1.
        var r = ItemCsvParser.Parse("Name,BaseUnit,AltUnit\nRice,KG,Bag\n");
        r.Rows.Should().BeEmpty();
        r.Errors.Should().ContainSingle(e => e.Contains("conversion"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    public void Rejects_bad_conversion_factors(string factor)
        => ItemCsvParser.Parse($"Name,BaseUnit,AltUnit,ConversionFactor\nRice,KG,Bag,{factor}\n")
            .Rows.Should().BeEmpty();

    [Fact]
    public void Rejects_pack_unit_equal_to_base_unit()
        => ItemCsvParser.Parse("Name,BaseUnit,AltUnit,ConversionFactor\nRice,KG,KG,10\n")
            .Rows.Should().BeEmpty();

    [Fact]
    public void Flags_duplicate_names_within_the_file()
        => ItemCsvParser.Parse("Name,BaseUnit\nRice,KG\nrice,KG\n")
            .Errors.Should().ContainSingle(e => e.Contains("more than once"));

    [Fact]
    public void Handles_excel_byte_order_mark()
    {
        var r = ItemCsvParser.Parse("\uFEFFName,BaseUnit\nRice,KG\n");
        r.Errors.Should().BeEmpty();
        r.Rows.Should().ContainSingle();
    }

    [Fact]
    public void Handles_quoted_fields_containing_commas()
        => ItemCsvParser.Parse("Name,BaseUnit\n\"Rice, Basmati\",KG\n")
            .Rows[0].Name.Should().Be("Rice, Basmati");

    [Fact]
    public void Handles_thousands_separators_in_prices()
        => ItemCsvParser.Parse("Name,BaseUnit,PurchasePrice\nRice,KG,\"1,250.50\"\n")
            .Rows[0].PurchasePrice.Should().Be(1250.50m);

    [Fact]
    public void Skips_blank_lines()
        => ItemCsvParser.Parse("Name,BaseUnit\nRice,KG\n\n\nOil,Liter\n")
            .Rows.Should().HaveCount(2);

    [Theory]
    [InlineData("liquid", ItemCategory.Liquid)]
    [InlineData("Frozen", ItemCategory.Frozen)]
    [InlineData("dry", ItemCategory.Solid)]
    [InlineData("", ItemCategory.Solid)]
    public void Maps_category_words(string text, ItemCategory expected)
        => ItemCsvParser.ParseCategory(text).Should().Be(expected);

    [Fact]
    public void Reports_an_error_when_the_name_column_is_missing()
        => ItemCsvParser.Parse("Foo,Bar\n1,2\n").Errors
            .Should().Contain(e => e.Contains("Name"));

    [Fact]
    public void Real_product_conversions_survive_a_round_trip()
    {
        var r = ItemCsvParser.Parse(
            "Name,Category,BaseUnit,AltUnit,ConversionFactor\n" +
            "Taper,Solid,Piece,Carton,192\n" +
            "Chocolate Syrup,Liquid,Liter,Cane,32\n");

        var taper = r.Rows.Single(x => x.Name == "Taper");
        UnitConverter.PackToBase(3m, taper.ConversionFactor).Should().Be(576m);

        var syrup = r.Rows.Single(x => x.Name == "Chocolate Syrup");
        UnitConverter.PackToBase(0.5m, syrup.ConversionFactor).Should().Be(16m);
    }
}
