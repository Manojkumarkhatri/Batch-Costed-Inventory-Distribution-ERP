using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

/// <summary>
/// Money received from customers. The screen his balances have been missing:
/// until now a sale could be recorded but never settled, so every party looked
/// permanently in debt and the aging buckets were meaningless.
/// </summary>
public partial class PaymentInViewModel : ObservableObject
{
    private readonly PaymentService _payments;
    private readonly PartyService _parties;

    [ObservableProperty] private string period = "This Month";
    [ObservableProperty] private DateTime fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime toDate = DateTime.Today;
    [ObservableProperty] private string? searchTerm;
    [ObservableProperty] private PaymentListRow? selectedPayment;

    [ObservableProperty] private decimal totalAmount;
    [ObservableProperty] private decimal totalOnAccount;
    [ObservableProperty] private string statusMessage = "";

    public ObservableCollection<PaymentListRow> Payments { get; } = new();

    public IReadOnlyList<string> Periods { get; } =
        new[] { "This Month", "Last Month", "This Year", "Custom" };

    /// <summary>
    /// Set by the view. Shows the entry dialog and returns what to record,
    /// or null if the user cancelled.
    /// </summary>
    public Func<IReadOnlyList<PartyRow>, PaymentInput?>? ShowPaymentForm { get; set; }

    public PaymentInViewModel(PaymentService payments, PartyService parties)
    {
        _payments = payments;
        _parties = parties;
        ApplyPeriod();
    }

    public bool HasSelection => SelectedPayment is not null;

    partial void OnSelectedPaymentChanged(PaymentListRow? value) =>
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
        var keepId = SelectedPayment?.Id ?? 0;

        var rows = _payments.List(FromDate, ToDate, PaymentDirection.In).AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            var t = SearchTerm.Trim();
            rows = rows.Where(r =>
                r.PartyName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                (r.ReferenceNo ?? "").Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        var list = rows.ToList();

        Payments.Clear();
        foreach (var r in list) Payments.Add(r);

        SelectedPayment = keepId > 0 ? Payments.FirstOrDefault(p => p.Id == keepId) : null;

        TotalAmount = Money.Round(list.Sum(r => r.Amount));
        TotalOnAccount = Money.Round(list.Sum(r => r.OnAccount));

        StatusMessage = $"{list.Count} payment{(list.Count == 1 ? "" : "s")} received";
    }

    [RelayCommand]
    private void AddPayment()
    {
        if (ShowPaymentForm is null) return;

        var customers = _parties.Search(null, null, includeInactive: false);
        if (customers.Count == 0)
        {
            MessageBox.Show("Add a party first - a payment has to be received from someone.",
                "No parties", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var input = ShowPaymentForm(customers);
        if (input is null) return;

        try
        {
            var payment = _payments.Create(input);
            var party = customers.FirstOrDefault(c => c.Id == input.PartyId)?.Name ?? "party";
            StatusMessage = $"Received {payment.Amount:N2} from {party}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cannot record payment",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Load();
    }

    [RelayCommand]
    private void DeletePayment()
    {
        if (SelectedPayment is not { } payment) return;

        var confirm = MessageBox.Show(
            $"Delete the {payment.Amount:N2} received from {payment.PartyName} " +
            $"on {payment.Date:dd-MMM-yyyy}?\n\n" +
            "The invoices it settled go back to unpaid and the party's balance rises again.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        _payments.Delete(payment.Id);
        StatusMessage = $"Deleted the payment from {payment.PartyName}.";
        Load();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchTerm = null;
        Period = "This Month";
    }
}
