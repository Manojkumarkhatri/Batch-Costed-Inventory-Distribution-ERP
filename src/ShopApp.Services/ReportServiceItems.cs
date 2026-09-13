using Microsoft.EntityFrameworkCore;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

public partial class ReportService
{
    // ----------------------------------------------------------- party reports

    /// <summary>One party's ledger with a running balance. His most-used report.</summary>
    public ReportResult PartyStatement(ReportFilter f)
    {
        if (f.PartyId is not int partyId)
            return ReportResult.Empty("Party Statement", "Choose a party first.");

        var party = _db.Parties.AsNoTracking().FirstOrDefault(p => p.Id == partyId);
        if (party is null) return ReportResult.Empty("Party Statement", "Party not found.");

        var rows = new List<ReportRow>();

        // Opening balance carried in as the first line.
        var opening = Row(("Date", party.OpeningBalanceDate), ("Type", "Opening Balance"),
            ("Ref", ""), ("Debit", party.OpeningIsReceivable ? party.OpeningBalance : 0m),
            ("Credit", party.OpeningIsReceivable ? 0m : party.OpeningBalance));
        opening.IsSummaryRow = true;
        rows.Add(opening);

        foreach (var s in _db.Sales.AsNoTracking()
                     .Where(s => s.CustomerId == partyId && !s.IsCancelled
                              && s.Date >= f.From.Date && s.Date <= f.To.Date).ToList())
            rows.Add(Row(("Date", s.Date), ("Type", "Sale"), ("Ref", s.InvoiceNo),
                ("Debit", s.Total), ("Credit", 0m)));

        foreach (var p in _db.Purchases.AsNoTracking()
                     .Where(p => p.SupplierId == partyId && !p.IsCancelled
                              && p.Date >= f.From.Date && p.Date <= f.To.Date).ToList())
            rows.Add(Row(("Date", p.Date), ("Type", "Purchase"),
                ("Ref", p.SupplierBillNo ?? $"#{p.Id}"),
                ("Debit", 0m), ("Credit", p.Total)));

        foreach (var pay in _db.Payments.AsNoTracking()
                     .Where(p => p.PartyId == partyId
                              && p.Date >= f.From.Date && p.Date <= f.To.Date).ToList())
            rows.Add(Row(("Date", pay.Date),
                ("Type", pay.Direction == PaymentDirection.In ? "Payment Received" : "Payment Made"),
                ("Ref", pay.ReferenceNo ?? pay.Mode.ToString()),
                ("Debit", pay.Direction == PaymentDirection.Out ? pay.Amount : 0m),
                ("Credit", pay.Direction == PaymentDirection.In ? pay.Amount : 0m)));

        var ordered = rows.OrderBy(r => (DateTime)r["Date"]!).ToList();

        decimal balance = 0m;
        foreach (var r in ordered)
        {
            balance += r.Num("Debit") - r.Num("Credit");
            r["Balance"] = Money.Round(balance);
        }

        var closing = Money.Round(balance);

        return new ReportResult($"Party Statement - {party.Name}", new[]
        {
            new ReportColumn("Date", "Date", ReportColumnType.Date, Width: 0.8),
            new ReportColumn("Type", "Type", ReportColumnType.Text, Width: 1.3),
            new ReportColumn("Reference", "Ref", ReportColumnType.Text),
            new ReportColumn("Debit", "Debit", ReportColumnType.Money, true),
            new ReportColumn("Credit", "Credit", ReportColumnType.Money, true),
            new ReportColumn("Balance", "Balance", ReportColumnType.Money),
        }, ordered, Totals(ordered, "Debit", "Credit"), f.RangeText,
        new[]
        {
            ("Closing balance", InvoiceFormatting.Rs(Math.Abs(closing), false, true)),
            ("", closing > 0 ? "To Receive" : closing < 0 ? "To Pay" : "Settled"),
        });
    }

