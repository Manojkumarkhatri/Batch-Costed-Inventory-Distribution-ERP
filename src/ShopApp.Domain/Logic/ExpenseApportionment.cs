namespace ShopApp.Domain.Logic;

/// <summary>
/// Spreads an expense across the days it covers.
///
/// Rent here is paid in advance: he hands over a month's rent on the 1st, and
/// that money buys the whole month. Charging all of it to the 1st makes that
/// one day look catastrophic and every other day look free, so a daily Profit
/// and Loss is useless and a custom range is meaningless.
///
/// Instead the amount is divided by the number of days it covers, and a report
/// charges only the days that fall inside its own range. Open today and he
/// sees one day of rent; open the month and he sees all of it; open the
/// quarter and he sees three months of it.
///
/// The money still LEAVES on the day it was paid. Cash flow and the day book
/// use the payment date and the full amount - those answer "what went out of
/// the till", which is a different question from "what did this month cost".
/// </summary>
public static class ExpenseApportionment
{
    /// <summary>
    /// How much of <paramref name="amount"/> belongs to the reporting window.
    ///
    /// A one-off expense - where the covered period is a single day - is
    /// charged in full if that day is inside the window, and not at all if it
    /// is outside. No rounding, no fractions.
    /// </summary>
    public static decimal InRange(decimal amount,
                                  DateTime coversFrom, DateTime coversTo,
                                  DateTime rangeFrom, DateTime rangeTo)
    {
        var from = coversFrom.Date;
        var to = coversTo.Date;
        if (to < from) to = from;

        var windowStart = rangeFrom.Date;
        var windowEnd = rangeTo.Date;
        if (windowEnd < windowStart) return 0m;

        var overlapStart = from > windowStart ? from : windowStart;
        var overlapEnd = to < windowEnd ? to : windowEnd;
        if (overlapEnd < overlapStart) return 0m;

        var coveredDays = (to - from).Days + 1;
        var overlapDays = (overlapEnd - overlapStart).Days + 1;

        if (coveredDays <= 1) return amount;
        if (overlapDays >= coveredDays) return amount;

        return Money.Round(amount * overlapDays / coveredDays);
    }

    /// <summary>What one day of this expense costs. For the dialog preview.</summary>
    public static decimal PerDay(decimal amount, DateTime coversFrom, DateTime coversTo)
    {
        var days = Days(coversFrom, coversTo);
        return days <= 1 ? amount : Money.Round(amount / days);
    }

    public static int Days(DateTime coversFrom, DateTime coversTo)
    {
        var from = coversFrom.Date;
        var to = coversTo.Date;
        return to < from ? 1 : (to - from).Days + 1;
    }

    /// <summary>
    /// The period a chosen frequency covers, starting from the day he paid.
    /// A month means the calendar month, not thirty days - rent paid on
    /// 1 February covers 28 days, and he still calls it a month's rent.
    /// </summary>
    public static (DateTime From, DateTime To) PeriodFor(string frequency, DateTime start)
    {
        var from = start.Date;

        var to = frequency switch
        {
            "Daily" => from,
            "Weekly" => from.AddDays(7).AddDays(-1),
            "Monthly" => from.AddMonths(1).AddDays(-1),
            "Quarterly" => from.AddMonths(3).AddDays(-1),
            "Half-yearly" => from.AddMonths(6).AddDays(-1),
            "Yearly" => from.AddYears(1).AddDays(-1),
            _ => from            // One-off
        };

        return (from, to);
    }

    public static IReadOnlyList<string> Frequencies { get; } = new[]
    {
        "One-off", "Daily", "Weekly", "Monthly", "Quarterly", "Half-yearly", "Yearly", "Custom"
    };
}
