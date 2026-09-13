using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

/// <summary>
/// Which filters a report actually uses. The filter bar shows only these, so
/// a stock report is not asking for a date range it ignores and a party
/// statement is not sitting beside an item picker it never reads.
/// </summary>
[Flags]
public enum ReportFilters
{
    None = 0,
    DateRange = 1,
    Party = 2,
    Item = 4,
    Category = 8
}

/// <summary>
/// A report and the chrome it wants. Reports are not all the same shape: a
/// day book needs a period and a total, a stock summary is a snapshot of
/// today and needs neither.
/// </summary>
public record ReportDefinition(
    string Id,
    string Name,
    string Group,
    string Description,
    ReportFilters Filters = ReportFilters.DateRange | ReportFilters.Party | ReportFilters.Item,
    bool ShowTotals = false);

public partial class ReportService
{
    private readonly AppDbContext _db;
    private readonly PartyService _parties;
    private readonly StockService _stock;

    public ReportService(AppDbContext db, PartyService parties, StockService stock)
    {
        _db = db;
        _parties = parties;
        _stock = stock;
    }

    /// <summary>
    /// The catalogue shown in the left-hand list. Grouped the same way as the
    /// software he already uses, so he can find things where he expects them.
    /// </summary>
    public static IReadOnlyList<ReportDefinition> Catalogue { get; } = new List<ReportDefinition>
    {
        new("sale", "Sale", "Transaction report", "Every invoice in the period",
            ReportFilters.DateRange | ReportFilters.Party | ReportFilters.Item, ShowTotals: true),
        new("purchase", "Purchase", "Transaction report", "Every purchase bill in the period",
            ReportFilters.DateRange | ReportFilters.Party | ReportFilters.Item, ShowTotals: true),
        new("daybook", "Day book", "Transaction report", "Everything that happened, day by day",
            ReportFilters.DateRange | ReportFilters.Party | ReportFilters.Item, ShowTotals: true),
        new("alltxn", "All Transactions", "Transaction report", "Sales, purchases, payments and expenses together",
            ReportFilters.DateRange | ReportFilters.Party, ShowTotals: true),
        new("pl", "Profit And Loss", "Transaction report", "Revenue, cost of goods sold, expenses, net profit",
            ReportFilters.DateRange, ShowTotals: false),
        new("billprofit", "Bill Wise Profit", "Transaction report", "Profit on each individual invoice",
            ReportFilters.DateRange | ReportFilters.Party, ShowTotals: true),
        new("balancesheet", "Balance Sheet", "Transaction report",
            "What the business owns, what it owes, and the difference",
            ReportFilters.None, ShowTotals: false),
        new("cashflow", "Cash flow", "Transaction report", "Money in and money out",
            ReportFilters.DateRange, ShowTotals: true),
        new("expense", "Expenses", "Transaction report", "Running costs by category",
            ReportFilters.DateRange, ShowTotals: true),

        new("partystatement", "Party Statement", "Party report", "One party's full ledger",
            ReportFilters.DateRange | ReportFilters.Party, ShowTotals: false),
        new("partypl", "Party wise Profit & Loss", "Party report", "Which customers actually make money",
            ReportFilters.DateRange | ReportFilters.Party, ShowTotals: true),
        new("allparties", "All parties", "Party report", "Every party with their balance",
            ReportFilters.None, ShowTotals: true),
        new("partybyitem", "Party Report By Item", "Party report", "What one party bought, item by item",
            ReportFilters.DateRange | ReportFilters.Party | ReportFilters.Item, ShowTotals: true),
        new("salepurchaseparty", "Sale Purchase By Party", "Party report", "Sales and purchases totalled per party",
            ReportFilters.DateRange | ReportFilters.Party, ShowTotals: true),
        new("salepurchasegroup", "Sale Purchase By Party Group", "Party report", "The same, rolled up by group",
            ReportFilters.DateRange, ShowTotals: true),

        new("stocksummary", "Stock summary", "Item / Stock report", "What is on hand and what it is worth",
            ReportFilters.Category, ShowTotals: true),
        new("itembyparty", "Item Report By Party", "Item / Stock report", "Who bought one particular item",
            ReportFilters.DateRange | ReportFilters.Item, ShowTotals: true),
        new("itempl", "Item Wise Profit And Loss", "Item / Stock report", "Profit per product",
            ReportFilters.DateRange | ReportFilters.Item | ReportFilters.Category, ShowTotals: true),
        new("categorypl", "Item Category Wise Profit And Loss", "Item / Stock report", "Profit by Solid, Liquid, Frozen",
            ReportFilters.DateRange, ShowTotals: true),
        new("lowstock", "Low Stock Summary", "Item / Stock report", "What has fallen to its reorder level",
            ReportFilters.Category, ShowTotals: false),
        new("stockdetail", "Stock Detail", "Item / Stock report", "Every movement of every batch",
            ReportFilters.DateRange | ReportFilters.Item, ShowTotals: true),
        new("itemdetail", "Item Detail", "Item / Stock report", "Every transaction for one item",
            ReportFilters.DateRange | ReportFilters.Item, ShowTotals: true),
        new("salepurchasecategory", "Sale/Purchase Report By Item Category", "Item / Stock report", "Trade split by category",
            ReportFilters.DateRange, ShowTotals: true),
        new("stockbycategory", "Stock Summary Report By Item Category", "Item / Stock report", "Stock value by category",
            ReportFilters.Category, ShowTotals: true),
        new("nearexpiry", "Near Expiry Stock", "Item / Stock report", "What is about to go out of date",
            ReportFilters.Category, ShowTotals: true),
        new("batchstock", "Batch Wise Stock", "Item / Stock report", "On-hand by individual batch",
            ReportFilters.Item | ReportFilters.Category, ShowTotals: true),
        new("shrinkage", "Shrinkage / Adjustments", "Item / Stock report", "Damage, spillage and count corrections",
            ReportFilters.DateRange | ReportFilters.Item, ShowTotals: true),
    };

