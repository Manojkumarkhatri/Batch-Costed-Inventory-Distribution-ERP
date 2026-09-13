using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

public record SaveOutcome(bool Success, string? ErrorText, string? WarningText, int Id);

/// <summary>On-hand and what it is worth, for one item.</summary>
public record ItemStock(decimal OnHand, decimal StockValue);

/// <summary>One line of an item's movement history, as shown on the Items screen.</summary>
public record ItemTxnRow(string Type, string? RefNo, string Name, DateTime Date,
                         decimal Qty, decimal PricePerUnit, string Status)
{
    /// <summary>Stock leaving the shelf. Drives the red quantity in the grid.</summary>
    public bool IsOutward => Qty < 0;
}

/// <summary>A batch of one item, with what is left of it.</summary>
public record ItemBatchRow(int BatchId, string BatchNo, DateTime? ExpiryDate,
                           decimal CostPrice, decimal OnHand)
{
    public string Display =>
        $"{BatchNo}  -  {OnHand:N3} left  @ {CostPrice:N2}" +
        (ExpiryDate is null ? "" : $"  -  expires {ExpiryDate:dd-MMM-yyyy}");
}

public class ItemService
{
    private readonly AppDbContext _db;

    public ItemService(AppDbContext db) => _db = db;

    public IReadOnlyList<Item> Search(string? term, ItemCategory? category, bool includeInactive)
    {
        var q = _db.Items.AsQueryable();

        if (!includeInactive) q = q.Where(i => i.IsActive);
        if (category is ItemCategory c) q = q.Where(i => i.Category == c);

        // Name only. Code and Barcode are no longer entered anywhere, so
        // matching on them just widened the query for nothing.
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(i => EF.Functions.Like(i.Name, $"%{t}%"));
        }

