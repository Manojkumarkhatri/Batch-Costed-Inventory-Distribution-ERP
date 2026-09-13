using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

/// <summary>One account in the list, with what is in it.</summary>
public record BankAccountRow(int Id, string Name, decimal Balance, bool IsActive,
                             string? BankName, bool PrintOnInvoice)
{
    public bool IsOverdrawn => Balance < 0;
}

/// <summary>One movement on the statement, with the running balance after it.</summary>
public record BankTxnRow(int Id, DateTime Date, BankTxnType Type, string TypeLabel,
                         string? Description, decimal Amount, decimal Balance)
{
    public bool IsIn => Amount >= 0;
}

public record BankTransferInput(int FromAccountId, int? ToAccountId, decimal Amount,
                                DateTime Date, string? Description, BankTxnType Type);

/// <summary>
/// Bank accounts and the money moving through them.
///
/// A balance is never stored. It is the opening figure plus every row since,
/// exactly as stock on hand is the sum of its moves - so a balance can never
/// drift away from the transactions that are supposed to explain it.
/// </summary>
public class BankService
{
    private readonly AppDbContext _db;

    public BankService(AppDbContext db) => _db = db;

    // ------------------------------------------------------------ accounts

    public IReadOnlyList<BankAccountRow> Accounts(bool includeInactive = false)
    {
        var accounts = _db.BankAccounts.AsNoTracking()
            .Where(a => includeInactive || a.IsActive)
            .Select(a => new
            {
                a.Id, a.Name, a.OpeningBalance, a.IsActive, a.BankName, a.PrintOnInvoice
            })
            .ToList();

        if (accounts.Count == 0) return Array.Empty<BankAccountRow>();

        var ids = accounts.Select(a => a.Id).ToList();

        // Amount uses a value converter, so EF cannot SUM it in SQL.
        var moved = _db.BankTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.BankAccountId))
            .Select(t => new { t.BankAccountId, t.Amount })
            .ToList()
            .GroupBy(t => t.BankAccountId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        return accounts
            .Select(a => new BankAccountRow(
                a.Id, a.Name,
                Money.Round(a.OpeningBalance + moved.GetValueOrDefault(a.Id)),
                a.IsActive, a.BankName, a.PrintOnInvoice))
            .OrderBy(a => a.Name)
            .ToList();
    }

    public BankAccount? GetById(int id) =>
        _db.BankAccounts.AsNoTracking().FirstOrDefault(a => a.Id == id);

    public decimal BalanceOf(int accountId)
    {
        var opening = _db.BankAccounts.AsNoTracking()
            .Where(a => a.Id == accountId).Select(a => a.OpeningBalance).FirstOrDefault();

        var moved = _db.BankTransactions.AsNoTracking()
            .Where(t => t.BankAccountId == accountId)
            .Select(t => t.Amount).ToList().Sum();

        return Money.Round(opening + moved);
    }

    public SaveOutcome Save(BankAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.Name))
            return new SaveOutcome(false, "Give the account a name you will recognise.", null, 0);

        account.Name = account.Name.Trim();

        var clash = _db.BankAccounts.AsNoTracking()
            .Any(a => a.Id != account.Id && a.Name == account.Name);
        if (clash)
            return new SaveOutcome(false, $"There is already an account called {account.Name}.", null, 0);

        account.AccountNumber = Blank(account.AccountNumber);
        account.BankName = Blank(account.BankName);
        account.BranchOrIban = Blank(account.BranchOrIban);
        account.AccountHolder = Blank(account.AccountHolder);

        string? warning = null;
        if (account.PrintOnInvoice && string.IsNullOrWhiteSpace(account.AccountNumber))
            warning = "This account is set to print on invoices but has no account number, " +
                      "so a customer could not pay into it.";

        if (account.Id == 0) _db.BankAccounts.Add(account);
        else _db.BankAccounts.Update(account);

        _db.SaveChanges();
        var id = account.Id;
        _db.ChangeTracker.Clear();

        return new SaveOutcome(true, null, warning, id);
    }

    /// <summary>
    /// An account with movements is deactivated, not deleted - the statement
    /// has to keep explaining where the money went.
    /// </summary>
    public SaveOutcome Remove(int accountId)
    {
        var account = _db.BankAccounts.Find(accountId);
        if (account is null) return new SaveOutcome(false, "That account is gone already.", null, 0);

        var used = _db.BankTransactions.Any(t => t.BankAccountId == accountId);

        if (used)
        {
            account.IsActive = false;
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
            return new SaveOutcome(true, null,
                $"{account.Name} has transactions, so it was made inactive rather than deleted. " +
                "Its statement still opens.", accountId);
        }

        _db.BankAccounts.Remove(account);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return new SaveOutcome(true, null, null, accountId);
    }

    // -------------------------------------------------------- transactions

    /// <summary>
    /// The account's statement, oldest first, with a running balance - the
    /// order a statement is read in and the order that makes the balance
    /// column mean anything.
    /// </summary>
    public IReadOnlyList<BankTxnRow> Statement(int accountId)
    {
        var account = _db.BankAccounts.AsNoTracking().FirstOrDefault(a => a.Id == accountId);
        if (account is null) return Array.Empty<BankTxnRow>();

        var moves = _db.BankTransactions.AsNoTracking()
            .Where(t => t.BankAccountId == accountId)
            .Select(t => new { t.Id, t.Date, t.Type, t.Amount, t.Description, t.CounterAccountId })
            .ToList()
            .OrderBy(t => t.Date).ThenBy(t => t.Id)
            .ToList();

        var names = _db.BankAccounts.AsNoTracking()
            .Select(a => new { a.Id, a.Name }).ToList()
            .ToDictionary(a => a.Id, a => a.Name);

        var rows = new List<BankTxnRow>();
        var running = account.OpeningBalance;

        // The opening figure is a line of the statement, not a hidden starting
        // point - otherwise the first row looks wrong to anyone checking it.
        if (account.OpeningBalance != 0)
            rows.Add(new BankTxnRow(0, account.OpeningBalanceDate, BankTxnType.Opening,
                "Opening balance", null, account.OpeningBalance, Money.Round(running)));

        foreach (var t in moves)
        {
            running += t.Amount;

            var label = Label(t.Type);
            if (t.CounterAccountId is int other && names.TryGetValue(other, out var name))
                label += t.Amount < 0 ? $" to {name}" : $" from {name}";

            rows.Add(new BankTxnRow(t.Id, t.Date, t.Type, label,
                t.Description, Money.Round(t.Amount), Money.Round(running)));
        }

        return rows;
    }

    private static string Label(BankTxnType type) => type switch
    {
        BankTxnType.Deposit => "Deposit",
        BankTxnType.Withdraw => "Withdrawal",
        BankTxnType.BankToCash => "Bank to cash",
        BankTxnType.CashToBank => "Cash to bank",
        BankTxnType.BankToBank => "Transfer",
        BankTxnType.Adjustment => "Adjustment",
        _ => type.ToString()
    };

    /// <summary>
    /// Records a movement. A bank-to-bank transfer writes both halves inside
    /// one transaction and stamps them with the same group: half a transfer is
    /// money that has left one account and arrived nowhere.
    /// </summary>
    public SaveOutcome Record(BankTransferInput input)
    {
        if (input.Amount <= 0)
            return new SaveOutcome(false, "Enter an amount greater than zero.", null, 0);

        if (input.Type == BankTxnType.BankToBank)
        {
            if (input.ToAccountId is null)
                return new SaveOutcome(false, "Choose the account the money is going to.", null, 0);

            if (input.ToAccountId == input.FromAccountId)
                return new SaveOutcome(false,
                    "The two accounts are the same, so nothing would move.", null, 0);
        }

        var amount = Money.Round(input.Amount);
        var date = input.Date.Date;
        var note = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();

        using var tx = _db.Database.BeginTransaction();

        if (input.Type == BankTxnType.BankToBank)
        {
            var group = Guid.NewGuid();

            _db.BankTransactions.Add(new BankTransaction
            {
                BankAccountId = input.FromAccountId, Type = BankTxnType.BankToBank,
                Amount = -amount, Date = date, Description = note,
                TransferGroup = group, CounterAccountId = input.ToAccountId
            });

            _db.BankTransactions.Add(new BankTransaction
            {
                BankAccountId = input.ToAccountId!.Value, Type = BankTxnType.BankToBank,
                Amount = amount, Date = date, Description = note,
                TransferGroup = group, CounterAccountId = input.FromAccountId
            });
        }
        else
        {
            // Deposit and cash-to-bank put money in; the rest take it out.
            var signed = input.Type is BankTxnType.Deposit or BankTxnType.CashToBank
                ? amount
                : -amount;

            // An adjustment is a correction in whichever direction was given.
            if (input.Type == BankTxnType.Adjustment) signed = input.Amount;

            _db.BankTransactions.Add(new BankTransaction
            {
                BankAccountId = input.FromAccountId, Type = input.Type,
                Amount = signed, Date = date, Description = note
            });
        }

        _db.SaveChanges();
        tx.Commit();
        _db.ChangeTracker.Clear();

        return new SaveOutcome(true, null, null, input.FromAccountId);
    }

    /// <summary>Deletes a movement, taking the other half of a transfer with it.</summary>
    public void DeleteTransaction(int transactionId)
    {
        var t = _db.BankTransactions.Find(transactionId);
        if (t is null) return;

        using var tx = _db.Database.BeginTransaction();

        if (t.TransferGroup is Guid group)
            _db.BankTransactions.RemoveRange(
                _db.BankTransactions.Where(x => x.TransferGroup == group));
        else
            _db.BankTransactions.Remove(t);

        _db.SaveChanges();
        tx.Commit();
        _db.ChangeTracker.Clear();
    }

    /// <summary>Accounts whose details are printed at the foot of an invoice.</summary>
    public IReadOnlyList<BankAccount> ForInvoiceFooter() =>
        _db.BankAccounts.AsNoTracking()
            .Where(a => a.IsActive && a.PrintOnInvoice)
            .OrderBy(a => a.Name)
            .ToList();

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
