using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Logic;

namespace ShopApp.UI.Views;

/// <summary>
/// Add or edit one expense, including the stretch of time the money buys.
/// Edits the object it is given directly, so a rejected save reopens with the
/// typing intact; the caller passes a detached copy, which makes Cancel safe.
/// </summary>
public partial class ExpenseDialog : Window
{
    private readonly Expense _expense;
    private bool _ready;

    public ExpenseDialog(Expense expense, IReadOnlyList<string> categories)
    {
        InitializeComponent();
        _expense = expense;

        var isNew = expense.Id == 0;
        Title = isNew ? "Add Expense" : "Edit Expense";
        HeaderText.Text = isNew ? "Add Expense" : "Edit Expense";

        CategoryBox.ItemsSource = categories;
        CategoryBox.Text = expense.Category;

        DateBox.SelectedDate = expense.Date == default ? DateTime.Today : expense.Date;
        DateBox.SelectedDateChanged += Covers_DateChanged;

        if (expense.Amount > 0)
            AmountBox.Text = expense.Amount.ToString("0.00", CultureInfo.CurrentCulture);
        AmountBox.TextChanged += (_, _) => UpdatePreview();

        NotesBox.Text = expense.Notes ?? "";

        CoversBox.ItemsSource = ExpenseApportionment.Frequencies;
        CoversBox.SelectedItem = FrequencyOf(expense);

        CoversFromBox.SelectedDate = expense.CoversFrom ?? expense.Date;
        CoversToBox.SelectedDate = expense.CoversTo ?? expense.Date;

        _ready = true;
        ApplyFrequency();

        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(CategoryBox.Text)) CategoryBox.Focus();
            else AmountBox.Focus();
        };
    }

    /// <summary>
    /// Works out which entry in the list an existing expense was saved under,
    /// so reopening it shows "Monthly" rather than dropping back to Custom.
    /// </summary>
    private static string FrequencyOf(Expense e)
    {
        if (e.CoversFrom is not DateTime from || e.CoversTo is not DateTime to)
            return "One-off";

        foreach (var candidate in ExpenseApportionment.Frequencies)
        {
            if (candidate is "One-off" or "Custom") continue;

            var (f, t) = ExpenseApportionment.PeriodFor(candidate, from);
            if (f.Date == from.Date && t.Date == to.Date) return candidate;
        }

        return from.Date == to.Date ? "One-off" : "Custom";
    }

    private void Covers_Changed(object sender, SelectionChangedEventArgs e) => ApplyFrequency();

    private void Covers_DateChanged(object? sender, SelectionChangedEventArgs e) => ApplyFrequency();

    /// <summary>
    /// Turns the chosen frequency into a from/to pair. Custom leaves the dates
    /// to him; everything else derives them from the payment date, because a
    /// month of rent starts the day he hands the money over.
    /// </summary>
    private void ApplyFrequency()
    {
        if (!_ready) return;

        var frequency = CoversBox.SelectedItem as string ?? "One-off";
        var custom = frequency == "Custom";

        CustomRangePanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;

        if (!custom)
        {
            var start = DateBox.SelectedDate ?? DateTime.Today;
            var (from, to) = ExpenseApportionment.PeriodFor(frequency, start);

            _ready = false;                 // setting these re-enters this method
            CoversFromBox.SelectedDate = from;
            CoversToBox.SelectedDate = to;
            _ready = true;
        }

        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!_ready) return;

        var from = CoversFromBox.SelectedDate ?? DateTime.Today;
        var to = CoversToBox.SelectedDate ?? from;
        var days = ExpenseApportionment.Days(from, to);

        if (days <= 1)
        {
            SpreadPanel.Visibility = Visibility.Collapsed;
            return;
        }

        decimal.TryParse(AmountBox.Text, NumberStyles.Number,
                         CultureInfo.CurrentCulture, out var amount);

        var perDay = ExpenseApportionment.PerDay(amount, from, to);

        SpreadPanel.Visibility = Visibility.Visible;
        SpreadHeadline.Text = amount > 0
            ? $"Charged at {perDay:N2} a day across {days} days"
            : $"Spread across {days} days";

        SpreadDetail.Text =
            $"Covers {from:dd-MMM-yyyy} to {to:dd-MMM-yyyy}. " +
            "Profit and Loss charges only the days inside the report's own range, " +
            "so a daily report shows one day of it and the full month shows all of it. " +
            $"The money still leaves on {(DateBox.SelectedDate ?? DateTime.Today):dd-MMM-yyyy}, " +
            "which is where the day book and cash flow show it.";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var category = CategoryBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(category))
        {
            Warn("Choose or type a category.");
            return;
        }

        if (!decimal.TryParse(AmountBox.Text, NumberStyles.Number,
                              CultureInfo.CurrentCulture, out var amount) || amount <= 0)
        {
            Warn("Enter the amount as a number greater than zero.");
            return;
        }

        var from = CoversFromBox.SelectedDate ?? DateBox.SelectedDate ?? DateTime.Today;
        var to = CoversToBox.SelectedDate ?? from;

        if (to.Date < from.Date)
        {
            Warn("The covered period ends before it starts. Check the two dates.");
            return;
        }

        _expense.Category = category;
        _expense.Amount = amount;
        _expense.Date = DateBox.SelectedDate ?? DateTime.Today;
        _expense.Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        // A single-day period is the same as no period at all. Storing nulls
        // keeps one-off expenses looking like the ones recorded before this
        // existed, instead of two shapes meaning the same thing.
        if (to.Date == from.Date)
        {
            _expense.CoversFrom = null;
            _expense.CoversTo = null;
        }
        else
        {
            _expense.CoversFrom = from.Date;
            _expense.CoversTo = to.Date;
        }

        DialogResult = true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Check this first",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
