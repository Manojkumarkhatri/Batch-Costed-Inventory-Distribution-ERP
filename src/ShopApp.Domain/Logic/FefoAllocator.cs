namespace ShopApp.Domain.Logic;

/// <summary>A batch with stock available to sell.</summary>
public record AvailableBatch(
    int BatchId,
    string BatchNo,
    decimal Available,
    DateTime? ExpiryDate,
    DateTime ReceivedAt,
    decimal CostPrice);

public record Allocation(int BatchId, string BatchNo, decimal Qty, decimal CostPrice);

public record AllocationResult(
    IReadOnlyList<Allocation> Allocations,
    decimal Shortfall)
{
    public bool IsComplete => Shortfall <= 0m;
}

/// <summary>
/// First Expiry First Out. Food moves by expiry date, not arrival date,
/// so plain FIFO would quietly leave the soonest-expiring stock to rot.
/// Batches with no expiry (dry goods) sort last, oldest received first.
/// </summary>
public static class FefoAllocator
{
    public static AllocationResult Allocate(
        IEnumerable<AvailableBatch> batches,
        decimal requiredQty,
        DateTime? asOf = null,
        bool excludeExpired = true)
    {
        if (requiredQty <= 0)
            return new AllocationResult(Array.Empty<Allocation>(), 0m);

        var today = asOf ?? DateTime.Today;

        var ordered = batches
            .Where(b => b.Available > 0)
            .Where(b => !excludeExpired || b.ExpiryDate == null || b.ExpiryDate.Value.Date >= today.Date)
            // null expiry sorts last
            .OrderBy(b => b.ExpiryDate == null ? 1 : 0)
            .ThenBy(b => b.ExpiryDate ?? DateTime.MaxValue)
            .ThenBy(b => b.ReceivedAt)
            .ThenBy(b => b.BatchId)
            .ToList();

        var allocations = new List<Allocation>();
        var remaining = Money.RoundQty(requiredQty);

        foreach (var batch in ordered)
        {
            if (remaining <= 0) break;
            var take = Math.Min(batch.Available, remaining);
            take = Money.RoundQty(take);
            if (take <= 0) continue;

            allocations.Add(new Allocation(batch.BatchId, batch.BatchNo, take, batch.CostPrice));
            remaining = Money.RoundQty(remaining - take);
        }

        return new AllocationResult(allocations, remaining > 0 ? remaining : 0m);
    }
}