    // ---------------------------------------------------------------- helpers

    private static ReportRow Row(params (string Field, object? Value)[] values)
    {
        var r = new ReportRow();
        foreach (var (f, v) in values) r[f] = v;
        return r;
    }

    private static Dictionary<string, decimal> Totals(
        IEnumerable<ReportRow> rows, params string[] fields)
    {
        var list = rows.Where(r => !r.IsSummaryRow).ToList();
        return fields.ToDictionary(f => f, f => Money.Round(list.Sum(r => r.Num(f))));
    }

    public ReportResult Run(string reportId, ReportFilter filter) => reportId switch
    {
        "sale" => SaleReport(filter),
        "purchase" => PurchaseReport(filter),
        "daybook" => DayBook(filter),
        "alltxn" => AllTransactions(filter),
        "pl" => ProfitAndLoss(filter),
        "billprofit" => BillWiseProfit(filter),
        "cashflow" => CashFlow(filter),
        "expense" => ExpenseReport(filter),
        "partystatement" => PartyStatement(filter),
        "partypl" => PartyWiseProfit(filter),
        "allparties" => AllParties(filter),
        "partybyitem" => PartyReportByItem(filter),
        "salepurchaseparty" => SalePurchaseByParty(filter),
        "salepurchasegroup" => SalePurchaseByPartyGroup(filter),
        "stocksummary" => StockSummaryReport(filter),
        "itembyparty" => ItemReportByParty(filter),
        "itempl" => ItemWiseProfit(filter),
        "categorypl" => CategoryWiseProfit(filter),
        "lowstock" => LowStockReport(filter),
        "stockdetail" => StockDetail(filter),
        "itemdetail" => ItemDetail(filter),
        "salepurchasecategory" => SalePurchaseByCategory(filter),
        "stockbycategory" => StockByCategory(filter),
        "nearexpiry" => NearExpiryReport(filter),
        "batchstock" => BatchWiseStock(filter),
        "shrinkage" => ShrinkageReport(filter),
        "balancesheet" => BalanceSheet(filter),
        _ => ReportResult.Empty("Unknown report", $"No report with id '{reportId}'.")
    };

    // ------------------------------------------------------ transaction reports

    public ReportResult SaleReport(ReportFilter f)
    {
        var q = _db.Sales.AsNoTracking().Include(s => s.Customer)
            .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled);

        if (f.PartyId is int pid) q = q.Where(s => s.CustomerId == pid);

        var sales = q.OrderBy(s => s.Date).ThenBy(s => s.Id).ToList();

