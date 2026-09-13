using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

public record PurchaseLineInput(
    int ItemId, string? BatchNo, DateTime? MfgDate, DateTime? ExpiryDate,
    decimal? PackQty, decimal Qty, decimal Rate, string? StorageLocation);

public record PurchaseInput(
    int SupplierId, string? SupplierBillNo, DateTime Date,
    decimal Discount, decimal OtherCharges, string? Notes,
    IReadOnlyList<PurchaseLineInput> Lines,
    decimal PaidNow, PaymentMode PaymentMode);

public record PurchaseResult(bool Success, string? ErrorText, string? WarningText, int PurchaseId);

/// <summary>One row of the Purchase Bills list.</summary>
public record PurchaseListRow(int Id, DateTime Date, string? SupplierBillNo, int PartyId,
                              string PartyName, decimal Amount, decimal Paid,
                              bool IsCancelled)
{
    public decimal Balance => Money.Round(Amount - Paid);
    public string Number => string.IsNullOrWhiteSpace(SupplierBillNo) ? $"#{Id}" : SupplierBillNo!;
    public string PaymentType => Paid >= Amount && Amount > 0 ? "Cash" : "Credit";
    public string Transaction => IsCancelled ? "Cancelled" : "Purchase";
    public string Status => IsCancelled ? "Cancelled" : "";
}

public class PurchaseService
{
    private readonly AppDbContext _db;

    public PurchaseService(AppDbContext db) => _db = db;

