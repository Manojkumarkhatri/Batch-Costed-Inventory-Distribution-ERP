using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

public record StockRow(int ItemId, string ItemName, ItemCategory Category,
                       string BaseUnit, decimal OnHand, decimal ReorderLevel,
                       decimal StockValue);

public record ExpiryRow(int BatchId, string BatchNo, int ItemId, string ItemName,
                        ItemCategory Category, DateTime ExpiryDate, int DaysLeft,
                        decimal OnHand, decimal StockValue, string? Location);

/// <summary>
/// Stock is DERIVED, never stored. On-hand is always SUM(StockMove.Qty).
/// No code path anywhere updates a quantity column - that is how inventory
/// systems drift permanently out of sync with the shelf.
/// </summary>
public class StockService
{
    private readonly AppDbContext _db;

    public StockService(AppDbContext db) => _db = db;

    public decimal BatchOnHand(int batchId)
    {
        // Qty uses a value converter, so EF cannot SUM it in SQL.
        // Filtering happens in SQL; the addition happens here. Rows per batch
        // are small, so this stays fast.
        var moves = _db.StockMoves.AsNoTracking()
            .Where(m => m.BatchId == batchId)
            .Select(m => m.Qty)
            .ToList();
        return Money.RoundQty(moves.Sum());
    }

    public IReadOnlyList<AvailableBatch> AvailableBatchesForItem(int itemId)
    {
        var batches = _db.Batches.AsNoTracking()
            .Where(b => b.ItemId == itemId)
            .Select(b => new { b.Id, b.BatchNo, b.ExpiryDate, b.ReceivedAt, b.CostPrice })
            .ToList();

        var batchIds = batches.Select(b => b.Id).ToList();

        var moves = _db.StockMoves.AsNoTracking()
            .Where(m => batchIds.Contains(m.BatchId))
            .Select(m => new { m.BatchId, m.Qty })
            .ToList();

        var onHand = moves
            .GroupBy(m => m.BatchId)
            .ToDictionary(g => g.Key, g => Money.RoundQty(g.Sum(x => x.Qty)));

        return batches
            .Select(b => new AvailableBatch(
                b.Id, b.BatchNo,
                onHand.TryGetValue(b.Id, out var q) ? q : 0m,
                b.ExpiryDate, b.ReceivedAt, b.CostPrice))
            .Where(b => b.Available > 0)
            .ToList();
    }

    public IReadOnlyList<StockRow> StockSummary(ItemCategory? category = null)
    {
        var items = _db.Items.AsNoTracking().Where(i => i.IsActive);
        if (category is ItemCategory c) items = items.Where(i => i.Category == c);

        var itemList = items
            .Select(i => new { i.Id, i.Name, i.Category, i.BaseUnit, i.ReorderLevel })
            .ToList();

        var batchCosts = _db.Batches.AsNoTracking()
            .Select(b => new { b.Id, b.ItemId, b.CostPrice })
            .ToList();

        var moves = _db.StockMoves.AsNoTracking()
            .Select(m => new { m.BatchId, m.Qty })
            .ToList();

        var perBatch = moves
            .GroupBy(m => m.BatchId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

        var perItem = batchCosts
            .GroupBy(b => b.ItemId)
            .ToDictionary(
                g => g.Key,
                g => g.Aggregate(
                    (Qty: 0m, Value: 0m),
                    (acc, b) =>
                    {
                        var q = perBatch.TryGetValue(b.Id, out var v) ? v : 0m;
                        return (acc.Qty + q, acc.Value + q * b.CostPrice);
                    }));

        return itemList.Select(i =>
        {
            var agg = perItem.TryGetValue(i.Id, out var a) ? a : (Qty: 0m, Value: 0m);
            return new StockRow(i.Id, i.Name, i.Category, i.BaseUnit,
                Money.RoundQty(agg.Qty), i.ReorderLevel, Money.Round(agg.Value));
        })
        .OrderBy(r => r.ItemName)
        .ToList();
    }

    /// <summary>
    /// The report Vyapar handles poorly and this app should do well.
    /// Anything already expired shows with a negative DaysLeft.
    /// </summary>
    public IReadOnlyList<ExpiryRow> NearExpiry(int withinDays = 30, DateTime? asOf = null)
    {
        var today = (asOf ?? DateTime.Today).Date;
        var cutoff = today.AddDays(withinDays);

        var batches = _db.Batches.AsNoTracking()
            .Include(b => b.Item)
            .Where(b => b.ExpiryDate != null && b.ExpiryDate <= cutoff)
            .ToList();

        var ids = batches.Select(b => b.Id).ToList();
        var moves = _db.StockMoves
            .Where(m => ids.Contains(m.BatchId))
            .Select(m => new { m.BatchId, m.Qty })
            .ToList();

        var onHand = moves.GroupBy(m => m.BatchId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

        return batches
            .Select(b =>
            {
                var q = onHand.TryGetValue(b.Id, out var v) ? Money.RoundQty(v) : 0m;
                return new ExpiryRow(
                    b.Id, b.BatchNo, b.ItemId, b.Item?.Name ?? "",
                    b.Item?.Category ?? ItemCategory.Solid,
                    b.ExpiryDate!.Value,
                    (b.ExpiryDate.Value.Date - today).Days,
                    q, Money.Round(q * b.CostPrice), b.StorageLocation);
            })
            .Where(r => r.OnHand > 0)          // sold-out batches are not a problem
            .OrderBy(r => r.DaysLeft)
            .ToList();
    }

    public IReadOnlyList<StockRow> LowStock() =>
        StockSummary().Where(r => r.ReorderLevel > 0 && r.OnHand <= r.ReorderLevel).ToList();

    /// <summary>Manual correction. Always a new append-only row, never an edit.</summary>
    public void Adjust(int batchId, decimal signedQty, AdjustmentReason reason, string? note)
    {
        if (signedQty == 0) return;

        _db.StockMoves.Add(new StockMove
        {
            BatchId = batchId,
            MoveType = StockMoveType.Adjustment,
            Qty = signedQty,
            Reason = reason,
            ReasonNote = note,
            MovedAt = DateTime.Now
        });
        _db.SaveChanges();
    }
}
