using System.Globalization;

namespace ShopApp.Domain.Logic;

/// <summary>
/// Money formatting exactly as it appears on his sample invoice: "Rs 951,250".
/// Kept out of the PDF code so it can be tested without rendering anything.
/// </summary>
public static class InvoiceFormatting
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Money(decimal amount, bool showDecimals, bool groupDigits)
    {
        var pattern = (groupDigits, showDecimals) switch
        {
            (true, true) => "#,##0.00",
            (true, false) => "#,##0",
            (false, true) => "0.00",
            (false, false) => "0"
        };
        return amount.ToString(pattern, Inv);
    }

    /// <summary>With the "Rs " prefix, as printed.</summary>
    public static string Rs(decimal amount, bool showDecimals, bool groupDigits) =>
        "Rs " + Money(amount, showDecimals, groupDigits);

    /// <summary>
    /// Quantities print without trailing zeros: 500 not 500.000, but 12.5 stays 12.5.
    /// His sample shows plain whole numbers.
    /// </summary>
    public static string Qty(decimal qty)
    {
        var rounded = decimal.Round(qty, 3);
        return rounded == decimal.Truncate(rounded)
            ? decimal.Truncate(rounded).ToString("#,##0", Inv)
            : rounded.ToString("#,##0.###", Inv);
    }

    /// <summary>DD-MM-YYYY, as on his invoice.</summary>
    public static string Date(DateTime date) => date.ToString("dd-MM-yyyy", Inv);
}
