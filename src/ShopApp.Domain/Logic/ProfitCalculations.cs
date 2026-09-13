namespace ShopApp.Domain.Logic;

/// <summary>A sale line reduced to just what profit needs.</summary>
public record ProfitLine(
    int ItemId,
    string ItemName,
    decimal Qty,
    decimal Rate,
    decimal CostAtSale);

public record LineProfit(
    int ItemId,
    string ItemName,
    decimal Qty,
    decimal Revenue,
    decimal Cost,
    decimal Profit)
{
    /// <summary>Margin on revenue. Zero revenue gives zero rather than dividing by zero.</summary>
    public decimal MarginPercent =>
        Revenue == 0m ? 0m : decimal.Round(Profit / Revenue * 100m, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Profit maths, kept pure so it can be checked without a database.
///
/// The subtle part is the invoice-level discount. If a bill has a 5,000
/// discount across four items, that discount has to come off each line in
/// proportion to its value, or per-item profit will not add up to the profit
/// on the invoice. Getting this wrong makes every item-wise report subtly
/// disagree with the profit and loss statement.
/// </summary>
public static class ProfitCalculations
{
    /// <summary>
    /// Spreads an invoice-level discount across lines by value and returns
    /// each line's revenue, cost and profit. Rounding remainder goes to the
    /// largest line so the total always reconciles exactly.
    /// </summary>
    public static IReadOnlyList<LineProfit> ForInvoice(
        IReadOnlyList<ProfitLine> lines, decimal invoiceDiscount)
    {
        if (lines.Count == 0) return Array.Empty<LineProfit>();

        var gross = lines.Select(l => Money.Round(l.Qty * l.Rate)).ToList();
        var grossTotal = gross.Sum();

        var shares = new decimal[lines.Count];
        if (invoiceDiscount != 0m && grossTotal > 0m)
        {
            decimal allocated = 0m;
            for (int i = 0; i < lines.Count; i++)
            {
                shares[i] = Money.Round(invoiceDiscount * (gross[i] / grossTotal));
                allocated += shares[i];
            }

            var remainder = Money.Round(invoiceDiscount - allocated);
            if (remainder != 0m)
            {
                var biggest = gross.Select((g, i) => (g, i)).OrderByDescending(x => x.g).First().i;
                shares[biggest] += remainder;
            }
        }

        var result = new List<LineProfit>();
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var revenue = Money.Round(gross[i] - shares[i]);
            var cost = Money.Round(l.Qty * l.CostAtSale);
            result.Add(new LineProfit(l.ItemId, l.ItemName, l.Qty,
                revenue, cost, Money.Round(revenue - cost)));
        }

        return result;
    }

    /// <summary>Combines line profits from many invoices into one row per item.</summary>
    public static IReadOnlyList<LineProfit> GroupByItem(IEnumerable<LineProfit> lines) =>
        lines.GroupBy(l => l.ItemId)
             .Select(g => new LineProfit(
                 g.Key,
                 g.First().ItemName,
                 Money.RoundQty(g.Sum(x => x.Qty)),
                 Money.Round(g.Sum(x => x.Revenue)),
                 Money.Round(g.Sum(x => x.Cost)),
                 Money.Round(g.Sum(x => x.Profit))))
             .OrderByDescending(x => x.Profit)
             .ToList();

    /// <summary>
    /// Net profit = gross profit on goods, less running costs.
    /// Purchases are deliberately NOT subtracted: stock bought but not yet
    /// sold is an asset, not an expense. Subtracting it would show a loss in
    /// every month he restocks heavily, which is the classic mistake in a
    /// home-grown profit and loss statement.
    /// </summary>
    public static (decimal Revenue, decimal Cogs, decimal GrossProfit, decimal Expenses, decimal NetProfit)
        ProfitAndLoss(decimal revenue, decimal cogs, decimal expenses)
    {
        var grossProfit = Money.Round(revenue - cogs);
        return (Money.Round(revenue), Money.Round(cogs), grossProfit,
                Money.Round(expenses), Money.Round(grossProfit - expenses));
    }

    public static decimal MarginPercent(decimal revenue, decimal profit) =>
        revenue == 0m ? 0m : decimal.Round(profit / revenue * 100m, 2, MidpointRounding.AwayFromZero);
}
