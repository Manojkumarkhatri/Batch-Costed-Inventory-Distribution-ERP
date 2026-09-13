using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Logic;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

/// <summary>One line of the by-category summary strip.</summary>
public record CategoryTotal(string Category, decimal Amount);

/// <summary>
/// Running costs. The last thing in the app that was recorded only halfway:
/// the entity, the report and the Profit and Loss line all existed, but
/// nothing could enter one, so profit was overstated by everything he spends
/// keeping the doors open.
/// </summary>
public partial class ExpensesViewModel : ObservableObject
{
    private readonly ExpenseService _expenses;

    [ObservableProperty] private string period = "This Month";
    [ObservableProperty] private DateTime fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private string? categoryFilter;
    [ObservableProperty] private ExpenseRow? selectedExpense;

    [ObservableProperty] private decimal totalSpent;
    [ObservableProperty] private string statusMessage = "";

    public ObservableCollection<ExpenseRow> Expenses { get; } = new();
    public ObservableCollection<CategoryTotal> ByCategory { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();

    public IReadOnlyList<string> Periods { get; } =
        new[] { "This Month", "Last Month", "This Year", "Custom" };

    /// <summary>
    /// Set by the view. Opens the entry dialog on the expense passed in and
    /// returns true if the user pressed Save.
    /// </summary>
    public Func<Expense, IReadOnlyList<string>, bool>? ShowEditor { get; set; }

    public ExpensesViewModel(ExpenseService expenses)
    {
        _expenses = expenses;
        ApplyPeriod();
    }

    public bool HasSelection => SelectedExpense is not null;

    partial void OnSelectedExpenseChanged(ExpenseRow? value) =>
        OnPropertyChanged(nameof(HasSelection));

    partial void OnPeriodChanged(string value) => ApplyPeriod();
    partial void OnCategoryFilterChanged(string? value) => Load();
    partial void OnFromDateChanged(DateTime value) { if (Period == "Custom") Load(); }
    partial void OnToDateChanged(DateTime value) { if (Period == "Custom") Load(); }

    private void ApplyPeriod()
    {
        var today = DateTime.Today;

        switch (Period)
        {
            case "Last Month":
                var firstThis = new DateTime(today.Year, today.Month, 1);
                FromDate = firstThis.AddMonths(-1);
                ToDate = firstThis.AddDays(-1);
                break;

            case "This Year":
                var startYear = today.Month >= 7 ? today.Year : today.Year - 1;
                FromDate = new DateTime(startYear, 7, 1);
                ToDate = new DateTime(startYear + 1, 6, 30);
                break;

            case "Custom":
                break;

            default:
                var first = new DateTime(today.Year, today.Month, 1);
                FromDate = first;
                ToDate = first.AddMonths(1).AddDays(-1);
                break;
        }

        Load();
    }

    public void Load()
    {
        var keepId = SelectedExpense?.Id ?? 0;

        var rows = _expenses.List(FromDate, ToDate, CategoryFilter);

        Expenses.Clear();
        foreach (var r in rows) Expenses.Add(r);

        SelectedExpense = keepId > 0 ? Expenses.FirstOrDefault(e => e.Id == keepId) : null;

        // The strip always shows the whole period, whatever the category filter
        // is set to - it is there to answer "where is the money going", and a
        // single-category breakdown answers nothing. The figures are the
        // apportioned slices, so they tie out to Profit and Loss.
        ByCategory.Clear();
        foreach (var (category, amount) in _expenses.ByCategory(FromDate, ToDate))
            ByCategory.Add(new CategoryTotal(category, amount));

        TotalSpent = Money.Round(ByCategory.Sum(c => c.Amount));

        var categories = _expenses.Categories();
        Categories.Clear();
        foreach (var c in categories) Categories.Add(c);

        StatusMessage = $"{rows.Count} expense{(rows.Count == 1 ? "" : "s")}" +
                        (string.IsNullOrWhiteSpace(CategoryFilter) ? "" : $" in {CategoryFilter}");
    }

    [RelayCommand]
    private void New() => Persist(new Expense
    {
        Date = DateTime.Today,
        Category = CategoryFilter ?? ""
    });

    [RelayCommand]
    private void Edit()
    {
        if (SelectedExpense is not { } row) return;

        var draft = _expenses.GetById(row.Id);
        if (draft is not null) Persist(draft);
    }

    /// <summary>
    /// Opens the editor, saves, and reopens it on failure with the user's
    /// typing still in place rather than throwing the whole form away.
    /// </summary>
    private void Persist(Expense draft)
    {
        if (ShowEditor is null) return;

        while (true)
        {
            if (!ShowEditor(draft, _expenses.Categories())) return;   // cancelled

            var outcome = _expenses.Save(draft);

            if (!outcome.Success)
            {
                MessageBox.Show(outcome.ErrorText, "Cannot save",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(outcome.WarningText))
                MessageBox.Show(outcome.WarningText, "Saved - please check",
                    MessageBoxButton.OK, MessageBoxImage.Information);

            Load();
            SelectedExpense = Expenses.FirstOrDefault(e => e.Id == outcome.Id);
            StatusMessage = $"Saved {draft.Category} {draft.Amount:N2}.";
            return;
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedExpense is not { } row) return;

        var confirm = MessageBox.Show(
            $"Delete the {row.Amount:N2} spent on {row.Category} " +
            $"dated {row.Date:dd-MMM-yyyy}?",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _expenses.Delete(row.Id);
        StatusMessage = $"Deleted the {row.Category} expense.";
        Load();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        CategoryFilter = null;
        Period = "This Month";
    }
}