        var saleIds = sales.Select(s => s.Id).ToList();
        var received = _db.PaymentAllocations.AsNoTracking()
            .Where(a => a.SaleId != null && saleIds.Contains(a.SaleId!.Value))
            .Select(a => new { a.SaleId, a.Amount }).ToList()
            .GroupBy(a => a.SaleId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var rows = sales.Select(s =>
        {
            var paid = received.GetValueOrDefault(s.Id);
            return Row(
                ("Date", s.Date), ("InvoiceNo", s.InvoiceNo),
                ("Party", s.Customer?.Name ?? ""),
                ("SubTotal", s.SubTotal),
                ("Total", s.Total), ("Received", paid),
                ("Balance", Money.Round(s.Total - paid)));
        }).ToList();

        return new ReportResult("Sale", new[]
        {
            new ReportColumn("Date", "Date", ReportColumnType.Date, Width: 0.8),
            new ReportColumn("Invoice No.", "InvoiceNo", ReportColumnType.Text, Width: 0.7),
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 2),
            new ReportColumn("Sub Total", "SubTotal", ReportColumnType.Money, true),
            new ReportColumn("Total", "Total", ReportColumnType.Money, true),
            new ReportColumn("Received", "Received", ReportColumnType.Money, true),
            new ReportColumn("Balance", "Balance", ReportColumnType.Money, true),
        }, rows, Totals(rows, "SubTotal", "Total", "Received", "Balance"),
        f.RangeText);
    }

    public ReportResult PurchaseReport(ReportFilter f)
    {
        var q = _db.Purchases.AsNoTracking().Include(p => p.Supplier)
            .Where(p => p.Date >= f.From.Date && p.Date <= f.To.Date && !p.IsCancelled);

        if (f.PartyId is int pid) q = q.Where(p => p.SupplierId == pid);

        var purchases = q.OrderBy(p => p.Date).ThenBy(p => p.Id).ToList();

        var ids = purchases.Select(p => p.Id).ToList();
        var paid = _db.PaymentAllocations.AsNoTracking()
            .Where(a => a.PurchaseId != null && ids.Contains(a.PurchaseId!.Value))
            .Select(a => new { a.PurchaseId, a.Amount }).ToList()
            .GroupBy(a => a.PurchaseId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        var rows = purchases.Select(p =>
        {
            var pd = paid.GetValueOrDefault(p.Id);
            return Row(
                ("Date", p.Date), ("BillNo", p.SupplierBillNo ?? $"#{p.Id}"),
                ("Party", p.Supplier?.Name ?? ""),
                ("SubTotal", p.SubTotal), ("Charges", p.OtherCharges),
                ("Total", p.Total),
                ("Paid", pd), ("Balance", Money.Round(p.Total - pd)));
        }).ToList();

        return new ReportResult("Purchase", new[]
        {
            new ReportColumn("Date", "Date", ReportColumnType.Date, Width: 0.8),
            new ReportColumn("Bill No.", "BillNo", ReportColumnType.Text, Width: 0.8),
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 2),
            new ReportColumn("Sub Total", "SubTotal", ReportColumnType.Money, true),
            new ReportColumn("Freight", "Charges", ReportColumnType.Money, true),
            new ReportColumn("Total", "Total", ReportColumnType.Money, true),
            new ReportColumn("Paid", "Paid", ReportColumnType.Money, true),
            new ReportColumn("Balance", "Balance", ReportColumnType.Money, true),
        }, rows, Totals(rows, "SubTotal", "Charges", "Total", "Paid", "Balance"),
        f.RangeText);
    }

    /// <summary>
    /// The running diary, one line per item moved rather than one per document,
    /// split into blocks with a heading and a subtotal each: sales, purchases,
    /// money in, money out, then expenses. At the end of a day he reads it as
    /// "what did I sell, what did I buy, what came in" - not as one
    /// chronological stream where the four are tangled together.
    ///
    /// Headings and subtotals are marked as summary rows, so the grid, the
    /// Excel export and the PDF all bold them, and the grand total ignores
    /// them instead of counting every figure twice.
    /// </summary>
    public ReportResult DayBook(ReportFilter f)
    {
        var sales = new List<ReportRow>();
        var purchases = new List<ReportRow>();
        var moneyIn = new List<ReportRow>();
        var moneyOut = new List<ReportRow>();
        var expenses = new List<ReportRow>();

        foreach (var sale in _db.Sales.AsNoTracking()
                     .Include(s => s.Customer)
                     .Include(s => s.Lines).ThenInclude(l => l.Item)
                     .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled)
                     .Where(s => f.PartyId == null || s.CustomerId == f.PartyId)
                     .ToList())
        {
            // FEFO splits one invoice line across batches. That is a costing
            // detail; on the day book he wants the line he actually sold.
            foreach (var g in sale.Lines
                         .Where(l => f.ItemId is not int want || l.ItemId == want)
                         .GroupBy(l => new { l.ItemId, l.Rate }))
            {
                var qty = g.Sum(l => l.Qty);

                sales.Add(Row(("Date", sale.Date), ("Type", "Sale"), ("Ref", sale.InvoiceNo),
                    ("Party", sale.Customer?.Name ?? ""),
                    ("Item", g.First().Item?.Name ?? ""),
                    ("Qty", qty), ("Rate", g.Key.Rate),
                    ("In", 0m), ("Out", Money.Round(qty * g.Key.Rate))));
            }
        }

        foreach (var p in _db.Purchases.AsNoTracking()
                     .Include(p => p.Supplier)
                     .Include(p => p.Lines).ThenInclude(l => l.Item)
                     .Where(p => p.Date >= f.From.Date && p.Date <= f.To.Date && !p.IsCancelled)
                     .Where(p => f.PartyId == null || p.SupplierId == f.PartyId)
                     .ToList())
        {
            foreach (var g in p.Lines
                         .Where(l => f.ItemId is not int want || l.ItemId == want)
                         .GroupBy(l => new { l.ItemId, l.Rate }))
            {
                var qty = g.Sum(l => l.Qty);

                purchases.Add(Row(("Date", p.Date), ("Type", "Purchase"),
                    ("Ref", p.SupplierBillNo ?? $"#{p.Id}"),
                    ("Party", p.Supplier?.Name ?? ""),
                    ("Item", g.First().Item?.Name ?? ""),
                    ("Qty", qty), ("Rate", g.Key.Rate),
                    ("In", Money.Round(qty * g.Key.Rate)), ("Out", 0m)));
            }
        }

        // Money has no quantity or rate, so those cells stay blank rather than
        // carrying a nought that means nothing.
        foreach (var pay in _db.Payments.AsNoTracking().Include(p => p.Party)
                     .Where(p => p.Date >= f.From.Date && p.Date <= f.To.Date)
                     .Where(p => f.PartyId == null || p.PartyId == f.PartyId).ToList())
        {
            var incoming = pay.Direction == PaymentDirection.In;

            var row = Row(("Date", pay.Date),
                ("Type", incoming ? "Payment In" : "Payment Out"),
                ("Ref", pay.ReferenceNo ?? pay.Mode.ToString()),
                ("Party", pay.Party?.Name ?? ""),
                ("Item", ""), ("Qty", null), ("Rate", null),
                ("In", incoming ? pay.Amount : 0m),
                ("Out", incoming ? 0m : pay.Amount));

            (incoming ? moneyIn : moneyOut).Add(row);
        }

        // An expense belongs to no party, so filtering by one excludes them all.
        foreach (var e in _db.Expenses.AsNoTracking()
                     .Where(e => e.Date >= f.From.Date && e.Date <= f.To.Date)
                     .Where(e => f.PartyId == null).ToList())
            expenses.Add(Row(("Date", e.Date), ("Type", "Expense"), ("Ref", e.Category),
                ("Party", ""), ("Item", ""), ("Qty", null), ("Rate", null),
                ("In", 0m), ("Out", e.Amount)));

        var rows = new List<ReportRow>();

        AppendSection(rows, "SALES", sales);
        AppendSection(rows, "PURCHASES", purchases);
        AppendSection(rows, "PAYMENT IN", moneyIn);
        AppendSection(rows, "PAYMENT OUT", moneyOut);
        AppendSection(rows, "EXPENSES", expenses);

        return new ReportResult("Day book", new[]
        {
            new ReportColumn("Date", "Date", ReportColumnType.Date, Width: 0.9),
            new ReportColumn("Type", "Type", ReportColumnType.Text, Width: 0.9),
            new ReportColumn("Reference", "Ref", ReportColumnType.Text, Width: 0.9),
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 1.6),
            new ReportColumn("Item", "Item", ReportColumnType.Text, Width: 1.6),
            new ReportColumn("Qty", "Qty", ReportColumnType.Quantity),
            new ReportColumn("Rate", "Rate", ReportColumnType.Money),
            new ReportColumn("Value In", "In", ReportColumnType.Money, true),
            new ReportColumn("Value Out", "Out", ReportColumnType.Money, true),
        }, rows, Totals(rows, "In", "Out"), f.RangeText);
    }

    /// <summary>
    /// Adds one block of the day book: a heading, the entries in date order,
    /// and a subtotal. An empty block is skipped entirely - a heading over
    /// nothing just makes the report longer.
    /// </summary>
    private static void AppendSection(List<ReportRow> target, string heading,
                                      List<ReportRow> entries)
    {
        if (entries.Count == 0) return;

        // The heading is a group key, not a row. The grid draws it as a band
        // across the full width; a heading row would leave the word stranded
        // in one column with empty cells either side.
        foreach (var r in entries.OrderBy(r => (DateTime)r["Date"]!)
                                 .ThenBy(r => (string?)r["Ref"]))
        {
            r.Section = heading;
            r.SectionCount = entries.Count;
            target.Add(r);
        }

        // No subtotal row. A blank line with three figures in it, between
        // every block, broke the table into fragments and read worse than
        // the entries it was summarising. The band gives the section its
        // count; the footer under the table gives the overall total.
    }

    /// <summary>
    /// The same day, one line per document instead of per item. Kept separate
    /// from the day book on purpose: this one reconciles against invoice
    /// numbers and bank entries, where item detail only gets in the way.
    /// </summary>
    public ReportResult AllTransactions(ReportFilter f)
    {
        var rows = new List<ReportRow>();

        foreach (var s in _db.Sales.AsNoTracking().Include(s => s.Customer)
                     .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled).ToList())
            rows.Add(Row(("Date", s.Date), ("Type", "Sale"), ("Ref", s.InvoiceNo),
                ("Party", s.Customer?.Name ?? ""), ("In", 0m), ("Out", s.Total)));

        foreach (var p in _db.Purchases.AsNoTracking().Include(p => p.Supplier)
                     .Where(p => p.Date >= f.From.Date && p.Date <= f.To.Date && !p.IsCancelled).ToList())
            rows.Add(Row(("Date", p.Date), ("Type", "Purchase"), ("Ref", p.SupplierBillNo ?? $"#{p.Id}"),
                ("Party", p.Supplier?.Name ?? ""), ("In", p.Total), ("Out", 0m)));

        foreach (var pay in _db.Payments.AsNoTracking().Include(p => p.Party)
                     .Where(p => p.Date >= f.From.Date && p.Date <= f.To.Date).ToList())
            rows.Add(Row(("Date", pay.Date),
                ("Type", pay.Direction == PaymentDirection.In ? "Payment In" : "Payment Out"),
                ("Ref", pay.ReferenceNo ?? pay.Mode.ToString()),
                ("Party", pay.Party?.Name ?? ""),
                ("In", pay.Direction == PaymentDirection.In ? pay.Amount : 0m),
                ("Out", pay.Direction == PaymentDirection.Out ? pay.Amount : 0m)));

        foreach (var e in _db.Expenses.AsNoTracking()
                     .Where(e => e.Date >= f.From.Date && e.Date <= f.To.Date).ToList())
            rows.Add(Row(("Date", e.Date), ("Type", "Expense"), ("Ref", e.Category),
                ("Party", ""), ("In", 0m), ("Out", e.Amount)));

        var ordered = rows.OrderBy(r => (DateTime)r["Date"]!)
                          .ThenBy(r => (string?)r["Type"]).ToList();

        return new ReportResult("All Transactions", new[]
        {
            new ReportColumn("Date", "Date", ReportColumnType.Date, Width: 0.8),
            new ReportColumn("Type", "Type", ReportColumnType.Text, Width: 0.9),
            new ReportColumn("Reference", "Ref", ReportColumnType.Text),
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 2),
            new ReportColumn("Value In", "In", ReportColumnType.Money, true),
            new ReportColumn("Value Out", "Out", ReportColumnType.Money, true),
        }, ordered, Totals(ordered, "In", "Out"), f.RangeText);
    }

    public ReportResult ProfitAndLoss(ReportFilter f)
    {
        var sales = _db.Sales.AsNoTracking().Include(s => s.Lines)
            .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled)
            .ToList();

        var revenue = Money.Round(sales.Sum(s => s.Total));
        var cogs = Money.Round(sales.SelectMany(s => s.Lines).Sum(l => l.Qty * l.CostAtSale));

        // Expenses are charged over the days they cover, not on the day the
        // money left. Rent paid on the 1st buys the whole month, so a daily
        // Profit and Loss should show one day of it - not the lot.
        var expenses = ExpensesCharged(f.From, f.To);

        var expenseTotal = Money.Round(expenses.Sum(e => e.Amount));
        var pl = ProfitCalculations.ProfitAndLoss(revenue, cogs, expenseTotal);

        var rows = new List<ReportRow>
        {
            Row(("Line", "Sales"), ("Amount", pl.Revenue)),
            Row(("Line", "Less: Cost of goods sold"), ("Amount", -pl.Cogs)),
        };

        var gp = Row(("Line", "Gross Profit"), ("Amount", pl.GrossProfit));
        gp.IsSummaryRow = true;
        rows.Add(gp);

        foreach (var g in expenses.GroupBy(e => e.Category).OrderByDescending(g => g.Sum(x => x.Amount)))
            rows.Add(Row(("Line", $"Less: {g.Key}"), ("Amount", -Money.Round(g.Sum(x => x.Amount)))));

        var np = Row(("Line", "Net Profit"), ("Amount", pl.NetProfit));
        np.IsSummaryRow = true;
        rows.Add(np);

        return new ReportResult("Profit And Loss", new[]
        {
            new ReportColumn("", "Line", ReportColumnType.Text, Width: 3),
            new ReportColumn("Amount", "Amount", ReportColumnType.Money),
        }, rows, new Dictionary<string, decimal> { ["Amount"] = pl.NetProfit },
        f.RangeText,
        new[]
        {
            ("Sales", InvoiceFormatting.Rs(pl.Revenue, false, true)),
            ("Gross Profit", InvoiceFormatting.Rs(pl.GrossProfit, false, true)),
            ("Margin", $"{ProfitCalculations.MarginPercent(pl.Revenue, pl.GrossProfit):0.##}%"),
            ("Net Profit", InvoiceFormatting.Rs(pl.NetProfit, false, true)),
        });
    }

    public ReportResult BillWiseProfit(ReportFilter f)
    {
        var sales = _db.Sales.AsNoTracking()
            .Include(s => s.Customer)
            .Include(s => s.Lines).ThenInclude(l => l.Item)
            .Where(s => s.Date >= f.From.Date && s.Date <= f.To.Date && !s.IsCancelled)
            .OrderBy(s => s.Date).ToList();

        var rows = sales.Select(s =>
        {
            var profits = ProfitCalculations.ForInvoice(
                s.Lines.Select(l => new ProfitLine(
                    l.ItemId, l.Item?.Name ?? "", l.Qty, l.Rate, l.CostAtSale)).ToList(),
                s.Discount);

            var revenue = Money.Round(profits.Sum(x => x.Revenue));
            var cost = Money.Round(profits.Sum(x => x.Cost));
            var profit = Money.Round(revenue - cost);

            return Row(("Date", s.Date), ("InvoiceNo", s.InvoiceNo),
                ("Party", s.Customer?.Name ?? ""),
                ("Revenue", revenue), ("Cost", cost), ("Profit", profit),
                ("Margin", ProfitCalculations.MarginPercent(revenue, profit)));
        }).ToList();

        return new ReportResult("Bill Wise Profit", new[]
        {
            new ReportColumn("Date", "Date", ReportColumnType.Date, Width: 0.8),
            new ReportColumn("Invoice No.", "InvoiceNo", ReportColumnType.Text, Width: 0.7),
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 2),
            new ReportColumn("Revenue", "Revenue", ReportColumnType.Money, true),
            new ReportColumn("Cost", "Cost", ReportColumnType.Money, true),
            new ReportColumn("Profit", "Profit", ReportColumnType.Money, true),
            new ReportColumn("Margin %", "Margin", ReportColumnType.Percent),
        }, rows, Totals(rows, "Revenue", "Cost", "Profit"), f.RangeText);
    }

    public ReportResult CashFlow(ReportFilter f)
    {
        var rows = new List<ReportRow>();

        foreach (var p in _db.Payments.AsNoTracking().Include(p => p.Party)
                     .Where(p => p.Date >= f.From.Date && p.Date <= f.To.Date).ToList())
            rows.Add(Row(("Date", p.Date), ("Type", p.Mode.ToString()),
                ("Party", p.Party?.Name ?? ""),
                ("Description", p.Direction == PaymentDirection.In
                    ? "Received from party" : "Paid to party"),
                ("In", p.Direction == PaymentDirection.In ? p.Amount : 0m),
                ("Out", p.Direction == PaymentDirection.Out ? p.Amount : 0m)));

        foreach (var e in _db.Expenses.AsNoTracking()
                     .Where(e => e.Date >= f.From.Date && e.Date <= f.To.Date).ToList())
            rows.Add(Row(("Date", e.Date), ("Type", "Cash"), ("Party", ""),
                ("Description", $"Expense: {e.Category}"),
                ("In", 0m), ("Out", e.Amount)));

        var ordered = rows.OrderBy(r => (DateTime)r["Date"]!).ToList();

        decimal running = 0m;
        foreach (var r in ordered)
        {
            running += r.Num("In") - r.Num("Out");
            r["Running"] = Money.Round(running);
        }

        return new ReportResult("Cash flow", new[]
        {
            new ReportColumn("Date", "Date", ReportColumnType.Date, Width: 0.8),
            new ReportColumn("Mode", "Type", ReportColumnType.Text, Width: 0.7),
            new ReportColumn("Party", "Party", ReportColumnType.Text, Width: 1.5),
            new ReportColumn("Description", "Description", ReportColumnType.Text, Width: 1.5),
            new ReportColumn("In", "In", ReportColumnType.Money, true),
            new ReportColumn("Out", "Out", ReportColumnType.Money, true),
            new ReportColumn("Running", "Running", ReportColumnType.Money),
        }, ordered, Totals(ordered, "In", "Out"), f.RangeText);
    }

    public ReportResult ExpenseReport(ReportFilter f)
    {
        var charged = ExpensesCharged(f.From, f.To);

        var rows = charged
            .GroupBy(e => e.Category)
            .Select(g => Row(("Category", g.Key), ("Amount", Money.Round(g.Sum(x => x.Amount)))))
            .OrderByDescending(r => r.Num("Amount"))
            .ToList();

        return new ReportResult("Expenses", new[]
        {
            new ReportColumn("Category", "Category", ReportColumnType.Text, Width: 3),
            new ReportColumn("Amount", "Amount", ReportColumnType.Money, true),
        }, rows, Totals(rows, "Amount"), f.RangeText);
    }

    /// <summary>
    /// Every expense touching the window, with the slice of it that belongs to
    /// that window. An expense covering one day is all-or-nothing; one covering
    /// a month contributes a day at a time.
    ///
    /// Deliberately NOT used by the day book or cash flow. Those record money
    /// leaving the till, which happens once, in full, on the day he paid.
    /// </summary>
    private List<(string Category, decimal Amount)> ExpensesCharged(DateTime from, DateTime to)
    {
        var window = (From: from.Date, To: to.Date);

        // Anything whose covered period overlaps the window. Older rows have no
        // covered period, so their payment date stands in for it.
        var candidates = _db.Expenses.AsNoTracking()
            .Select(e => new
            {
                e.Category, e.Amount, e.Date, e.CoversFrom, e.CoversTo
            })
            .ToList();

        var result = new List<(string, decimal)>();

        foreach (var e in candidates)
        {
            var coverFrom = (e.CoversFrom ?? e.Date).Date;
            var coverTo = (e.CoversTo ?? e.Date).Date;
            if (coverTo < coverFrom) coverTo = coverFrom;

            if (coverTo < window.From || coverFrom > window.To) continue;

            var slice = ExpenseApportionment.InRange(
                e.Amount, coverFrom, coverTo, window.From, window.To);

            if (slice != 0m) result.Add((e.Category, slice));
        }

        return result;
    }
}
