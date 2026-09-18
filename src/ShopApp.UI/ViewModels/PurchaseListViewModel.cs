using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using System.IO;
using ShopApp.Domain.Logic;
using ShopApp.Reports;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

/// <summary>
/// The Purchase Bills list. Entry stays in PurchaseView, which this screen
/// opens in a window - same arrangement as Sale Invoices.
/// </summary>
public partial class PurchaseListViewModel : ObservableObject
{
    private readonly PurchaseService _purchases;
    private readonly DocumentBuilder _documents;

    [ObservableProperty] private string period = "This Month";
    [ObservableProperty] private DateTime fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private string? searchTerm;
    [ObservableProperty] private PurchaseListRow? selectedPurchase;

    [ObservableProperty] private decimal totalPurchases;
    [ObservableProperty] private decimal totalPaid;
    [ObservableProperty] private decimal totalBalance;
    [ObservableProperty] private string statusMessage = "";

    public ObservableCollection<PurchaseListRow> Purchases { get; } = new();

    public IReadOnlyList<string> Periods { get; } =
        new[] { "This Month", "Last Month", "This Year", "Custom" };

    /// <summary>Set by the view. Opens the entry form; returns once it closes.</summary>
    public Action? ShowPurchaseForm { get; set; }

    /// <summary>
    /// Set by the view. Opens the entry form loaded with an existing bill.
    /// </summary>
    public Action<int>? ShowPurchaseFormForEdit { get; set; }

    /// <summary>
    /// Set by the view. Asks whether the description goes on the printout,
    /// and returns null if he changed his mind.
    /// </summary>
    public Func<bool, bool?>? AskPrintOptions { get; set; }

    public PurchaseListViewModel(PurchaseService purchases, DocumentBuilder documents)
    {
        _purchases = purchases;
        _documents = documents;
        ApplyPeriod();
    }

    public bool HasSelection => SelectedPurchase is not null;

    partial void OnSelectedPurchaseChanged(PurchaseListRow? value) =>
        OnPropertyChanged(nameof(HasSelection));

    partial void OnPeriodChanged(string value) => ApplyPeriod();
    partial void OnSearchTermChanged(string? value) => Load();
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
        var keepId = SelectedPurchase?.Id ?? 0;

        var rows = _purchases.List(FromDate, ToDate).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var t = SearchTerm.Trim();
            rows = rows.Where(r =>
                r.PartyName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                r.Number.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows.ToList();

        Purchases.Clear();
        foreach (var r in list) Purchases.Add(r);

        SelectedPurchase = keepId > 0 ? Purchases.FirstOrDefault(p => p.Id == keepId) : null;

        // Cancelled bills are shown but never counted - the totals have to tie
        // out to stock that is actually on the shelf.
        var live = list.Where(r => !r.IsCancelled).ToList();
        TotalPurchases = Money.Round(live.Sum(r => r.Amount));
        TotalPaid = Money.Round(live.Sum(r => r.Paid));
        TotalBalance = Money.Round(live.Sum(r => r.Balance));

        var cancelled = list.Count - live.Count;
        StatusMessage = $"{live.Count} bill{(live.Count == 1 ? "" : "s")}" +
                        (cancelled > 0 ? $"   |   {cancelled} cancelled" : "");
    }

    [RelayCommand]
    private void AddPurchase()
    {
        ShowPurchaseForm?.Invoke();
        Load();
    }

    [RelayCommand]
    private void Print()
    {
        if (SelectedPurchase is not { } bill) return;

        var data = _documents.BuildPurchaseBill(bill.Id);
        if (data is null)
        {
            StatusMessage = "That bill could not be rebuilt for printing.";
            return;
        }

        var withDescription = AskPrintOptions?.Invoke(!string.IsNullOrWhiteSpace(data.Description));
        if (withDescription is null) return;

        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ShopApp Purchase Bills");
            Directory.CreateDirectory(folder);

            var safe = string.Join("_", data.BillNo.Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(folder, $"Bill-{safe}.pdf");

            DocumentPrinter.SavePurchaseBill(data, withDescription.Value, path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

            AppLog.Info($"Purchase bill {data.BillNo} printed to {path}");
            StatusMessage = $"Bill {data.BillNo} written to {path}";
        }
        catch (Exception ex)
        {
            AppLog.Error($"Printing purchase bill {bill.Id} failed", ex);
            MessageBox.Show($"Printing failed:\n{ex.Message}", "Print",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void CancelPurchase()
    {
        if (SelectedPurchase is not { } bill) return;

        if (bill.IsCancelled)
        {
            MessageBox.Show($"Bill {bill.Number} is already cancelled.", "Cancel bill",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Cancel bill {bill.Number} from {bill.PartyName}?\n\n" +
            "The stock it brought in is taken back off the shelf and the supplier's " +
            "balance is reversed. This is refused if any of that stock has already " +
            "been sold.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var result = _purchases.Cancel(bill.Id, "Cancelled from the purchase list");

        if (!result.Success)
        {
            MessageBox.Show(result.ErrorText, "Cannot cancel",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.WarningText))
            MessageBox.Show(result.WarningText, "Cancelled - please check",
                MessageBoxButton.OK, MessageBoxImage.Information);

        StatusMessage = $"Bill {bill.Number} cancelled. Stock reversed.";
        Load();
    }

    /// <summary>
    /// Opens the bill for correction, but only while none of the stock it
    /// brought in has moved. After that its cost is frozen onto whatever was
    /// sold from it, and the honest answer is cancel and re-enter.
    /// </summary>
    [RelayCommand]
    private void Correct()
    {
        if (SelectedPurchase is not { } bill) return;

        var (canEdit, reason) = _purchases.CanEdit(bill.Id);

        if (!canEdit)
        {
            MessageBox.Show(
                $"{reason}\n\n" +
                "Cancelling puts the stock back and lets you enter it again, but that is " +
                "refused too once any of it has been sold.",
                "Cannot edit this bill", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ShowPurchaseFormForEdit?.Invoke(bill.Id);
        Load();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchTerm = null;
        Period = "This Month";
    }
}
