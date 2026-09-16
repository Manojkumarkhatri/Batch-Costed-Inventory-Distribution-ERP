using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

/// <summary>One recorded payment, as shown on the Payment-In list.</summary>
public record PaymentListRow(int Id, DateTime Date, int PartyId, string PartyName,
                             PaymentMode Mode, decimal Amount, string? ReferenceNo,
                             decimal Allocated, string? Notes, string? VoucherNo)
{
    /// <summary>Money received that is not yet tied to a specific invoice.</summary>
    public decimal OnAccount => Money.Round(Amount - Allocated);

    public string Settles => Allocated <= 0
        ? "On account"
        : OnAccount > 0 ? $"{Allocated:N0} allocated, {OnAccount:N0} on account"
                        : "Fully allocated";
}

/// <summary>An invoice or bill with something still outstanding on it.</summary>
public record OpenDocumentRow(int DocumentId, string Number, DateTime Date,
                              decimal Total, decimal Paid)
{
    public decimal Outstanding => Money.Round(Total - Paid);
    public string Display => $"{Number}  -  {Date:dd-MMM-yyyy}  -  {Outstanding:N2} due";
}

public record PaymentInput(int PartyId, PaymentDirection Direction, DateTime Date,
                           decimal Amount, PaymentMode Mode, string? ReferenceNo,
                           DateTime? ChequeDueDate, string? Notes);

/// <summary>
/// Recording money moving between him and a party.
///
/// Payments settle invoices oldest first. That is the convention every party
/// already assumes when they hand over a lump sum without saying what it is
/// for, and it is what makes the aging buckets in the receivables report mean
/// anything. Anything left over after the open invoices are covered stays
/// unallocated - it still moves the party's balance, it just is not tied to a
/// document yet.
/// </summary>
public class PaymentService
{
    private readonly AppDbContext _db;

    public PaymentService(AppDbContext db) => _db = db;

