using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

public record SaleLineInput(int ItemId, int? BatchId, decimal Qty, decimal Rate, decimal? PackQty);
public record SaleInput(int CustomerId, DateTime Date, decimal Discount,
                        string? Notes, IReadOnlyList<SaleLineInput> Lines,
                        decimal PaidNow, PaymentMode PaymentMode,
                        InvoicePrintOptions? PrintOptions = null);

/// <summary>One row of the Sale Invoices list.</summary>
public record SaleListRow(int Id, DateTime Date, string InvoiceNo, int PartyId,
                          string PartyName, decimal Amount, decimal Received,
                          bool IsCancelled)
{
    public decimal Balance => Money.Round(Amount - Received);

    /// <summary>Settled at the counter reads Cash; anything left owing is Credit.</summary>
    public string PaymentType => Received >= Amount && Amount > 0 ? "Cash" : "Credit";

    public string Status => IsCancelled ? "Cancelled" : "";

    /// <summary>What kind of document this row is, as Vyapar labels it.</summary>
    public string Transaction => IsCancelled ? "Cancelled" : "Sale";
}

public class SaleService
{
    private readonly AppDbContext _db;
    private readonly StockService _stock;

    public SaleService(AppDbContext db, StockService stock)
    {
        _db = db;
        _stock = stock;
    }

    /// <summary>Shows him the number the next bill will carry, before he saves it.</summary>
    public string PeekNextInvoiceNo()
    {
        var settings = _db.Settings.AsNoTracking().Single(s => s.Id == 1);
        return InvoiceNumber.Format(settings.InvoicePrefix, settings.InvoiceNextNumber,
            settings.InvoiceNumberPadding, settings.InvoiceResetYearly, DateTime.Today);
    }

    /// <summary>
    /// One sale writes to sales, sale_lines, stock_moves and possibly payments.
    /// All of it commits together or none of it does. A half-written sale is
    /// worse than a crash: stock silently stops matching the shelf.
    /// </summary>
    public Sale Create(SaleInput input)
    {
        if (input.Lines.Count == 0)
            throw new InvalidOperationException("A sale needs at least one line.");

        using var tx = _db.Database.BeginTransaction();

        var settings = _db.Settings.Single(s => s.Id == 1);

        // Internal reference counts every sale ever recorded, cancelled ones
        // included, so it stays gapless even if a printed number is reused.
        var nextInternal = (_db.Sales.Max(x => (int?)x.InternalRefNo) ?? 0) + 1;

        var sale = new Sale
        {
            CustomerId = input.CustomerId,
            Date = input.Date,
            Discount = input.Discount,
            Notes = input.Notes,
            InternalRefNo = nextInternal,
            InvoiceNo = InvoiceNumber.Format(
                settings.InvoicePrefix, settings.InvoiceNextNumber,
                settings.InvoiceNumberPadding, settings.InvoiceResetYearly, input.Date)
        };

        decimal subTotal = 0m;

        foreach (var line in input.Lines)
        {
            // Explicit batch, or let FEFO choose.
            var allocations = line.BatchId is int fixedBatch
                ? new List<Allocation> { BuildFixedAllocation(fixedBatch, line.Qty) }
                : AllocateFefo(line.ItemId, line.Qty, input.Date);

            foreach (var alloc in allocations)
            {
                var amount = Money.Round(alloc.Qty * line.Rate);
                subTotal += amount;

                sale.Lines.Add(new SaleLine
                {
                    ItemId = line.ItemId,
                    BatchId = alloc.BatchId,
                    Qty = alloc.Qty,
                    PackQty = line.PackQty,
                    Rate = line.Rate,
                    Amount = amount,
                    // frozen at sale time so past margins never shift
                    CostAtSale = alloc.CostPrice
                });
            }
        }

        sale.SubTotal = Money.Round(subTotal);
        sale.Total = Money.Round(sale.SubTotal - sale.Discount);

        _db.Sales.Add(sale);
        _db.SaveChanges();   // need sale.Id before writing stock moves

        foreach (var line in sale.Lines)
        {
            _db.StockMoves.Add(new StockMove
            {
                BatchId = line.BatchId,
                MoveType = StockMoveType.Sale,
                Qty = -line.Qty,               // negative: stock leaving
                RefTable = nameof(Sale),
                RefId = sale.Id,
                MovedAt = input.Date
            });
        }

        if (input.PaidNow > 0)
        {
            var payment = new Payment
            {
                PartyId = input.CustomerId,
                Direction = PaymentDirection.In,
                Mode = input.PaymentMode,
                Amount = input.PaidNow,
                Date = input.Date
            };
            payment.Allocations.Add(new PaymentAllocation
            {
                SaleId = sale.Id,
                Amount = Math.Min(input.PaidNow, sale.Total)
            });
            _db.Payments.Add(payment);
        }

        // Saved against the sale so a reprint reproduces exactly what the
        // customer was handed the first time.
        var options = (input.PrintOptions ?? InvoicePrintOptions.Defaults()).Clone();
        options.SaleId = sale.Id;
        _db.InvoicePrintOptions.Add(options);

        settings.InvoiceNextNumber++;

        _db.SaveChanges();
        tx.Commit();
        _db.ChangeTracker.Clear();

        return sale;
    }

