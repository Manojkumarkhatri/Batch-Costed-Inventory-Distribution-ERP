using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using ShopApp.Reports;

namespace ShopApp.Services;

/// <summary>
/// Turns a saved purchase or payment into the data a printed document needs.
///
/// Separate from the services that write them: printing is a read concern and
/// has no business sharing a class with the code that allocates stock or
/// moves money.
/// </summary>
public class DocumentBuilder
{
    private readonly AppDbContext _db;
    private readonly PurchaseService _purchases;
    private readonly PartyService _parties;

    public DocumentBuilder(AppDbContext db, PurchaseService purchases, PartyService parties)
    {
        _db = db;
        _purchases = purchases;
        _parties = parties;
    }

    public PurchaseBillData? BuildPurchaseBill(int purchaseId)
    {
        var p = _purchases.GetById(purchaseId);
        if (p is null) return null;

        var settings = _db.Settings.AsNoTracking().Single(s => s.Id == 1);

        var lines = p.Lines.Select((l, i) => new PurchaseLineData(
            Serial: i + 1,
            ItemName: l.Item?.Name ?? "",
            Qty: l.Qty,
            Unit: l.Item?.BaseUnit ?? "",
            Rate: l.Rate,
            Amount: Money.Round(l.Qty * l.Rate),
            // Batch and expiry are the two things a supplier's own invoice
            // leaves off and he cannot reconstruct later.
            BatchNo: l.Batch?.BatchNo,
            Expiry: l.Batch?.ExpiryDate)).ToList();

        var paid = _db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PurchaseId == purchaseId)
            .Select(a => a.Amount)
            .ToList()
            .Sum();

        return new PurchaseBillData(
            BillNo: string.IsNullOrWhiteSpace(p.SupplierBillNo) ? $"#{p.Id}" : p.SupplierBillNo!,
            Date: p.Date,
            SupplierName: p.Supplier?.Name ?? "",
            SupplierAddress: p.Supplier?.Address,
            Lines: lines,
            SubTotal: p.SubTotal,
            Charges: p.OtherCharges,
            Total: p.Total,
            Paid: Money.Round(paid),
            Balance: Money.Round(p.Total - paid),
            Description: p.Notes,
            BusinessName: settings.BusinessName,
            BusinessAddress: settings.Address,
            BusinessPhone: settings.Phone);
    }

    public PaymentVoucherData? BuildPaymentVoucher(int paymentId)
    {
        var pay = _db.Payments.AsNoTracking()
            .Include(p => p.Party)
            .Include(p => p.Allocations)
            .FirstOrDefault(p => p.Id == paymentId);

        if (pay is null) return null;

        var settings = _db.Settings.AsNoTracking().Single(s => s.Id == 1);
        var incoming = pay.Direction == PaymentDirection.In;

        // Which documents this money actually settled. Asked weeks later by
        // both sides, and remembered by neither.
        var settles = new List<(string Document, DateTime Date, decimal Amount)>();

        foreach (var a in pay.Allocations)
        {
            if (a.SaleId is int sid)
            {
                var sale = _db.Sales.AsNoTracking()
                    .Where(s => s.Id == sid)
                    .Select(s => new { s.InvoiceNo, s.Date })
                    .FirstOrDefault();
                if (sale is not null)
                    settles.Add((sale.InvoiceNo, sale.Date, a.Amount));
            }
            else if (a.PurchaseId is int pid)
            {
                var bill = _db.Purchases.AsNoTracking()
                    .Where(x => x.Id == pid)
                    .Select(x => new { x.SupplierBillNo, x.Date, x.Id })
                    .FirstOrDefault();
                if (bill is not null)
                    settles.Add((string.IsNullOrWhiteSpace(bill.SupplierBillNo)
                                     ? $"#{bill.Id}" : bill.SupplierBillNo!,
                                 bill.Date, a.Amount));
            }
        }

        return new PaymentVoucherData(
            VoucherNo: pay.VoucherNo ?? $"#{pay.Id}",
            Date: pay.Date,
            Incoming: incoming,
            PartyName: pay.Party?.Name ?? "",
            Amount: pay.Amount,
            Mode: pay.Mode.ToString(),
            ReferenceNo: pay.ReferenceNo,
            PartyBalanceAfter: _parties.BalanceOf(pay.PartyId),
            Settles: settles.OrderBy(s => s.Date).ToList(),
            Description: pay.Notes,
            BusinessName: settings.BusinessName,
            BusinessAddress: settings.Address,
            BusinessPhone: settings.Phone);
    }
}
