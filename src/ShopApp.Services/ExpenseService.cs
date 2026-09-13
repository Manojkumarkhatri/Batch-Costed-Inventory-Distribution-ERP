using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Logic;

namespace ShopApp.Services;

/// <summary>
/// One recorded expense. <paramref name="Amount"/> is what he paid;
/// <paramref name="ChargedHere"/> is the slice of it belonging to the period
/// currently on screen, which differs whenever the money covers a stretch of
/// time rather than a single day.
/// </summary>
public record ExpenseRow(int Id, DateTime Date, string Category, decimal Amount,
                         DateTime CoversFrom, DateTime CoversTo, decimal ChargedHere,
                         string? Notes)
{
    public bool IsSpread => CoversTo.Date > CoversFrom.Date;

    public string Covers => IsSpread
        ? $"{CoversFrom:dd MMM} to {CoversTo:dd MMM yyyy}"
        : "One-off";

    public string PerDay => IsSpread
        ? $"{Money.Round(Amount / ((CoversTo.Date - CoversFrom.Date).Days + 1)):N2} / day"
        : "";
}

/// <summary>
/// Running costs - rent, wages, fuel, electricity, repairs.
///
/// These are the figures that were missing from Profit and Loss. Until now the
/// report could only subtract the cost of goods sold, so every month looked
/// more profitable than it was by exactly the amount he spends keeping the
/// place open.
///
/// An expense is not a purchase. A purchase buys stock he still owns and can
/// sell; an expense is money gone. That is why they are separate tables and
/// why buying stock does not reduce profit but paying the rent does.
/// </summary>
public class ExpenseService
{
    private readonly AppDbContext _db;

    public ExpenseService(AppDbContext db) => _db = db;

    /// <summary>
    /// Seeded so the category box is never blank on a fresh install. His own
    /// categories join the list as soon as he types them.
    /// </summary>
    public static IReadOnlyList<string> SuggestedCategories { get; } = new[]
    {
        "Rent", "Wages", "Electricity", "Fuel", "Transport", "Repairs",
        "Packaging", "Phone and internet", "Cold storage", "Licences",
        "Bank charges", "Other"
    };

    /// <summary>Every category he has actually used, plus the suggestions.</summary>
    public IReadOnlyList<string> Categories()
    {
        var used = _db.Expenses.AsNoTracking()
            .Select(e => e.Category)
            .Distinct()
            .ToList();

        return used.Concat(SuggestedCategories)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList();
    }

    /// <summary>
    /// Expenses touching the period - which is not the same as expenses paid
    /// in it. Rent paid on 1 September still belongs to a report for the 20th,
    /// because the money is buying that day too.
    /// </summary>
    public IReadOnlyList<ExpenseRow> List(DateTime from, DateTime to, string? category = null)
    {
        var q = _db.Expenses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = category.Trim();
            q = q.Where(e => e.Category == c);
        }

        var window = (From: from.Date, To: to.Date);

        return q.Select(e => new { e.Id, e.Date, e.Category, e.Amount, e.CoversFrom, e.CoversTo, e.Notes })
            .ToList()
            .Select(e =>
            {
                var coverFrom = (e.CoversFrom ?? e.Date).Date;
                var coverTo = (e.CoversTo ?? e.Date).Date;
                if (coverTo < coverFrom) coverTo = coverFrom;

                var charged = ExpenseApportionment.InRange(
                    e.Amount, coverFrom, coverTo, window.From, window.To);

                return new ExpenseRow(e.Id, e.Date, e.Category, e.Amount,
                    coverFrom, coverTo, charged, e.Notes);
            })
            .Where(r => r.ChargedHere != 0m)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Id)
            .ToList();
    }

    /// <summary>Totals per category for the summary strip.</summary>
    public IReadOnlyList<(string Category, decimal Amount)> ByCategory(DateTime from, DateTime to) =>
        List(from, to)
            .GroupBy(e => e.Category)
            .Select(g => (g.Key, Money.Round(g.Sum(x => x.ChargedHere))))
            .OrderByDescending(x => x.Item2)
            .ToList();

    public Expense? GetById(int id) =>
        _db.Expenses.AsNoTracking().FirstOrDefault(e => e.Id == id);

    public SaveOutcome Save(Expense expense)
    {
        if (string.IsNullOrWhiteSpace(expense.Category))
            return new SaveOutcome(false, "Choose or type a category.", null, 0);

        if (expense.Amount <= 0)
            return new SaveOutcome(false, "Enter an amount greater than zero.", null, 0);

        expense.Category = expense.Category.Trim();
        expense.Notes = string.IsNullOrWhiteSpace(expense.Notes) ? null : expense.Notes.Trim();
        expense.Date = expense.Date.Date;

        string? warning = null;
        if (expense.Date > DateTime.Today)
            warning = "That date is in the future. It will not appear in this month's " +
                      "Profit and Loss until then.";

        if (expense.Id == 0)
            _db.Expenses.Add(expense);
        else
            _db.Expenses.Update(expense);

        _db.SaveChanges();
        var id = expense.Id;
        _db.ChangeTracker.Clear();

        return new SaveOutcome(true, null, warning, id);
    }

    /// <summary>
    /// Removed outright. An expense moves no stock and nothing was printed for
    /// anyone, so unlike a sale there is no trail worth preserving.
    /// </summary>
    public void Delete(int id)
    {
        var expense = _db.Expenses.Find(id);
        if (expense is null) return;

        _db.Expenses.Remove(expense);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }
}