    private Allocation BuildFixedAllocation(int batchId, decimal qty)
    {
        var batch = _db.Batches.Find(batchId)
            ?? throw new InvalidOperationException($"Batch {batchId} not found.");
        var available = _stock.BatchOnHand(batchId);
        if (available < qty)
            throw new InvalidOperationException(
                $"Batch {batch.BatchNo} has only {available:0.###} available, {qty:0.###} requested.");
        return new Allocation(batchId, batch.BatchNo, qty, batch.CostPrice);
    }

    private List<Allocation> AllocateFefo(int itemId, decimal qty, DateTime asOf)
    {
        var available = _stock.AvailableBatchesForItem(itemId);
        var result = FefoAllocator.Allocate(available, qty, asOf);

        if (!result.IsComplete)
        {
            var item = _db.Items.Find(itemId);
            throw new InvalidOperationException(
                $"Not enough stock for {item?.Name ?? $"item {itemId}"}. " +
                $"Short by {result.Shortfall:0.###}.");
        }

        return result.Allocations.ToList();
    }

    /// <summary>
    /// Invoices are never deleted. Cancelling reverses the stock with new
    /// positive moves, so the audit trail and the reports still reconcile.
    /// </summary>
    public void Cancel(int saleId, string reason)
    {
        using var tx = _db.Database.BeginTransaction();

        var sale = _db.Sales.Include(s => s.Lines).Single(s => s.Id == saleId);
        if (sale.IsCancelled) return;

        foreach (var line in sale.Lines)
        {
            _db.StockMoves.Add(new StockMove
            {
                BatchId = line.BatchId,
                MoveType = StockMoveType.SaleReturn,
                Qty = line.Qty,                 // positive: stock coming back
                Reason = AdjustmentReason.Other,
                ReasonNote = $"Invoice {sale.InvoiceNo} cancelled: {reason}",
                RefTable = nameof(Sale),
                RefId = sale.Id,
                MovedAt = DateTime.Now
            });
        }

        sale.IsCancelled = true;
        _db.SaveChanges();
        tx.Commit();
    }

    /// <summary>
    /// Invoices in a date range with how much has been received against each.
    /// Cancelled invoices stay in the list: a missing number is harder to
    /// explain later than a voided one.
    /// </summary>
    public IReadOnlyList<SaleListRow> List(DateTime from, DateTime to)
    {
        var sales = _db.Sales.AsNoTracking()
            .Where(s => s.Date >= from.Date && s.Date <= to.Date)
            .Select(s => new
            {
                s.Id, s.Date, s.InvoiceNo, s.CustomerId,
                Party = s.Customer!.Name, s.Total, s.IsCancelled
            })
            .ToList();

        if (sales.Count == 0) return Array.Empty<SaleListRow>();

        var ids = sales.Select(s => s.Id).ToList();

        // Total and Amount use a value converter, so EF cannot SUM in SQL.
        var received = _db.PaymentAllocations.AsNoTracking()
            .Where(a => a.SaleId != null && ids.Contains(a.SaleId!.Value))
            .Select(a => new { SaleId = a.SaleId!.Value, a.Amount })
            .ToList()
            .GroupBy(a => a.SaleId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        return sales
            .OrderByDescending(s => s.Date).ThenByDescending(s => s.Id)
            .Select(s => new SaleListRow(
                s.Id, s.Date, s.InvoiceNo, s.CustomerId, s.Party,
                Money.Round(s.Total),
                Money.Round(received.GetValueOrDefault(s.Id)),
                s.IsCancelled))
            .ToList();
    }
}
