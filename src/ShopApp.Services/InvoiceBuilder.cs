using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Logic;
using ShopApp.Reports;

namespace ShopApp.Services;

/// <summary>
/// Turns a saved sale into the data the printed invoice needs.
///
/// Lines are grouped back to one row per item. A single item may have been
/// split across several batches by FEFO, but the customer must not see three
/// rows of the same product - his sample invoice shows one row per item.
/// </summary>
public class InvoiceBuilder
{
    private readonly AppDbContext _db;
    private readonly PartyService _parties;

    public InvoiceBuilder(AppDbContext db, PartyService parties)
    {
        _db = db;
        _parties = parties;
    }

    public (InvoiceData Data, InvoicePrintOptions Options)? Build(int saleId)
    {
        var sale = _db.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Lines).ThenInclude(l => l.Item)
            .FirstOrDefault(s => s.Id == saleId);

        if (sale is null) return null;

        var options = _db.InvoicePrintOptions.AsNoTracking()
            .FirstOrDefault(o => o.SaleId == saleId) ?? InvoicePrintOptions.Defaults();

        var settings = _db.Settings.AsNoTracking().Single(s => s.Id == 1);

        // Merge batch splits back into one line per item and rate.
        var grouped = sale.Lines
            .GroupBy(l => new { l.ItemId, l.Rate })
            .Select(g => new
            {
                Name = g.First().Item?.Name ?? "",
                Unit = g.First().Item?.BaseUnit ?? "",
                Qty = g.Sum(x => x.Qty),
                Rate = g.Key.Rate,
                Amount = g.Sum(x => x.Amount)
            })
            .OrderBy(x => x.Name)
            .ToList();

        var lines = grouped
            .Select((x, i) => new InvoiceLineData(
                i + 1, x.Name, Money.RoundQty(x.Qty), x.Unit,
                x.Rate, Money.Round(x.Amount)))
            .ToList();

        var received = _db.Payments.AsNoTracking()
            .Where(p => p.PartyId == sale.CustomerId)
            .SelectMany(p => p.Allocations)
            .Where(a => a.SaleId == saleId)
            .Select(a => a.Amount)
            .ToList()
            .Sum();

        var data = new InvoiceData(
            InvoiceNo: sale.InvoiceNo,
            Date: sale.Date,
            PartyName: sale.Customer?.Name ?? "",
            PartyAddress: sale.Customer?.Address,
            Lines: lines,
            SubTotal: sale.SubTotal,
            Total: sale.Total,
            Received: Money.Round(received),
            Balance: Money.Round(sale.Total - received),
            PartyCurrentBalance: _parties.BalanceOf(sale.CustomerId),
            BusinessName: settings.BusinessName,
            BusinessAddress: settings.Address,
            BusinessPhone: settings.Phone);

        return (data, options);
    }
}
