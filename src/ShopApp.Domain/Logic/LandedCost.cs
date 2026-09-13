namespace ShopApp.Domain.Logic;

public record CostLine(int LineIndex, decimal Qty, decimal LineAmount);
public record LandedCostResult(int LineIndex, decimal UnitCost, decimal ApportionedCharge);

/// <summary>
/// Spreads freight/labour/cold-storage across purchase lines by value,
/// so each batch's CostPrice is the true landed cost and margins are honest.
/// Rounding remainder goes to the largest line so the total always reconciles.
/// </summary>
public static class LandedCost
{
    public static IReadOnlyList<LandedCostResult> Apportion(
        IReadOnlyList<CostLine> lines, decimal otherCharges)
    {
        if (lines.Count == 0) return Array.Empty<LandedCostResult>();

        var totalValue = lines.Sum(l => l.LineAmount);
        var results = new List<LandedCostResult>();

        // No value to apportion by (all-free goods): fall back to quantity.
        var useQty = totalValue <= 0;
        var totalQty = lines.Sum(l => l.Qty);

        decimal allocated = 0m;
        foreach (var line in lines)
        {
            decimal share;
            if (otherCharges == 0) share = 0m;
            else if (useQty) share = totalQty <= 0 ? 0m : Money.Round(otherCharges * (line.Qty / totalQty));
            else share = Money.Round(otherCharges * (line.LineAmount / totalValue));

            allocated += share;
            results.Add(new LandedCostResult(line.LineIndex, 0m, share));
        }

        // Push the rounding remainder onto the biggest line.
        var remainder = Money.Round(otherCharges - allocated);
        if (remainder != 0m && results.Count > 0)
        {
            var biggest = lines
                .Select((l, i) => (Index: i, Key: useQty ? l.Qty : l.LineAmount))
                .OrderByDescending(x => x.Key).First().Index;
            results[biggest] = results[biggest] with
            {
                ApportionedCharge = results[biggest].ApportionedCharge + remainder
            };
        }

        // Turn totals into a per-unit landed cost.
        for (int i = 0; i < results.Count; i++)
        {
            var qty = lines[i].Qty;
            var unitCost = qty <= 0
                ? 0m
                : Money.Round((lines[i].LineAmount + results[i].ApportionedCharge) / qty);
            results[i] = results[i] with { UnitCost = unitCost };
        }

        return results;
    }
}
