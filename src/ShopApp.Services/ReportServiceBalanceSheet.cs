using Microsoft.EntityFrameworkCore;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

/// <summary>
/// The balance sheet, and the honest limits on it.
///
/// This app records trading, not double-entry books. It knows precisely what
/// is on the shelf, what customers owe, what is owed to suppliers, and what is
/// in the bank. It knows nothing about capital introduced, money drawn out,
/// the van, the freezer, or any loan.
///
/// So this is not an accountant's balance sheet and is not labelled as one.
/// Assets and liabilities are real, measured figures. Owner's equity is the
/// difference between them - the residual, not an independently tracked
/// number. Presented that way the statement always balances, because equity
/// is defined as the thing that makes it balance, and the report says so
/// rather than implying an audit.
///
/// It is produced as of today only. A balance sheet for a past date means
/// replaying every stock move and allocation up to that date; producing one
/// from today's figures with an old label would be quietly wrong, which is
/// worse than not offering it.
/// </summary>
public partial class ReportService
{
    public ReportResult BalanceSheet(ReportFilter f)
    {
        var today = DateTime.Today;
        var settings = _db.Settings.AsNoTracking().Single(s => s.Id == 1);

        // ---------------------------------------------------------- assets

        // Stock at what each batch actually cost, never an average.
        var stockValue = Money.Round(_stock.StockSummary().Sum(r => r.StockValue));

        var partyIds = _db.Parties.AsNoTracking().Select(p => p.Id).ToList();
        var balances = _parties.BalancesFor(partyIds);

        var receivables = Money.Round(balances.Values.Where(v => v > 0).Sum());
        var payables = Money.Round(Math.Abs(balances.Values.Where(v => v < 0).Sum()));

        var bankRows = _db.BankAccounts.AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => new { a.Id, a.Name, a.OpeningBalance })
            .ToList();

        // BankTransaction.Amount is already signed - money out is stored
        // negative - so the balance is a plain sum, exactly as BankService
        // computes it. Two different rules for one figure is how a bank
        // screen and a balance sheet end up disagreeing.
        var bankMoves = _db.BankTransactions.AsNoTracking()
            .Select(t => new { t.BankAccountId, t.Type, t.Amount })
            .ToList();

        var bankBalances = bankRows.ToDictionary(
            a => a.Id,
            a => Money.Round(a.OpeningBalance + bankMoves
                .Where(m => m.BankAccountId == a.Id)
                .Sum(m => m.Amount)));

        var bankTotal = Money.Round(bankBalances.Values.Sum());

        var cash = CashInHand(settings, bankMoves.Select(m => (m.Type, m.Amount)).ToList());

        var totalAssets = Money.Round(cash + bankTotal + stockValue + receivables);
        var totalLiabilities = payables;
        var equity = Money.Round(totalAssets - totalLiabilities);

        // ----------------------------------------------------------- rows

        var rows = new List<ReportRow>();

        void Section(string name) => rows.Add(Banner(name));
        void Line(string label, decimal amount) =>
            rows.Add(Row(("Line", label), ("Amount", amount)));

        Section("ASSETS");
        Line("Cash in hand", cash);

        foreach (var a in bankRows.OrderBy(a => a.Name))
            Line($"Bank - {a.Name}", bankBalances[a.Id]);

        Line("Stock on hand", stockValue);
        Line("Receivables - owed by customers", receivables);

        var assets = Row(("Line", "Total assets"), ("Amount", totalAssets));
        assets.IsSummaryRow = true;
        rows.Add(assets);

        Section("LIABILITIES");
        Line("Payables - owed to suppliers", payables);

        var liabilities = Row(("Line", "Total liabilities"), ("Amount", totalLiabilities));
        liabilities.IsSummaryRow = true;
        rows.Add(liabilities);

        Section("OWNER'S EQUITY");
        var networth = Row(("Line", "Net worth of the business"), ("Amount", equity));
        networth.IsSummaryRow = true;
        rows.Add(networth);

        return new ReportResult(
            "Balance Sheet",
            new[]
            {
                new ReportColumn("", "Line", ReportColumnType.Text, Width: 4),
                new ReportColumn("Amount", "Amount", ReportColumnType.Money, Width: 1.4),
            },
            rows,
            new Dictionary<string, decimal>(),
            $"As of {today:dd MMM yyyy}",
            new[]
            {
                ("Total assets", InvoiceFormatting.Money(totalAssets, false, true)),
                ("Total liabilities", InvoiceFormatting.Money(totalLiabilities, false, true)),
                ("Net worth", InvoiceFormatting.Money(equity, false, true)),
            });
    }

    /// <summary>A full-width heading inside the statement.</summary>
    private static ReportRow Banner(string text)
    {
        var r = Row(("Line", text));
        r.IsSummaryRow = true;
        return r;
    }

    /// <summary>
    /// Cash in the till, worked out from what has moved rather than counted.
    ///
    /// Opening figure, plus cash received, less cash paid out, plus anything
    /// withdrawn from the bank, less anything banked, less expenses.
    ///
    /// One assumption is baked in: expenses are treated as paid in cash,
    /// because an Expense has no payment mode. Rent paid by bank transfer
    /// therefore understates cash and overstates the bank. Giving Expense a
    /// payment mode is the fix, and is worth doing before he leans on this
    /// figure.
    /// </summary>
    private decimal CashInHand(AppSettings settings,
                               List<(BankTxnType Type, decimal Amount)> bankMoves)
    {
        var payments = _db.Payments.AsNoTracking()
            .Select(p => new { p.Direction, p.Mode, p.Amount })
            .ToList();

        var cashIn = payments
            .Where(p => p.Mode == PaymentMode.Cash && p.Direction == PaymentDirection.In)
            .Sum(p => p.Amount);

        var cashOut = payments
            .Where(p => p.Mode == PaymentMode.Cash && p.Direction == PaymentDirection.Out)
            .Sum(p => p.Amount);

        // Signed on the bank's side, so take the magnitude: money leaving the
        // bank for the till is cash coming in.
        var fromBank = bankMoves.Where(m => m.Type == BankTxnType.BankToCash)
                                .Sum(m => Math.Abs(m.Amount));
        var toBank = bankMoves.Where(m => m.Type == BankTxnType.CashToBank)
                              .Sum(m => Math.Abs(m.Amount));

        var expenses = _db.Expenses.AsNoTracking().Select(e => e.Amount).ToList().Sum();

        return Money.Round(settings.OpeningCashInHand
                           + cashIn - cashOut
                           + fromBank - toBank
                           - expenses);
    }
}