    /// <summary>
    /// Each line becomes one batch. Freight and other charges are apportioned
    /// across lines by value and folded into the batch cost, so margin later
    /// is measured against what the goods actually cost to get here - not just
    /// the supplier's rate.
    ///
    /// Header discount reduces cost the same way, so net charges can be
    /// negative. LandedCost handles that.
    /// </summary>
    public PurchaseResult Create(PurchaseInput input)
    {
        // Validate against the real item records, not just what the UI sent.
        var itemIds = input.Lines.Select(l => l.ItemId).Distinct().ToList();
        var items = _db.Items.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id))
            .ToDictionary(i => i.Id);

        var drafts = input.Lines.Select((l, idx) =>
        {
            items.TryGetValue(l.ItemId, out var item);
            return new PurchaseLineDraft(
                idx + 1, l.ItemId, item?.Name ?? "", l.BatchNo, l.MfgDate, l.ExpiryDate,
                l.Qty, l.Rate,
                item?.Category ?? ItemCategory.Solid,
                RequiresExpiry: item?.Category is ItemCategory.Frozen or ItemCategory.Liquid);
        }).ToList();

        var validation = PurchaseValidator.Validate(
            input.SupplierId, input.Date, input.Discount, input.OtherCharges, drafts);

        if (validation.HasErrors)
            return new PurchaseResult(false, validation.ErrorText, validation.WarningText, 0);

        using var tx = _db.Database.BeginTransaction();
        try
        {
            var purchase = new Purchase
            {
                SupplierId = input.SupplierId,
                SupplierBillNo = string.IsNullOrWhiteSpace(input.SupplierBillNo)
                    ? null : input.SupplierBillNo.Trim(),
                Date = input.Date,
                Discount = input.Discount,
                OtherCharges = input.OtherCharges,
                Notes = input.Notes
            };

            var costLines = input.Lines
                .Select((l, idx) => new CostLine(idx, l.Qty, Money.Round(l.Qty * l.Rate)))
                .ToList();

            purchase.SubTotal = Money.Round(costLines.Sum(c => c.LineAmount));
            purchase.Total = Money.Round(
                purchase.SubTotal - input.Discount + input.OtherCharges);

            // Discount lowers cost, other charges raise it.
            var netCharges = Money.Round(input.OtherCharges - input.Discount);
            var landed = LandedCost.Apportion(costLines, netCharges);

            _db.Purchases.Add(purchase);
            _db.SaveChanges();   // need purchase.Id for the stock move reference

            var usedBatchNos = _db.Batches.AsNoTracking()
                .Select(b => b.BatchNo).ToList();

            for (int i = 0; i < input.Lines.Count; i++)
            {
                var line = input.Lines[i];
                var unitCost = landed[i].UnitCost;

                var batchNo = string.IsNullOrWhiteSpace(line.BatchNo)
                    ? BatchNumbering.NextFor(input.Date, usedBatchNos)
                    : line.BatchNo.Trim();
                usedBatchNos.Add(batchNo);

                var batch = new Batch
                {
                    ItemId = line.ItemId,
                    BatchNo = batchNo,
                    MfgDate = line.MfgDate,
                    ExpiryDate = line.ExpiryDate,
                    CostPrice = unitCost,
                    StorageLocation = line.StorageLocation,
                    ReceivedAt = input.Date
                };
                _db.Batches.Add(batch);
                _db.SaveChanges();   // need batch.Id

                _db.PurchaseLines.Add(new PurchaseLine
                {
                    PurchaseId = purchase.Id,
                    ItemId = line.ItemId,
                    BatchId = batch.Id,
                    PackQty = line.PackQty,
                    Qty = line.Qty,
                    Rate = line.Rate,
                    Amount = Money.Round(line.Qty * line.Rate)
                });

                _db.StockMoves.Add(new StockMove
                {
                    BatchId = batch.Id,
                    MoveType = StockMoveType.Purchase,
                    Qty = line.Qty,                  // positive: stock arriving
                    RefTable = nameof(Purchase),
                    RefId = purchase.Id,
                    MovedAt = input.Date
                });
            }

            if (input.PaidNow > 0)
            {
                var payment = new Payment
                {
                    PartyId = input.SupplierId,
                    Direction = PaymentDirection.Out,
                    Mode = input.PaymentMode,
                    Amount = input.PaidNow,
                    Date = input.Date
                };
                payment.Allocations.Add(new PaymentAllocation
                {
                    PurchaseId = purchase.Id,
                    Amount = Math.Min(input.PaidNow, purchase.Total)
                });
                _db.Payments.Add(payment);
            }

            _db.SaveChanges();
            tx.Commit();
            _db.ChangeTracker.Clear();

            return new PurchaseResult(true, null, validation.WarningText, purchase.Id);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _db.ChangeTracker.Clear();
            return new PurchaseResult(false, $"Could not save the purchase:\n{ex.Message}", null, 0);
        }
    }

    public IReadOnlyList<Purchase> Recent(int count = 50) =>
        _db.Purchases.AsNoTracking()
            .Include(p => p.Supplier)
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .Take(count).ToList();

    public Purchase? GetById(int id) =>
        _db.Purchases.AsNoTracking()
            .Include(p => p.Supplier)
            .Include(p => p.Lines).ThenInclude(l => l.Item)
            .Include(p => p.Lines).ThenInclude(l => l.Batch)
            .FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Reverses the stock rather than deleting rows. Blocked if any of the
    /// stock has already been sold, because reversing it would push the batch
    /// negative and the books would stop reconciling.
    /// </summary>
    public PurchaseResult Cancel(int purchaseId, string reason)
    {
        using var tx = _db.Database.BeginTransaction();
        try
        {
            var purchase = _db.Purchases.Include(p => p.Lines)
                .FirstOrDefault(p => p.Id == purchaseId);
            if (purchase is null)
                return new PurchaseResult(false, "Purchase not found.", null, purchaseId);
            if (purchase.IsCancelled)
                return new PurchaseResult(true, null, "Already cancelled.", purchaseId);

            foreach (var line in purchase.Lines)
            {
                var moves = _db.StockMoves.Where(m => m.BatchId == line.BatchId)
                    .Select(m => m.Qty).ToList();
                var onHand = Money.RoundQty(moves.Sum());

                if (onHand < line.Qty)
                {
                    tx.Rollback();
                    var item = _db.Items.Find(line.ItemId);
                    return new PurchaseResult(false,
                        $"Cannot cancel: some of {item?.Name ?? "this stock"} has already been sold. " +
                        $"Received {line.Qty:0.###}, only {onHand:0.###} left.", null, purchaseId);
                }
            }

            foreach (var line in purchase.Lines)
            {
                _db.StockMoves.Add(new StockMove
                {
                    BatchId = line.BatchId,
                    MoveType = StockMoveType.PurchaseReturn,
                    Qty = -line.Qty,
                    Reason = AdjustmentReason.Other,
                    ReasonNote = $"Purchase cancelled: {reason}",
                    RefTable = nameof(Purchase),
                    RefId = purchase.Id,
                    MovedAt = DateTime.Now
                });
            }

            purchase.IsCancelled = true;
            _db.SaveChanges();
            tx.Commit();
            _db.ChangeTracker.Clear();
            return new PurchaseResult(true, null, null, purchaseId);
        }
        catch (Exception ex)
        {
            tx.Rollback();
            _db.ChangeTracker.Clear();
            return new PurchaseResult(false, ex.Message, null, purchaseId);
        }
    }

    /// <summary>
    /// Bills in a date range with how much has been paid against each.
    /// Cancelled bills stay in the list for the same reason cancelled invoices
    /// do - a gap is harder to explain later than a voided row.
    /// </summary>
    public IReadOnlyList<PurchaseListRow> List(DateTime from, DateTime to)
    {
        var bills = _db.Purchases.AsNoTracking()
            .Where(p => p.Date >= from.Date && p.Date <= to.Date)
            .Select(p => new
            {
                p.Id, p.Date, p.SupplierBillNo, p.SupplierId,
                Party = p.Supplier!.Name, p.Total, p.IsCancelled
            })
            .ToList();

        if (bills.Count == 0) return Array.Empty<PurchaseListRow>();

        var ids = bills.Select(b => b.Id).ToList();

        // Amount uses a value converter, so EF cannot SUM it in SQL.
        var paid = _db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PurchaseId != null && ids.Contains(a.PurchaseId!.Value))
            .Select(a => new { PurchaseId = a.PurchaseId!.Value, a.Amount })
            .ToList()
            .GroupBy(a => a.PurchaseId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        return bills
            .OrderByDescending(b => b.Date).ThenByDescending(b => b.Id)
            .Select(b => new PurchaseListRow(
                b.Id, b.Date, b.SupplierBillNo, b.SupplierId, b.Party,
                Money.Round(b.Total), Money.Round(paid.GetValueOrDefault(b.Id)),
                b.IsCancelled))
            .ToList();
    }
}
