namespace ShopApp.Domain.Logic;

/// <summary>
/// Invoice numbering. Decide the format before the first invoice prints -
/// changing it later means a business with two numbering schemes in its books.
/// </summary>
public static class InvoiceNumber
{
    /// <summary>Pakistan financial year runs July to June.</summary>
    public static string FinancialYear(DateTime date)
    {
        var startYear = date.Month >= 7 ? date.Year : date.Year - 1;
        return $"{startYear % 100:D2}{(startYear + 1) % 100:D2}";
    }

    /// <summary>
    /// His approved invoice prints a plain sequential number - "Invoice No.: 7".
    /// So the defaults are: no prefix, no padding, no yearly reset.
    ///
    /// Prefix, zero-padding and yearly reset are all still supported, because
    /// changing the printed format after the first bill leaves his books with
    /// two numbering schemes. Keeping the options costs nothing now and avoids
    /// a painful migration if he changes his mind.
    /// </summary>
    public static string Format(string? prefix, int number, int padding,
                                bool resetYearly, DateTime date)
    {
        var core = padding <= 1
            ? number.ToString()
            : number.ToString(new string('0', padding));

        return resetYearly
            ? $"{prefix}{FinancialYear(date)}-{core}"
            : $"{prefix}{core}";
    }

    /// <summary>Plain sequential, exactly as on his sample invoice.</summary>
    public static string Plain(int number) => number.ToString();
}