        // AsNoTracking: the UI binds these entities directly to a form. If they
        // stayed tracked, pressing Cancel would leave edited values sitting in
        // the change tracker and a later SaveChanges would quietly persist them.
        return q.AsNoTracking().OrderBy(i => i.Name).ToList();
    }

    public Item? GetById(int id) =>
        _db.Items.AsNoTracking().FirstOrDefault(i => i.Id == id);

    /// <summary>
    /// Exact barcode match, for the scanner in Phase 4. The Barcode column is
    /// still on the table even though the entry form no longer shows it, so
    /// this keeps working the day a scanner is plugged in.
    /// </summary>
    public Item? FindByBarcode(string barcode) =>
        _db.Items.AsNoTracking().FirstOrDefault(i => i.Barcode == barcode && i.IsActive);

    public bool NameExists(string name, int excludeId)
    {
        var n = name.Trim();
        return _db.Items.Any(i => i.Id != excludeId && i.Name.ToLower() == n.ToLower());
    }

    /// <summary>
    /// Validate then save. Warnings are returned but do not block - the caller
    /// decides whether to ask the user to confirm.
    /// </summary>
    public SaveOutcome Save(Item item)
    {
        item.Name = item.Name?.Trim() ?? string.Empty;
        item.Code = string.IsNullOrWhiteSpace(item.Code) ? null : item.Code.Trim();
        item.Barcode = string.IsNullOrWhiteSpace(item.Barcode) ? null : item.Barcode.Trim();
        item.AltUnit = string.IsNullOrWhiteSpace(item.AltUnit) ? null : item.AltUnit.Trim();
        item.BaseUnit = item.BaseUnit?.Trim() ?? string.Empty;

        if (item.AltUnit is null) item.ConversionFactor = 1m;

        var result = ItemValidator.Validate(item, NameExists(item.Name, item.Id));
        if (result.HasErrors)
            return new SaveOutcome(false, result.ErrorText, result.WarningText, item.Id);

        if (item.Id == 0)
        {
            _db.Items.Add(item);
        }
        else
        {
            // Entity arrives detached (AsNoTracking above), so attach and mark dirty.
            _db.Items.Update(item);
        }

        _db.SaveChanges();
        _db.ChangeTracker.Clear();   // nothing stays tracked between operations
        return new SaveOutcome(true, null, result.WarningText, item.Id);
    }

    /// <summary>True when the item has ever moved. Such items can never be deleted.</summary>
    public bool HasHistory(int itemId) =>
        _db.PurchaseLines.Any(l => l.ItemId == itemId) ||
        _db.SaleLines.Any(l => l.ItemId == itemId);

    public decimal OnHand(int itemId)
    {
        // AsNoTracking so a long-lived context cannot serve a stale cached copy.
        var batchIds = _db.Batches.AsNoTracking()
            .Where(b => b.ItemId == itemId).Select(b => b.Id).ToList();
        if (batchIds.Count == 0) return 0m;
        var qtys = _db.StockMoves.AsNoTracking()
            .Where(m => batchIds.Contains(m.BatchId))
            .Select(m => m.Qty).ToList();
        return Money.RoundQty(qtys.Sum());
    }

    /// <summary>
    /// Deactivate rather than delete once an item has history - deleting would
    /// break every past invoice that references it. Only a never-used item is
    /// removed outright, and only when it holds no stock.
    /// </summary>
    public SaveOutcome Remove(int itemId)
    {
        var item = _db.Items.Find(itemId);
        if (item is null) return new SaveOutcome(false, "Item not found.", null, itemId);

        var onHand = OnHand(itemId);
        if (onHand != 0)
            return new SaveOutcome(false,
                $"{item.Name} still has {onHand:0.###} {item.BaseUnit} in stock. " +
                "Clear the stock first, or mark the item inactive.", null, itemId);

        if (HasHistory(itemId))
        {
            item.IsActive = false;
            _db.SaveChanges();
            return new SaveOutcome(true, null,
                $"{item.Name} has past transactions, so it was marked inactive instead of deleted.",
                itemId);
        }

        _db.Items.Remove(item);
        _db.SaveChanges();
        return new SaveOutcome(true, null, null, itemId);
    }

    public void SetActive(int itemId, bool active)
    {
        var item = _db.Items.Find(itemId);
        if (item is null) return;
        item.IsActive = active;
        _db.SaveChanges();
    }

    // ------------------------------------------------------------- stock

    /// <summary>
    /// On-hand and stock value for many items at once, for the list. Value is
    /// each batch's remaining quantity at its own landed cost - never an
    /// average, which is the whole point of costing by batch.
    /// </summary>
    public Dictionary<int, ItemStock> StockFor(IReadOnlyList<int> itemIds)
    {
        var result = new Dictionary<int, ItemStock>();
        if (itemIds.Count == 0) return result;

        var batches = _db.Batches.AsNoTracking()
            .Where(b => itemIds.Contains(b.ItemId))
            .Select(b => new { b.Id, b.ItemId, b.CostPrice })
            .ToList();
        if (batches.Count == 0) return result;

        var batchIds = batches.Select(b => b.Id).ToList();

        // Qty uses a value converter, so EF cannot SUM it in SQL.
        var onHand = _db.StockMoves.AsNoTracking()
            .Where(m => batchIds.Contains(m.BatchId))
            .Select(m => new { m.BatchId, m.Qty })
            .ToList()
            .GroupBy(m => m.BatchId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));

        foreach (var group in batches.GroupBy(b => b.ItemId))
        {
            var qty = 0m;
            var value = 0m;
            foreach (var b in group)
            {
                var q = onHand.GetValueOrDefault(b.Id);
                qty += q;
                value += q * b.CostPrice;
            }
            result[group.Key] = new ItemStock(Money.RoundQty(qty), Money.Round(value));
        }

        return result;
    }

    public ItemStock StockOf(int itemId) =>
        StockFor(new[] { itemId }).GetValueOrDefault(itemId, new ItemStock(0m, 0m));

    /// <summary>Batches of one item, newest first. Used by the adjustment dialog.</summary>
    public IReadOnlyList<ItemBatchRow> BatchesOf(int itemId, bool onlyWithStock = true)
    {
        var batches = _db.Batches.AsNoTracking()
            .Where(b => b.ItemId == itemId)
            .OrderByDescending(b => b.ReceivedAt)
            .Select(b => new { b.Id, b.BatchNo, b.ExpiryDate, b.CostPrice })
            .ToList();
        if (batches.Count == 0) return Array.Empty<ItemBatchRow>();

        var ids = batches.Select(b => b.Id).ToList();
        var onHand = _db.StockMoves.AsNoTracking()
            .Where(m => ids.Contains(m.BatchId))
            .Select(m => new { m.BatchId, m.Qty })
            .ToList()
            .GroupBy(m => m.BatchId)
            .ToDictionary(g => g.Key, g => Money.RoundQty(g.Sum(x => x.Qty)));

        var rows = batches.Select(b => new ItemBatchRow(
            b.Id, b.BatchNo, b.ExpiryDate, b.CostPrice, onHand.GetValueOrDefault(b.Id)));

        return onlyWithStock ? rows.Where(r => r.OnHand > 0).ToList() : rows.ToList();
    }

    // ----------------------------------------------------------- history

    /// <summary>
    /// Everything that has ever moved this item, newest first, read off the
    /// StockMove ledger. Sale and purchase rows are enriched with the party and
    /// the rate actually charged; opening and adjustment rows carry the batch
    /// cost instead, because there was no rate.
    ///
    /// One invoice reads as one line here even when FEFO split it across two
    /// batches. This screen answers "what happened to this item", and what
    /// happened was a sale of 150 - the batch split is an internal detail that
    /// belongs in Batch Wise Stock and Bill Wise Profit. Two lines of the same
    /// item at genuinely different rates on one bill stay separate, because
    /// the rate is part of the key.
    /// </summary>
    public IReadOnlyList<ItemTxnRow> History(int itemId, int take = 300)
    {
        var batches = _db.Batches.AsNoTracking()
            .Where(b => b.ItemId == itemId)
            .Select(b => new { b.Id, b.BatchNo, b.CostPrice })
            .ToList();
        if (batches.Count == 0) return Array.Empty<ItemTxnRow>();

        var batchIds = batches.Select(b => b.Id).ToList();
        var batchNo = batches.ToDictionary(b => b.Id, b => b.BatchNo);
        var batchCost = batches.ToDictionary(b => b.Id, b => b.CostPrice);

        // Cancelling a document writes a reversing move, so a cancelled
        // purchase produced two rows: the original marked Cancelled, and a
        // Purchase Return of the same quantity also marked Cancelled. Stock is
        // derived from the moves either way, so the reversal is dropped from
        // the history and the original row alone carries the Cancelled label.
        var reversals = new[] { StockMoveType.PurchaseReturn, StockMoveType.SaleReturn };

        var cancelledSales = _db.Sales.AsNoTracking()
            .Where(x => x.IsCancelled).Select(x => x.Id).ToList();
        var cancelledPurchases = _db.Purchases.AsNoTracking()
            .Where(x => x.IsCancelled).Select(x => x.Id).ToList();

        var moves = _db.StockMoves.AsNoTracking()
            .Where(m => batchIds.Contains(m.BatchId))
            .Where(m => !reversals.Contains(m.MoveType)
                        || m.RefId == null
                        || !(m.RefTable == nameof(Sale) && cancelledSales.Contains(m.RefId.Value))
                        && !(m.RefTable == nameof(Purchase) && cancelledPurchases.Contains(m.RefId.Value)))
            .OrderByDescending(m => m.MovedAt).ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new
            {
                m.BatchId, m.MoveType, m.Qty, m.Reason, m.ReasonNote,
                m.RefTable, m.RefId, m.MovedAt
            })
            .ToList();
        if (moves.Count == 0) return Array.Empty<ItemTxnRow>();

        var saleIds = moves.Where(m => m.RefTable == nameof(Sale) && m.RefId != null)
                           .Select(m => m.RefId!.Value).Distinct().ToList();
        var purchaseIds = moves.Where(m => m.RefTable == nameof(Purchase) && m.RefId != null)
                               .Select(m => m.RefId!.Value).Distinct().ToList();

        var sales = _db.Sales.AsNoTracking().Where(s => saleIds.Contains(s.Id))
            .Select(s => new { s.Id, s.InvoiceNo, s.IsCancelled, Party = s.Customer!.Name })
            .ToList().ToDictionary(x => x.Id);

        var purchases = _db.Purchases.AsNoTracking().Where(p => purchaseIds.Contains(p.Id))
            .Select(p => new { p.Id, p.SupplierBillNo, p.IsCancelled, Party = p.Supplier!.Name })
            .ToList().ToDictionary(x => x.Id);

        // A single document can hit two batches of the same item, so the rate
        // is keyed by document AND batch rather than by document alone.
        var saleRates = _db.SaleLines.AsNoTracking()
            .Where(l => saleIds.Contains(l.SaleId) && l.ItemId == itemId)
            .Select(l => new { l.SaleId, l.BatchId, l.Rate }).ToList()
            .GroupBy(l => (l.SaleId, l.BatchId))
            .ToDictionary(g => g.Key, g => g.First().Rate);

        var purchaseRates = _db.PurchaseLines.AsNoTracking()
            .Where(l => purchaseIds.Contains(l.PurchaseId) && l.ItemId == itemId)
            .Select(l => new { l.PurchaseId, l.BatchId, l.Rate }).ToList()
            .GroupBy(l => (l.PurchaseId, l.BatchId))
            .ToDictionary(g => g.Key, g => g.First().Rate);

        // Key groups the moves that belong to the same document line; the
        // index preserves the newest-first order the query produced.
        var built = new List<(string Key, int Order, ItemTxnRow Row)>();
        var order = 0;

        foreach (var m in moves)
        {
            string type = m.MoveType switch
            {
                StockMoveType.Purchase => "Purchase",
                StockMoveType.Sale => "Sale",
                StockMoveType.PurchaseReturn => "Purchase Return",
                StockMoveType.SaleReturn => "Sale Return",
                StockMoveType.Opening => "Opening Stock",
                StockMoveType.Adjustment => m.Reason?.ToString() ?? "Adjustment",
                _ => m.MoveType.ToString()
            };

            string? refNo = null;
            var name = batchNo.GetValueOrDefault(m.BatchId, "");
            var status = "";
            var price = batchCost.GetValueOrDefault(m.BatchId);

            if (m.RefTable == nameof(Sale) && m.RefId is int sid &&
                sales.TryGetValue(sid, out var sale))
            {
                refNo = sale.InvoiceNo;
                name = sale.Party;
                status = sale.IsCancelled ? "Cancelled" : "";
                if (saleRates.TryGetValue((sid, m.BatchId), out var r)) price = r;
            }
            else if (m.RefTable == nameof(Purchase) && m.RefId is int pid &&
                     purchases.TryGetValue(pid, out var purchase))
            {
                refNo = purchase.SupplierBillNo;
                name = purchase.Party;
                status = purchase.IsCancelled ? "Cancelled" : "";
                if (purchaseRates.TryGetValue((pid, m.BatchId), out var r)) price = r;
            }
            else if (m.MoveType == StockMoveType.Adjustment)
            {
                refNo = batchNo.GetValueOrDefault(m.BatchId);
                name = string.IsNullOrWhiteSpace(m.ReasonNote) ? "Manual adjustment" : m.ReasonNote;
            }

            var row = new ItemTxnRow(type, refNo, name, m.MovedAt,
                Money.RoundQty(m.Qty), Money.Round(price), status);

            // Moves with no document behind them - opening stock, adjustments -
            // get a key of their own so they are never merged with anything.
            var key = m.RefTable is null || m.RefId is null
                ? $"move:{order}"
                : $"{m.RefTable}:{m.RefId}:{type}:{price}:{status}";

            built.Add((key, order++, row));
        }

        return built
            .GroupBy(x => x.Key)
            .Select(g => new
            {
                Order = g.Min(x => x.Order),
                Row = g.Count() == 1
                    ? g.First().Row
                    : g.OrderBy(x => x.Order).First().Row with
                      {
                          Qty = Money.RoundQty(g.Sum(x => x.Row.Qty))
                      }
            })
            .OrderBy(x => x.Order)
            .Select(x => x.Row)
            .ToList();
    }
}