    public IReadOnlyList<PaymentListRow> List(DateTime from, DateTime to,
                                              PaymentDirection direction,
                                              int? partyId = null)
    {
        var q = _db.Payments.AsNoTracking()
            .Where(p => p.Direction == direction
                     && p.Date >= from.Date && p.Date <= to.Date);

        if (partyId is int id) q = q.Where(p => p.PartyId == id);

        var payments = q
            .Select(p => new
            {
                p.Id, p.Date, p.PartyId, Party = p.Party!.Name,
                p.Mode, p.Amount, p.ReferenceNo, p.Notes, p.VoucherNo
            })
            .ToList();

        if (payments.Count == 0) return Array.Empty<PaymentListRow>();

        var ids = payments.Select(p => p.Id).ToList();

        // Amount uses a value converter, so EF cannot SUM it in SQL.
        var allocated = _db.PaymentAllocations.AsNoTracking()
            .Where(a => ids.Contains(a.PaymentId))
            .Select(a => new { a.PaymentId, a.Amount })
            .ToList()
            .GroupBy(a => a.PaymentId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        return payments
            .OrderByDescending(p => p.Date).ThenByDescending(p => p.Id)
            .Select(p => new PaymentListRow(
                p.Id, p.Date, p.PartyId, p.Party, p.Mode, Money.Round(p.Amount),
                p.ReferenceNo, Money.Round(allocated.GetValueOrDefault(p.Id)),
                p.Notes, p.VoucherNo))
            .ToList();
    }

    /// <summary>
    /// Invoices (for money in) or supplier bills (for money out) that still
    /// have something owing, oldest first. Cancelled documents are excluded -
    /// there is nothing left to settle on a voided bill.
    /// </summary>
    public IReadOnlyList<OpenDocumentRow> OpenDocuments(int partyId, PaymentDirection direction)
    {
        if (direction == PaymentDirection.In)
        {
            var sales = _db.Sales.AsNoTracking()
                .Where(s => s.CustomerId == partyId && !s.IsCancelled)
                .Select(s => new { s.Id, s.InvoiceNo, s.Date, s.Total })
                .ToList();
            if (sales.Count == 0) return Array.Empty<OpenDocumentRow>();

            var saleIds = sales.Select(s => s.Id).ToList();
            var paid = _db.PaymentAllocations.AsNoTracking()
                .Where(a => a.SaleId != null && saleIds.Contains(a.SaleId!.Value))
                .Select(a => new { SaleId = a.SaleId!.Value, a.Amount })
                .ToList()
                .GroupBy(a => a.SaleId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            return sales
                .Select(s => new OpenDocumentRow(s.Id, s.InvoiceNo, s.Date,
                    Money.Round(s.Total), Money.Round(paid.GetValueOrDefault(s.Id))))
                .Where(r => r.Outstanding > 0)
                .OrderBy(r => r.Date).ThenBy(r => r.DocumentId)
                .ToList();
        }

        var purchases = _db.Purchases.AsNoTracking()
            .Where(p => p.SupplierId == partyId && !p.IsCancelled)
            .Select(p => new { p.Id, p.SupplierBillNo, p.Date, p.Total })
            .ToList();
        if (purchases.Count == 0) return Array.Empty<OpenDocumentRow>();

        var purchaseIds = purchases.Select(p => p.Id).ToList();
        var settled = _db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PurchaseId != null && purchaseIds.Contains(a.PurchaseId!.Value))
            .Select(a => new { PurchaseId = a.PurchaseId!.Value, a.Amount })
            .ToList()
            .GroupBy(a => a.PurchaseId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        return purchases
            .Select(p => new OpenDocumentRow(p.Id,
                string.IsNullOrWhiteSpace(p.SupplierBillNo) ? $"Bill #{p.Id}" : p.SupplierBillNo!,
                p.Date, Money.Round(p.Total), Money.Round(settled.GetValueOrDefault(p.Id))))
            .Where(r => r.Outstanding > 0)
            .OrderBy(r => r.Date).ThenBy(r => r.DocumentId)
            .ToList();
    }

    /// <summary>
    /// Records the payment and spreads it across open documents oldest first,
    /// all in one transaction. A payment that lands without its allocations
    /// would move the balance while leaving every invoice looking unpaid.
    /// </summary>
    public Payment Create(PaymentInput input)
    {
        if (input.Amount <= 0)
            throw new InvalidOperationException("Enter an amount greater than zero.");

        var party = _db.Parties.Find(input.PartyId)
            ?? throw new InvalidOperationException("Party not found.");

        using var tx = _db.Database.BeginTransaction();

        // His own number for the voucher, taken under the same transaction as
        // the payment. Two vouchers with the same number would be impossible
        // to tell apart on the phone.
        var settings = _db.Settings.Single(x => x.Id == 1);
        var incoming = input.Direction == PaymentDirection.In;

        var prefix = incoming ? settings.PaymentInPrefix : settings.PaymentOutPrefix;
        var next = incoming ? settings.PaymentInNextNumber : settings.PaymentOutNextNumber;
        var pad = Math.Clamp(settings.PaymentNumberPadding, 1, 8);

        var voucherNo = $"{prefix}{next.ToString().PadLeft(pad, '0')}";

        if (incoming) settings.PaymentInNextNumber = next + 1;
        else settings.PaymentOutNextNumber = next + 1;

        var payment = new Payment
        {
            VoucherNo = voucherNo,
            PartyId = input.PartyId,
            Direction = input.Direction,
            Mode = input.Mode,
            Amount = Money.Round(input.Amount),
            Date = input.Date.Date,
            ReferenceNo = string.IsNullOrWhiteSpace(input.ReferenceNo)
                ? null : input.ReferenceNo.Trim(),
            ChequeDueDate = input.ChequeDueDate,
            Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()
        };

        var remaining = payment.Amount;

        foreach (var doc in OpenDocuments(input.PartyId, input.Direction))
        {
            if (remaining <= 0) break;

            var take = Math.Min(remaining, doc.Outstanding);
            if (take <= 0) continue;

            payment.Allocations.Add(new PaymentAllocation
            {
                SaleId = input.Direction == PaymentDirection.In ? doc.DocumentId : null,
                PurchaseId = input.Direction == PaymentDirection.Out ? doc.DocumentId : null,
                Amount = Money.Round(take)
            });

            remaining = Money.Round(remaining - take);
        }

        _db.Payments.Add(payment);
        _db.SaveChanges();

        tx.Commit();
        _db.ChangeTracker.Clear();

        return payment;
    }

    /// <summary>
    /// Removes a payment and its allocations. Unlike a sale, a payment has no
    /// stock consequences and nothing is printed for the customer, so there is
    /// nothing to preserve by keeping a voided row around.
    /// </summary>
    public void Delete(int paymentId)
    {
        var payment = _db.Payments
            .Include(p => p.Allocations)
            .FirstOrDefault(p => p.Id == paymentId);
        if (payment is null) return;

        using var tx = _db.Database.BeginTransaction();

        _db.PaymentAllocations.RemoveRange(payment.Allocations);
        _db.Payments.Remove(payment);
        _db.SaveChanges();

        tx.Commit();
        _db.ChangeTracker.Clear();
    }

    /// <summary>What the party still owes overall, for the dialog header.</summary>
    public decimal OutstandingFor(int partyId, PaymentDirection direction) =>
        Money.Round(OpenDocuments(partyId, direction).Sum(d => d.Outstanding));
}