    public ReportResult PartyWiseProfit(ReportFilter f)
    {
        var sales = _db.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Lines).ThenInclude(l => l.Item)
            .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled)
            .ToList();

        var rows = sales
            .GroupBy(s => new { s.CustomerId, Name = s.Customer?.Name ?? "" })
            .Select(g =>
            {
                decimal revenue = 0m, cost = 0m;
                foreach (var s in g)
                {
                    var lp = ProfitCalculations.ForInvoice(
                        s.Lines.Select(l => new ProfitLine(
                            l.ItemId, l.Item?.Name ?? "", l.Qty, l.Rate, l.CostAtSale)).ToList(),
                        s.Discount);
                    revenue += lp.Sum(x => x.Revenue);
                    cost += lp.Sum(x => x.Cost);
                }
                revenue = Money.Round(revenue);
                cost = Money.Round(cost);
                var profit = Money.Round(revenue - cost);

                return Row(("Party", g.Key.Name), ("Bills", g.Count()),
                    ("Revenue", revenue), ("Cost", cost), ("Profit", profit),
                    ("Margin", ProfitCalculations.MarginPercent(revenue, profit)));
            })
            .OrderByDescending(r => r.Num("Profit"))
            .ToList();

        return new ReportResult("Party wise Profit & Loss", new[]
        {
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 2.5),
            new ReportColumn("Bills", "Bills", ReportColumnType.Number, true, 0.6),
            new ReportColumn("Revenue", "Revenue", ReportColumnType.Money, true),
            new ReportColumn("Cost", "Cost", ReportColumnType.Money, true),
            new ReportColumn("Profit", "Profit", ReportColumnType.Money, true),
            new ReportColumn("Margin %", "Margin", ReportColumnType.Percent),
        }, rows, Totals(rows, "Bills", "Revenue", "Cost", "Profit"), f.RangeText);
    }

    public ReportResult AllParties(ReportFilter f)
    {
        var parties = _parties.Search(null, f.PartyGroup, includeInactive: false);

        var rows = parties.Select(p => Row(
            ("Party", p.Name), ("Group", p.GroupName ?? ""), ("Phone", p.Phone ?? ""),
            ("Receivable", p.Balance > 0 ? p.Balance : 0m),
            ("Payable", p.Balance < 0 ? -p.Balance : 0m),
            ("CreditLimit", p.CreditLimit))).ToList();

        return new ReportResult("All parties", new[]
        {
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 2.2),
            new ReportColumn("Group", "Group", ReportColumnType.Text),
            new ReportColumn("Phone", "Phone", ReportColumnType.Text),
            new ReportColumn("To Receive", "Receivable", ReportColumnType.Money, true),
            new ReportColumn("To Pay", "Payable", ReportColumnType.Money, true),
            new ReportColumn("Credit Limit", "CreditLimit", ReportColumnType.Money),
        }, rows, Totals(rows, "Receivable", "Payable"), "As at today");
    }

    public ReportResult PartyReportByItem(ReportFilter f)
    {
        if (f.PartyId is not int partyId)
            return ReportResult.Empty("Party Report By Item", "Choose a party first.");

        var lines = _db.SaleLines.AsNoTracking()
            .Include(l => l.Item).Include(l => l.Sale)
            .Where(l => l.Sale!.CustomerId == partyId && !l.Sale.IsCancelled
                     && l.Sale.Date >= f.From.Date && l.Sale.Date <= f.To.Date)
            .ToList();

        var rows = lines
            .GroupBy(l => new { l.ItemId, Name = l.Item?.Name ?? "", Unit = l.Item?.BaseUnit ?? "" })
            .Select(g =>
            {
                var qty = Money.RoundQty(g.Sum(x => x.Qty));
                var revenue = Money.Round(g.Sum(x => x.Amount));
                var cost = Money.Round(g.Sum(x => x.Qty * x.CostAtSale));
                return Row(("Item", g.Key.Name), ("Unit", g.Key.Unit), ("Qty", qty),
                    ("Revenue", revenue), ("Cost", cost),
                    ("Profit", Money.Round(revenue - cost)));
            })
            .OrderByDescending(r => r.Num("Revenue")).ToList();

        var partyName = _db.Parties.AsNoTracking()
            .Where(p => p.Id == partyId).Select(p => p.Name).FirstOrDefault() ?? "";

        return new ReportResult($"Party Report By Item - {partyName}", new[]
        {
            new ReportColumn("Item", "Item", ReportColumnType.Text, Width: 2.5),
            new ReportColumn("Unit", "Unit", ReportColumnType.Text, Width: 0.6),
            new ReportColumn("Quantity", "Qty", ReportColumnType.Quantity, true),
            new ReportColumn("Revenue", "Revenue", ReportColumnType.Money, true),
            new ReportColumn("Cost", "Cost", ReportColumnType.Money, true),
            new ReportColumn("Profit", "Profit", ReportColumnType.Money, true),
        }, rows, Totals(rows, "Qty", "Revenue", "Cost", "Profit"), f.RangeText);
    }

    public ReportResult SalePurchaseByParty(ReportFilter f)
    {
        var sales = _db.Sales.AsNoTracking().Include(s => s.Customer)
            .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled)
            .Select(s => new { s.CustomerId, Name = s.Customer!.Name, s.Total }).ToList();

        var purchases = _db.Purchases.AsNoTracking().Include(p => p.Supplier)
            .Where(p => p.Date >= f.From.Date && p.Date <= f.To.Date && !p.IsCancelled)
            .Select(p => new { p.SupplierId, Name = p.Supplier!.Name, p.Total }).ToList();

        var names = sales.Select(s => (s.CustomerId, s.Name))
            .Concat(purchases.Select(p => (p.SupplierId, p.Name)))
            .Distinct().ToList();

        var rows = names.Select(n => Row(
            ("Party", n.Name),
            ("SaleAmount", Money.Round(sales.Where(s => s.CustomerId == n.Item1).Sum(s => s.Total))),
            ("SaleCount", sales.Count(s => s.CustomerId == n.Item1)),
            ("PurchaseAmount", Money.Round(purchases.Where(p => p.SupplierId == n.Item1).Sum(p => p.Total))),
            ("PurchaseCount", purchases.Count(p => p.SupplierId == n.Item1))))
            .OrderByDescending(r => r.Num("SaleAmount") + r.Num("PurchaseAmount"))
            .ToList();

        return new ReportResult("Sale Purchase By Party", new[]
        {
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 2.5),
            new ReportColumn("Sale Bills", "SaleCount", ReportColumnType.Number, true, 0.7),
            new ReportColumn("Sale Amount", "SaleAmount", ReportColumnType.Money, true),
            new ReportColumn("Purchase Bills", "PurchaseCount", ReportColumnType.Number, true, 0.7),
            new ReportColumn("Purchase Amount", "PurchaseAmount", ReportColumnType.Money, true),
        }, rows, Totals(rows, "SaleCount", "SaleAmount", "PurchaseCount", "PurchaseAmount"),
        f.RangeText);
    }

    public ReportResult SalePurchaseByPartyGroup(ReportFilter f)
    {
        var groups = _db.Parties.AsNoTracking()
            .Select(p => new { p.Id, Group = p.GroupName ?? "(no group)" }).ToList()
            .ToDictionary(x => x.Id, x => x.Group);

        var sales = _db.Sales.AsNoTracking()
            .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled)
            .Select(s => new { s.CustomerId, s.Total }).ToList();

        var purchases = _db.Purchases.AsNoTracking()
            .Where(p => p.Date >= f.From.Date && p.Date <= f.To.Date && !p.IsCancelled)
            .Select(p => new { p.SupplierId, p.Total }).ToList();

        var allGroups = groups.Values.Distinct().OrderBy(g => g).ToList();

        var rows = allGroups.Select(g => Row(
            ("Group", g),
            ("SaleAmount", Money.Round(sales
                .Where(s => groups.GetValueOrDefault(s.CustomerId) == g).Sum(s => s.Total))),
            ("PurchaseAmount", Money.Round(purchases
                .Where(p => groups.GetValueOrDefault(p.SupplierId) == g).Sum(p => p.Total)))))
            .Where(r => r.Num("SaleAmount") != 0m || r.Num("PurchaseAmount") != 0m)
            .OrderByDescending(r => r.Num("SaleAmount"))
            .ToList();

        return new ReportResult("Sale Purchase By Party Group", new[]
        {
            new ReportColumn("Group", "Group", ReportColumnType.Text, Width: 2.5),
            new ReportColumn("Sale Amount", "SaleAmount", ReportColumnType.Money, true),
            new ReportColumn("Purchase Amount", "PurchaseAmount", ReportColumnType.Money, true),
        }, rows, Totals(rows, "SaleAmount", "PurchaseAmount"), f.RangeText);
    }

    // ------------------------------------------------------ item / stock reports

    public ReportResult StockSummaryReport(ReportFilter f)
    {
        var stock = _stock.StockSummary(f.Category);

        var rows = stock.Select(s => Row(
            ("Item", s.ItemName), ("Category", s.Category.ToString()),
            ("OnHand", s.OnHand), ("Unit", s.BaseUnit),
            ("ReorderLevel", s.ReorderLevel), ("Value", s.StockValue))).ToList();

        return new ReportResult("Stock summary", new[]
        {
            new ReportColumn("Item", "Item", ReportColumnType.Text, Width: 2.5),
            new ReportColumn("Category", "Category", ReportColumnType.Text),
            new ReportColumn("On Hand", "OnHand", ReportColumnType.Quantity, true),
            new ReportColumn("Unit", "Unit", ReportColumnType.Text, Width: 0.6),
            new ReportColumn("Reorder At", "ReorderLevel", ReportColumnType.Quantity),
            new ReportColumn("Stock Value", "Value", ReportColumnType.Money, true),
        }, rows, Totals(rows, "Value"), "As at today");
    }

    public ReportResult ItemReportByParty(ReportFilter f)
    {
        if (f.ItemId is not int itemId)
            return ReportResult.Empty("Item Report By Party", "Choose an item first.");

        var lines = _db.SaleLines.AsNoTracking()
            .Include(l => l.Sale).ThenInclude(s => s!.Customer)
            .Where(l => l.ItemId == itemId && !l.Sale!.IsCancelled
                     && l.Sale.Date >= f.From.Date && l.Sale.Date <= f.To.Date)
            .ToList();

        var rows = lines
            .GroupBy(l => l.Sale!.Customer?.Name ?? "")
            .Select(g => Row(("Party", g.Key),
                ("Qty", Money.RoundQty(g.Sum(x => x.Qty))),
                ("Revenue", Money.Round(g.Sum(x => x.Amount))),
                ("Bills", g.Select(x => x.SaleId).Distinct().Count())))
            .OrderByDescending(r => r.Num("Qty")).ToList();

        var itemName = _db.Items.AsNoTracking()
            .Where(i => i.Id == itemId).Select(i => i.Name).FirstOrDefault() ?? "";

        return new ReportResult($"Item Report By Party - {itemName}", new[]
        {
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 2.5),
            new ReportColumn("Bills", "Bills", ReportColumnType.Number, true, 0.6),
            new ReportColumn("Quantity", "Qty", ReportColumnType.Quantity, true),
            new ReportColumn("Revenue", "Revenue", ReportColumnType.Money, true),
        }, rows, Totals(rows, "Bills", "Qty", "Revenue"), f.RangeText);
    }

    public ReportResult ItemWiseProfit(ReportFilter f)
    {
        var sales = _db.Sales.AsNoTracking()
            .Include(s => s.Lines).ThenInclude(l => l.Item)
            .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled)
            .ToList();

        var allLines = new List<LineProfit>();
        foreach (var s in sales)
            allLines.AddRange(ProfitCalculations.ForInvoice(
                s.Lines.Select(l => new ProfitLine(
                    l.ItemId, l.Item?.Name ?? "", l.Qty, l.Rate, l.CostAtSale)).ToList(),
                s.Discount));

        var units = _db.Items.AsNoTracking()
            .Select(i => new { i.Id, i.BaseUnit }).ToList()
            .ToDictionary(x => x.Id, x => x.BaseUnit);

        var rows = ProfitCalculations.GroupByItem(allLines).Select(g => Row(
            ("Item", g.ItemName), ("Qty", g.Qty),
            ("Unit", units.GetValueOrDefault(g.ItemId, "")),
            ("Revenue", g.Revenue), ("Cost", g.Cost), ("Profit", g.Profit),
            ("Margin", g.MarginPercent))).ToList();

        return new ReportResult("Item Wise Profit And Loss", new[]
        {
            new ReportColumn("Item", "Item", ReportColumnType.Text, Width: 2.5),
            new ReportColumn("Quantity", "Qty", ReportColumnType.Quantity, true),
            new ReportColumn("Unit", "Unit", ReportColumnType.Text, Width: 0.6),
            new ReportColumn("Revenue", "Revenue", ReportColumnType.Money, true),
            new ReportColumn("Cost", "Cost", ReportColumnType.Money, true),
            new ReportColumn("Profit", "Profit", ReportColumnType.Money, true),
            new ReportColumn("Margin %", "Margin", ReportColumnType.Percent),
        }, rows, Totals(rows, "Qty", "Revenue", "Cost", "Profit"), f.RangeText);
    }

    public ReportResult CategoryWiseProfit(ReportFilter f)
    {
        var sales = _db.Sales.AsNoTracking()
            .Include(s => s.Lines).ThenInclude(l => l.Item)
            .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled)
            .ToList();

        var categories = _db.Items.AsNoTracking()
            .Select(i => new { i.Id, i.Category }).ToList()
            .ToDictionary(x => x.Id, x => x.Category);

        var allLines = new List<(ItemCategory Category, LineProfit Profit)>();
        foreach (var s in sales)
            foreach (var lp in ProfitCalculations.ForInvoice(
                         s.Lines.Select(l => new ProfitLine(
                             l.ItemId, l.Item?.Name ?? "", l.Qty, l.Rate, l.CostAtSale)).ToList(),
                         s.Discount))
                allLines.Add((categories.GetValueOrDefault(lp.ItemId, ItemCategory.Solid), lp));

        var rows = allLines.GroupBy(x => x.Category).Select(g =>
        {
            var revenue = Money.Round(g.Sum(x => x.Profit.Revenue));
            var cost = Money.Round(g.Sum(x => x.Profit.Cost));
            var profit = Money.Round(revenue - cost);
            return Row(("Category", g.Key.ToString()),
                ("Revenue", revenue), ("Cost", cost), ("Profit", profit),
                ("Margin", ProfitCalculations.MarginPercent(revenue, profit)));
        }).OrderByDescending(r => r.Num("Profit")).ToList();

        return new ReportResult("Item Category Wise Profit And Loss", new[]
        {
            new ReportColumn("Category", "Category", ReportColumnType.Text, Width: 1.5),
            new ReportColumn("Revenue", "Revenue", ReportColumnType.Money, true),
            new ReportColumn("Cost", "Cost", ReportColumnType.Money, true),
            new ReportColumn("Profit", "Profit", ReportColumnType.Money, true),
            new ReportColumn("Margin %", "Margin", ReportColumnType.Percent),
        }, rows, Totals(rows, "Revenue", "Cost", "Profit"), f.RangeText);
    }

    public ReportResult LowStockReport(ReportFilter f)
    {
        var rows = _stock.LowStock().Select(s => Row(
            ("Item", s.ItemName), ("Category", s.Category.ToString()),
            ("OnHand", s.OnHand), ("Unit", s.BaseUnit),
            ("ReorderLevel", s.ReorderLevel),
            ("Shortfall", Money.RoundQty(s.ReorderLevel - s.OnHand)))).ToList();

        return new ReportResult("Low Stock Summary", new[]
        {
            new ReportColumn("Item", "Item", ReportColumnType.Text, Width: 2.5),
            new ReportColumn("Category", "Category", ReportColumnType.Text),
            new ReportColumn("On Hand", "OnHand", ReportColumnType.Quantity),
            new ReportColumn("Unit", "Unit", ReportColumnType.Text, Width: 0.6),
            new ReportColumn("Reorder At", "ReorderLevel", ReportColumnType.Quantity),
            new ReportColumn("Short By", "Shortfall", ReportColumnType.Quantity),
        }, rows, new Dictionary<string, decimal>(),
        rows.Count == 0 ? "Nothing is below its reorder level." : "As at today");
    }

    public ReportResult StockDetail(ReportFilter f)
    {
        var q = _db.StockMoves.AsNoTracking()
            .Include(m => m.Batch).ThenInclude(b => b!.Item)
            .Where(m => m.MovedAt >= f.From.Date && m.MovedAt < f.To.Date.AddDays(1));

        if (f.ItemId is int itemId) q = q.Where(m => m.Batch!.ItemId == itemId);
        if (f.Category is ItemCategory c) q = q.Where(m => m.Batch!.Item!.Category == c);

        var moves = q.OrderBy(m => m.MovedAt).ThenBy(m => m.Id).ToList();

        var rows = moves.Select(m => Row(
            ("Date", m.MovedAt), ("Item", m.Batch?.Item?.Name ?? ""),
            ("Batch", m.Batch?.BatchNo ?? ""), ("Type", m.MoveType.ToString()),
            ("In", m.Qty > 0 ? m.Qty : 0m), ("Out", m.Qty < 0 ? -m.Qty : 0m),
            ("Reason", m.Reason?.ToString() ?? m.ReasonNote ?? ""))).ToList();

        return new ReportResult("Stock Detail", new[]
        {
            new ReportColumn("Date", "Date", ReportColumnType.Date, Width: 0.9),
            new ReportColumn("Item", "Item", ReportColumnType.Text, Width: 2),
            new ReportColumn("Batch", "Batch", ReportColumnType.Text),
            new ReportColumn("Type", "Type", ReportColumnType.Text),
            new ReportColumn("In", "In", ReportColumnType.Quantity, true),
            new ReportColumn("Out", "Out", ReportColumnType.Quantity, true),
            new ReportColumn("Reason", "Reason", ReportColumnType.Text, Width: 1.5),
        }, rows, Totals(rows, "In", "Out"), f.RangeText);
    }

    public ReportResult ItemDetail(ReportFilter f)
    {
        if (f.ItemId is not int itemId)
            return ReportResult.Empty("Item Detail", "Choose an item first.");

        var result = StockDetail(f with { ItemId = itemId });
        var itemName = _db.Items.AsNoTracking()
            .Where(i => i.Id == itemId).Select(i => i.Name).FirstOrDefault() ?? "";

        return result with { Title = $"Item Detail - {itemName}" };
    }

    public ReportResult SalePurchaseByCategory(ReportFilter f)
    {
        var categories = _db.Items.AsNoTracking()
            .Select(i => new { i.Id, i.Category }).ToList()
            .ToDictionary(x => x.Id, x => x.Category);

        var saleLines = _db.SaleLines.AsNoTracking().Include(l => l.Sale)
            .Where(l => !l.Sale!.IsCancelled
                     && l.Sale.Date >= f.From.Date && l.Sale.Date <= f.To.Date)
            .Select(l => new { l.ItemId, l.Qty, l.Amount }).ToList();

        var purchaseLines = _db.PurchaseLines.AsNoTracking().Include(l => l.Purchase)
            .Where(l => !l.Purchase!.IsCancelled
                     && l.Purchase.Date >= f.From.Date && l.Purchase.Date <= f.To.Date)
            .Select(l => new { l.ItemId, l.Qty, l.Amount }).ToList();

        var rows = Enum.GetValues<ItemCategory>().Select(cat =>
        {
            var s = saleLines.Where(l => categories.GetValueOrDefault(l.ItemId) == cat).ToList();
            var p = purchaseLines.Where(l => categories.GetValueOrDefault(l.ItemId) == cat).ToList();
            return Row(("Category", cat.ToString()),
                ("SaleQty", Money.RoundQty(s.Sum(x => x.Qty))),
                ("SaleAmount", Money.Round(s.Sum(x => x.Amount))),
                ("PurchaseQty", Money.RoundQty(p.Sum(x => x.Qty))),
                ("PurchaseAmount", Money.Round(p.Sum(x => x.Amount))));
        })
        .Where(r => r.Num("SaleAmount") != 0m || r.Num("PurchaseAmount") != 0m)
        .ToList();

        return new ReportResult("Sale/Purchase Report By Item Category", new[]
        {
            new ReportColumn("Category", "Category", ReportColumnType.Text, Width: 1.5),
            new ReportColumn("Sale Qty", "SaleQty", ReportColumnType.Quantity, true),
            new ReportColumn("Sale Amount", "SaleAmount", ReportColumnType.Money, true),
            new ReportColumn("Purchase Qty", "PurchaseQty", ReportColumnType.Quantity, true),
            new ReportColumn("Purchase Amount", "PurchaseAmount", ReportColumnType.Money, true),
        }, rows, Totals(rows, "SaleQty", "SaleAmount", "PurchaseQty", "PurchaseAmount"),
        f.RangeText);
    }

    public ReportResult StockByCategory(ReportFilter f)
    {
        var stock = _stock.StockSummary();

        var rows = stock.GroupBy(s => s.Category).Select(g => Row(
            ("Category", g.Key.ToString()),
            ("Items", g.Count(x => x.OnHand > 0)),
            ("Value", Money.Round(g.Sum(x => x.StockValue)))))
            .OrderByDescending(r => r.Num("Value")).ToList();

        return new ReportResult("Stock Summary Report By Item Category", new[]
        {
            new ReportColumn("Category", "Category", ReportColumnType.Text, Width: 1.5),
            new ReportColumn("Items In Stock", "Items", ReportColumnType.Number, true),
            new ReportColumn("Stock Value", "Value", ReportColumnType.Money, true),
        }, rows, Totals(rows, "Items", "Value"), "As at today");
    }

    public ReportResult NearExpiryReport(ReportFilter f)
    {
        // Days ahead comes from the filter's end date, so he can look further out.
        var days = Math.Max(1, (f.To.Date - DateTime.Today).Days);
        var rows = _stock.NearExpiry(days).Select(e => Row(
            ("Item", e.ItemName), ("Category", e.Category.ToString()),
            ("Batch", e.BatchNo), ("Expiry", e.ExpiryDate),
            ("DaysLeft", e.DaysLeft), ("OnHand", e.OnHand),
            ("Value", e.StockValue), ("Location", e.Location ?? ""))).ToList();

        return new ReportResult("Near Expiry Stock", new[]
        {
            new ReportColumn("Item", "Item", ReportColumnType.Text, Width: 2),
            new ReportColumn("Category", "Category", ReportColumnType.Text),
            new ReportColumn("Batch", "Batch", ReportColumnType.Text),
            new ReportColumn("Expiry", "Expiry", ReportColumnType.Date),
            new ReportColumn("Days Left", "DaysLeft", ReportColumnType.Number, Width: 0.7),
            new ReportColumn("On Hand", "OnHand", ReportColumnType.Quantity, true),
            new ReportColumn("Value At Risk", "Value", ReportColumnType.Money, true),
            new ReportColumn("Location", "Location", ReportColumnType.Text),
        }, rows, Totals(rows, "OnHand", "Value"),
        $"Expiring within {days} day(s)");
    }

    public ReportResult BatchWiseStock(ReportFilter f)
    {
        var batches = _db.Batches.AsNoTracking().Include(b => b.Item).ToList();
        var moves = _db.StockMoves.AsNoTracking()
            .Select(m => new { m.BatchId, m.Qty }).ToList()
            .GroupBy(m => m.BatchId)
            .ToDictionary(g => g.Key, g => Money.RoundQty(g.Sum(x => x.Qty)));

        var rows = batches
            .Where(b => f.ItemId is null || b.ItemId == f.ItemId)
            .Where(b => f.Category is null || b.Item?.Category == f.Category)
            .Select(b =>
            {
                var onHand = moves.GetValueOrDefault(b.Id);
                return Row(("Item", b.Item?.Name ?? ""), ("Batch", b.BatchNo),
                    ("Received", b.ReceivedAt), ("Expiry", b.ExpiryDate),
                    ("OnHand", onHand), ("Cost", b.CostPrice),
                    ("Value", Money.Round(onHand * b.CostPrice)),
                    ("Location", b.StorageLocation ?? ""));
            })
            .Where(r => r.Num("OnHand") != 0m)
            .OrderBy(r => (string?)r["Item"]).ToList();

        return new ReportResult("Batch Wise Stock", new[]
        {
            new ReportColumn("Item", "Item", ReportColumnType.Text, Width: 2),
            new ReportColumn("Batch", "Batch", ReportColumnType.Text),
            new ReportColumn("Received", "Received", ReportColumnType.Date),
            new ReportColumn("Expiry", "Expiry", ReportColumnType.Date),
            new ReportColumn("On Hand", "OnHand", ReportColumnType.Quantity, true),
            new ReportColumn("Cost/Unit", "Cost", ReportColumnType.Money),
            new ReportColumn("Value", "Value", ReportColumnType.Money, true),
            new ReportColumn("Location", "Location", ReportColumnType.Text),
        }, rows, Totals(rows, "OnHand", "Value"), "As at today");
    }

    /// <summary>
    /// Damage, spillage, thaw loss and count corrections. In food distribution
    /// this is a real cost line, and it is the report that shows whether the
    /// cold chain or the handling is losing him money.
    /// </summary>
    public ReportResult ShrinkageReport(ReportFilter f)
    {
        var moves = _db.StockMoves.AsNoTracking()
            .Include(m => m.Batch).ThenInclude(b => b!.Item)
            .Where(m => m.MoveType == StockMoveType.Adjustment
                     && m.MovedAt >= f.From.Date && m.MovedAt < f.To.Date.AddDays(1))
            .ToList();

        var rows = moves
            .GroupBy(m => new { Reason = m.Reason?.ToString() ?? "Other",
                                Item = m.Batch?.Item?.Name ?? "" })
            .Select(g =>
            {
                var qty = Money.RoundQty(g.Sum(x => x.Qty));
                var value = Money.Round(g.Sum(x => x.Qty * (x.Batch?.CostPrice ?? 0m)));
                return Row(("Reason", g.Key.Reason), ("Item", g.Key.Item),
                    ("Qty", qty), ("Value", value));
            })
            .OrderBy(r => r.Num("Value")).ToList();

        return new ReportResult("Shrinkage / Adjustments", new[]
        {
            new ReportColumn("Reason", "Reason", ReportColumnType.Text, Width: 1.5),
            new ReportColumn("Item", "Item", ReportColumnType.Text, Width: 2.5),
            new ReportColumn("Quantity", "Qty", ReportColumnType.Quantity, true),
            new ReportColumn("Value", "Value", ReportColumnType.Money, true),
        }, rows, Totals(rows, "Qty", "Value"), f.RangeText);
    }
}
