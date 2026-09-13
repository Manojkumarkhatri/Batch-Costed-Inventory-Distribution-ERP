using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

public record PartyRow(int Id, string Name, string? GroupName, string? Phone,
                       string? Email, decimal Balance, decimal CreditLimit, bool IsActive)
{
    /// <summary>Positive means they owe him.</summary>
    public bool OverLimit => CreditLimit > 0 && Balance > CreditLimit;

    /// <summary>"To Receive" / "To Pay" wording, as on his screen.</summary>
    public string BalanceDirection =>
        Balance > 0 ? "To Receive" : Balance < 0 ? "To Pay" : "";
}

/// <summary>
/// One line of a party's ledger. Total is the document amount as written;
/// Balance is what they owed after it, so the column reads like a statement.
/// </summary>
public record PartyTxnRow(string Type, string? Number, DateTime Date,
                          decimal Total, decimal Balance, string Status)
{
    /// <summary>Positive means they owe him. Drives the red balance.</summary>
    public bool IsReceivable => Balance > 0;
}

public class PartyService
{
    private readonly AppDbContext _db;

    public PartyService(AppDbContext db) => _db = db;

    public IReadOnlyList<PartyRow> Search(string? term, string? group, bool includeInactive)
    {
        var q = _db.Parties.AsQueryable();

        if (!includeInactive) q = q.Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(group))
            q = q.Where(p => p.GroupName == group);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var s = term.Trim();
            q = q.Where(p =>
                EF.Functions.Like(p.Name, $"%{s}%") ||
                (p.Phone != null && EF.Functions.Like(p.Phone, $"%{s}%")) ||
                (p.GroupName != null && EF.Functions.Like(p.GroupName, $"%{s}%")));
        }

        // AsNoTracking for the same reason as ItemService.Search.
        var parties = q.AsNoTracking().OrderBy(p => p.Name).ToList();
        var balances = BalancesFor(parties.Select(p => p.Id).ToList());

        return parties.Select(p => new PartyRow(
            p.Id, p.Name, p.GroupName, p.Phone, p.Email,
            balances.TryGetValue(p.Id, out var b) ? b : p.SignedOpeningBalance,
            p.CreditLimit, p.IsActive)).ToList();
    }

    public Party? GetById(int id) =>
        _db.Parties.AsNoTracking().FirstOrDefault(p => p.Id == id);

    public bool NameExists(string name, int excludeId)
    {
        var n = name.Trim();
        return _db.Parties.Any(p => p.Id != excludeId && p.Name.ToLower() == n.ToLower());
    }

    /// <summary>
    /// Balance = opening + sales - payments in - purchases + payments out.
    /// Positive means the party owes us.
    ///
    /// Money columns use a value converter, so EF cannot SUM them in SQL.
    /// Rows are filtered in SQL and added here. Fine at this volume; revisit
    /// only if the party list ever feels slow.
    /// </summary>
    public Dictionary<int, decimal> BalancesFor(IReadOnlyList<int> partyIds)
    {
        var result = new Dictionary<int, decimal>();
        if (partyIds.Count == 0) return result;

        // Opening balance is stored positive; direction comes from the flag.
        foreach (var p in _db.Parties.Where(p => partyIds.Contains(p.Id))
                     .Select(p => new { p.Id, p.OpeningBalance, p.OpeningIsReceivable }).ToList())
            result[p.Id] = p.OpeningIsReceivable ? p.OpeningBalance : -p.OpeningBalance;

        foreach (var s in _db.Sales.Where(s => partyIds.Contains(s.CustomerId) && !s.IsCancelled)
                                   .Select(s => new { s.CustomerId, s.Total }).ToList())
            result[s.CustomerId] = result.GetValueOrDefault(s.CustomerId) + s.Total;

        foreach (var p in _db.Purchases.Where(x => partyIds.Contains(x.SupplierId) && !x.IsCancelled)
                                       .Select(x => new { x.SupplierId, x.Total }).ToList())
            result[p.SupplierId] = result.GetValueOrDefault(p.SupplierId) - p.Total;

        foreach (var pay in _db.Payments.Where(x => partyIds.Contains(x.PartyId))
                                        .Select(x => new { x.PartyId, x.Direction, x.Amount }).ToList())
        {
            var delta = pay.Direction == PaymentDirection.In ? -pay.Amount : pay.Amount;
            result[pay.PartyId] = result.GetValueOrDefault(pay.PartyId) + delta;
        }

        foreach (var key in result.Keys.ToList())
            result[key] = Money.Round(result[key]);

        return result;
    }

    public decimal BalanceOf(int partyId) =>
        BalancesFor(new[] { partyId }).GetValueOrDefault(partyId);

    public SaveOutcome Save(Party party)
    {
        party.Name = party.Name?.Trim() ?? string.Empty;
        party.Phone = string.IsNullOrWhiteSpace(party.Phone) ? null : party.Phone.Trim();
        party.Email = string.IsNullOrWhiteSpace(party.Email) ? null : party.Email.Trim();
        party.GroupName = string.IsNullOrWhiteSpace(party.GroupName) ? null : party.GroupName.Trim();
        party.Address = string.IsNullOrWhiteSpace(party.Address) ? null : party.Address.Trim();

        var result = PartyValidator.Validate(party, NameExists(party.Name, party.Id));
        if (result.HasErrors)
            return new SaveOutcome(false, result.ErrorText, result.WarningText, party.Id);

        if (party.Id == 0) _db.Parties.Add(party);
        else _db.Parties.Update(party);

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return new SaveOutcome(true, null, result.WarningText, party.Id);
    }

    public bool HasHistory(int partyId) =>
        _db.Sales.Any(s => s.CustomerId == partyId) ||
        _db.Purchases.Any(p => p.SupplierId == partyId) ||
        _db.Payments.Any(p => p.PartyId == partyId);

    public SaveOutcome Remove(int partyId)
    {
        var party = _db.Parties.Find(partyId);
        if (party is null) return new SaveOutcome(false, "Party not found.", null, partyId);

        if (HasHistory(partyId))
        {
            var balance = BalanceOf(partyId);
            if (balance != 0)
                return new SaveOutcome(false,
                    $"{party.Name} has an outstanding balance of {balance:N2}. " +
                    "Settle it first, or mark the party inactive.", null, partyId);

            party.IsActive = false;
            _db.SaveChanges();
            return new SaveOutcome(true, null,
                $"{party.Name} has past transactions, so it was marked inactive instead of deleted.",
                partyId);
        }

        _db.Parties.Remove(party);
        _db.SaveChanges();
        return new SaveOutcome(true, null, null, partyId);
    }

    /// <summary>Distinct groups already in use, for the Group dropdown.</summary>
    public IReadOnlyList<string> Groups() =>
        _db.Parties.AsNoTracking()
            .Where(p => p.GroupName != null && p.GroupName != "")
            .Select(p => p.GroupName!)
            .Distinct().OrderBy(g => g).ToList();

    public void SetActive(int partyId, bool active)
    {
        var party = _db.Parties.Find(partyId);
        if (party is null) return;
        party.IsActive = active;
        _db.SaveChanges();
    }

    /// <summary>
    /// Everything that has moved this party's balance, oldest first, with the
    /// running balance after each line - the order a statement is read in.
    ///
    /// The signs match BalancesFor exactly: a sale and a payment out increase
    /// what they owe, a purchase and a payment in decrease it. If the two ever
    /// disagree, the closing figure here will not match the list, which is the
    /// symptom to look for.
    ///
    /// Cancelled documents are shown but excluded from the running balance.
    /// He needs to see that a bill existed and was voided; hiding it makes a
    /// gap in the invoice numbers that nobody can explain later.
    /// </summary>
    public IReadOnlyList<PartyTxnRow> Ledger(int partyId)
    {
        var party = _db.Parties.AsNoTracking().FirstOrDefault(p => p.Id == partyId);
        if (party is null) return Array.Empty<PartyTxnRow>();

        var entries = new List<(DateTime Date, int Order, string Type, string? Number,
                                decimal Total, decimal Effect, string Status)>();

        var sales = _db.Sales.AsNoTracking()
            .Where(s => s.CustomerId == partyId)
            .Select(s => new { s.Id, s.InvoiceNo, s.Date, s.Total, s.IsCancelled })
            .ToList();

        foreach (var s in sales)
            entries.Add((s.Date, 1, "Sale", s.InvoiceNo, s.Total,
                s.IsCancelled ? 0m : s.Total, s.IsCancelled ? "Cancelled" : ""));

        var purchases = _db.Purchases.AsNoTracking()
            .Where(p => p.SupplierId == partyId)
            .Select(p => new { p.Id, p.SupplierBillNo, p.Date, p.Total, p.IsCancelled })
            .ToList();

        foreach (var p in purchases)
            entries.Add((p.Date, 2, "Purchase", p.SupplierBillNo, p.Total,
                p.IsCancelled ? 0m : -p.Total, p.IsCancelled ? "Cancelled" : ""));

        var payments = _db.Payments.AsNoTracking()
            .Where(x => x.PartyId == partyId)
            .Select(x => new { x.Id, x.Direction, x.Mode, x.Amount, x.Date, x.ReferenceNo })
            .ToList();

        foreach (var pay in payments)
        {
            var type = pay.Direction == PaymentDirection.In ? "Payment In" : "Payment Out";
            var effect = pay.Direction == PaymentDirection.In ? -pay.Amount : pay.Amount;
            entries.Add((pay.Date, 3, $"{type} ({pay.Mode})", pay.ReferenceNo,
                pay.Amount, effect, ""));
        }

        var rows = new List<PartyTxnRow>();
        var running = party.SignedOpeningBalance;

        // The opening balance is a line of the statement, not a hidden starting
        // point - otherwise the first row looks wrong to anyone checking it.
        if (party.OpeningBalance != 0)
            rows.Add(new PartyTxnRow("Opening Balance", null, party.OpeningBalanceDate,
                party.OpeningBalance, Money.Round(running),
                party.OpeningIsReceivable ? "To Receive" : "To Pay"));

        foreach (var e in entries.OrderBy(x => x.Date).ThenBy(x => x.Order))
        {
            running += e.Effect;
            rows.Add(new PartyTxnRow(e.Type, e.Number, e.Date,
                Money.Round(e.Total), Money.Round(running), e.Status));
        }

        return rows;
    }
}
