using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Domain.Logic;
using ShopApp.Reports;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

/// <summary>
/// The Sale Invoices list. Entry itself still lives in SaleView, which this
/// screen opens in a window - the form was already complete and works, so it
/// was worth keeping rather than rewriting into the list.
/// </summary>
public partial class SaleListViewModel : ObservableObject
{
    private readonly SaleService _sales;
    private readonly InvoiceBuilder _invoices;

    [ObservableProperty] private string period = "This Month";
    [ObservableProperty] private DateTime fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private string? searchTerm;
    [ObservableProperty] private SaleListRow? selectedSale;

    [ObservableProperty] private decimal totalSales;
    [ObservableProperty] private decimal totalReceived;
    [ObservableProperty] private decimal totalBalance;
    [ObservableProperty] private string statusMessage = "";

    public ObservableCollection<SaleListRow> Sales { get; } = new();

    /// <summary>This year runs July to June, the way his books do.</summary>
    public IReadOnlyList<string> Periods { get; } =
        new[] { "This Month", "Last Month", "This Year", "Custom" };

    /// <summary>Set by the view. Opens the invoice form; returns once it closes.</summary>
    public Action? ShowSaleForm { get; set; }

    public SaleListViewModel(SaleService sales, InvoiceBuilder invoices)
    {
        _sales = sales;
        _invoices = invoices;
        ApplyPeriod();
    }

    public bool HasSelection => SelectedSale is not null;

    partial void OnSelectedSaleChanged(SaleListRow? value) =>
        OnPropertyChanged(nameof(HasSelection));

    partial void OnPeriodChanged(string value) => ApplyPeriod();
    partial void OnSearchTermChanged(string? value) => Load();

    // Editing a date by hand means he wants that exact span, so stop
    // overwriting it from the preset on the next reload.
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
        var keepId = SelectedSale?.Id ?? 0;

        var rows = _sales.List(FromDate, ToDate).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var t = SearchTerm.Trim();
            rows = rows.Where(r =>
                r.PartyName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                r.InvoiceNo.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows.ToList();

        Sales.Clear();
        foreach (var r in list) Sales.Add(r);

        SelectedSale = keepId > 0 ? Sales.FirstOrDefault(s => s.Id == keepId) : null;

        // Cancelled invoices are shown but never counted - the totals have to
        // tie out to the money that actually changed hands.
        var live = list.Where(r => !r.IsCancelled).ToList();
        TotalSales = Money.Round(live.Sum(r => r.Amount));
        TotalReceived = Money.Round(live.Sum(r => r.Received));
        TotalBalance = Money.Round(live.Sum(r => r.Balance));

        var cancelled = list.Count - live.Count;
        StatusMessage = $"{live.Count} invoice{(live.Count == 1 ? "" : "s")}" +
                        (cancelled > 0 ? $"   |   {cancelled} cancelled" : "");
    }

    [RelayCommand]
    private void AddSale()
    {
        ShowSaleForm?.Invoke();
        Load();
    }

    [RelayCommand]
    private void Print()
    {
        if (SelectedSale is not { } sale) return;

        try
        {
            var built = _invoices.Build(sale.Id);
            if (built is null)
            {
                StatusMessage = "That invoice could not be rebuilt for printing.";
                return;
            }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ShopApp Invoices");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, $"Invoice-{built.Value.Data.InvoiceNo}.pdf");
            InvoicePrinter.SavePdf(built.Value.Data, built.Value.Options, path);

            // Opens in the default PDF viewer, where he can print or save.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            StatusMessage = $"Invoice {sale.InvoiceNo} written to {path}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Printing failed:\n{ex.Message}", "Print",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void CancelSale()
    {
        if (SelectedSale is not { } sale) return;

        if (sale.IsCancelled)
        {
            MessageBox.Show($"Invoice {sale.InvoiceNo} is already cancelled.", "Cancel invoice",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Cancel invoice {sale.InvoiceNo} for {sale.PartyName}?\n\n" +
            "The stock goes back on the shelf and the party's balance is reversed. " +
            "The invoice number stays in the list, marked cancelled.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            _sales.Cancel(sale.Id, "Cancelled from the invoice list");
            StatusMessage = $"Invoice {sale.InvoiceNo} cancelled. Stock returned.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cannot cancel",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Load();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchTerm = null;
        Period = "This Month";
    }
}
