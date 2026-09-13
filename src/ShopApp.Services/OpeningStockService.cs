using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

public record OpeningStockLineInput(
    int ItemId, string? BatchNo, decimal Qty, decimal CostPrice,
    DateTime? MfgDate, DateTime? ExpiryDate, string? StorageLocation);

public record OpeningStockResult(bool Success, string? ErrorText, string? WarningText, int RowsSaved);

/// <summary>
/// What is physically in the godown on go-live day.
///
/// Recorded as StockMoveType.Opening, never as a Purchase. That distinction
/// matters: his first month's purchase figures and payables must not include
/// stock he already owned before switching to the app.
/// </summary>
public class OpeningStockService
{
    private readonly AppDbContext _db;

    public OpeningStockService(AppDbContext db) => _db = db;

    /// <summary>True when this item already has an opening entry, to catch double entry.</summary>
    public bool HasOpening(int itemId) =>
        _db.StockMoves.Any(m => m.MoveType == StockMoveType.Opening
                             && _db.Batches.Any(b => b.Id == m.BatchId && b.ItemId == itemId));

    public IReadOnlyList<int> ItemsWithOpening()
    {
        var openingBatchIds = _db.StockMoves
            .Where(m => m.MoveType == StockMoveType.Opening)
            .Select(m => m.BatchId).Distinct().ToList();

        return _db.Batches.Where(b => openingBatchIds.Contains(b.Id))
            .Select(b => b.ItemId).Distinct().ToList();
    }

    public OpeningStockResult Save(DateTime asOfDate, IReadOnlyList<OpeningStockLineInput> lines)
    {
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id)).ToDictionary(i => i.Id);

        var withOpening = ItemsWithOpening().ToHashSet();

        var drafts = lines.Select((l, i) =>
        {
            items.TryGetValue(l.ItemId, out var item);
            return new OpeningStockDraft(
                i + 1, l.ItemId, item?.Name ?? "", l.BatchNo, l.Qty, l.CostPrice,
                l.MfgDate, l.ExpiryDate,
                item?.Category ?? ItemCategory.Solid,
                AlreadyHasOpening: withOpening.Contains(l.ItemId));
        }).ToList();

        var validation = OpeningStockValidator.Validate(asOfDate, drafts);
        if (validation.HasErrors)
            return new OpeningStockResult(false, validation.ErrorText, validation.WarningText, 0);

        using var tx = _db.Database.BeginTransaction();
        try
        {
            var usedBatchNos = _db.Batches.AsNoTracking().Select(b => b.BatchNo).ToList();
            var saved = 0;

            foreach (var line in lines)
            {
                var batchNo = string.IsNullOrWhiteSpace(line.BatchNo)
                    ? BatchNumbering.NextFor(asOfDate, usedBatchNos)
                    : line.BatchNo.Trim();
                usedBatchNos.Add(batchNo);

                var batch = new Batch
                {
                    ItemId = line.ItemId,
                    BatchNo = batchNo,
                    MfgDate = line.MfgDate,
                    ExpiryDate = line.ExpiryDate,
                    CostPrice = line.CostPrice,
                    StorageLocation = line.StorageLocation,
                    ReceivedAt = asOfDate
                };
                _db.Batches.Add(batch);
                _db.SaveChanges();

                _db.StockMoves.Add(new StockMove
                {
                    BatchId = batch.Id,
                    MoveType = StockMoveType.Opening,
                    Qty = line.Qty,
                    ReasonNote = "Opening stock at go-live",
                    MovedAt = asOfDate
                });
                saved++;
            }

            _db.SaveChanges();
            tx.Commit();
            _db.ChangeTracker.Clear();

            return new OpeningStockResult(true, null, validation.WarningText, saved);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _db.ChangeTracker.Clear();
            return new OpeningStockResult(false, $"Could not save opening stock:\n{ex.Message}", null, 0);
        }
    }

    /// <summary>Total value of opening stock, for checking against his own figure.</summary>
    public decimal OpeningStockValue()
    {
        var openingMoves = _db.StockMoves
            .Where(m => m.MoveType == StockMoveType.Opening)
            .Select(m => new { m.BatchId, m.Qty }).ToList();

        if (openingMoves.Count == 0) return 0m;

        var batchIds = openingMoves.Select(m => m.BatchId).Distinct().ToList();
        var costs = _db.Batches.Where(b => batchIds.Contains(b.Id))
            .Select(b => new { b.Id, b.CostPrice })
            .ToDictionary(x => x.Id, x => x.CostPrice);

        return Money.Round(openingMoves.Sum(m =>
            m.Qty * (costs.TryGetValue(m.BatchId, out var c) ? c : 0m)));
    }
}
