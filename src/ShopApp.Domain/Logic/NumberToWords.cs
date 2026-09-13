namespace ShopApp.Domain.Logic;

/// <summary>
/// "Invoice Amount In Words" line on the printed bill.
///
/// His Vyapar setting is the English/international format (thousand, million),
/// not the South Asian lakh/crore grouping. His sample invoice reads:
///   951,250 -> "Nine Hundred Fifty One Thousand Two Hundred Fifty Rupees only"
/// Note there is no "and" between the parts, which matches that wording.
///
/// Indian format is included because he may prefer it later - it is one
/// setting to flip, not a rewrite.
/// </summary>
public static class NumberToWords
{
    private static readonly string[] Ones =
    {
        "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
        "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen",
        "Seventeen", "Eighteen", "Nineteen"
    };

    private static readonly string[] Tens =
    {
        "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety"
    };

    /// <summary>Full invoice line, e.g. "Nine Hundred Fifty One Thousand ... Rupees only".</summary>
    public static string RupeesInWords(decimal amount, bool indianFormat = false)
    {
        if (amount < 0) return "Minus " + RupeesInWords(Math.Abs(amount), indianFormat);

        var rupees = (long)decimal.Truncate(amount);
        var paisa = (int)decimal.Round((amount - rupees) * 100, 0, MidpointRounding.AwayFromZero);

        // Rounding 99.999 up to 100 paisa must carry into rupees.
        if (paisa == 100) { rupees += 1; paisa = 0; }

        if (rupees == 0 && paisa == 0) return "Zero Rupees only";

        var text = rupees == 0
            ? ""
            : (indianFormat ? IndianWords(rupees) : InternationalWords(rupees)) + " Rupees";

        if (paisa > 0)
        {
            var paisaWords = InternationalWords(paisa) + " Paisa";
            text = string.IsNullOrEmpty(text) ? paisaWords : $"{text} and {paisaWords}";
        }

        return text + " only";
    }

    /// <summary>Thousand / Million / Billion grouping.</summary>
    public static string InternationalWords(long n)
    {
        if (n == 0) return "Zero";
        if (n < 0) return "Minus " + InternationalWords(-n);

        var parts = new List<string>();

        void Take(long divisor, string label)
        {
            if (n < divisor) return;
            var count = n / divisor;
            n %= divisor;
            parts.Add($"{UnderThousand(count)} {label}");
        }

        Take(1_000_000_000_000L, "Trillion");
        Take(1_000_000_000L, "Billion");
        Take(1_000_000L, "Million");
        Take(1_000L, "Thousand");

        if (n > 0) parts.Add(UnderThousand(n));

        return string.Join(" ", parts).Trim();
    }

    /// <summary>Lakh / Crore grouping, for South Asian conventions.</summary>
    public static string IndianWords(long n)
    {
        if (n == 0) return "Zero";
        if (n < 0) return "Minus " + IndianWords(-n);

        var parts = new List<string>();

        void Take(long divisor, string label)
        {
            if (n < divisor) return;
            var count = n / divisor;
            n %= divisor;
            parts.Add($"{(count >= 100 ? UnderThousand(count) : UnderHundred(count))} {label}");
        }

        Take(10_000_000L, "Crore");
        Take(100_000L, "Lakh");
        Take(1_000L, "Thousand");

        if (n > 0) parts.Add(UnderThousand(n));

        return string.Join(" ", parts).Trim();
    }

    private static string UnderThousand(long n)
    {
        if (n >= 1000) return InternationalWords(n);   // safety net

        var words = new List<string>();

        if (n >= 100)
        {
            words.Add(Ones[n / 100]);
            words.Add("Hundred");
            n %= 100;
        }

        if (n > 0) words.Add(UnderHundred(n));

        return string.Join(" ", words);
    }

    private static string UnderHundred(long n)
    {
        if (n <= 0) return "";
        if (n < 20) return Ones[n];

        var tens = Tens[n / 10];
        var ones = n % 10;
        return ones == 0 ? tens : $"{tens} {Ones[ones]}";
    }
}
