using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using ShopApp.Reports;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

public partial class SaleLineViewModel : ObservableObject
{
    [ObservableProperty] private Item? item;
    [ObservableProperty] private decimal? packQty;
    [ObservableProperty] private decimal qty;
    [ObservableProperty] private decimal rate;

    /// <summary>Live stock for the chosen item, so he sees a shortfall before saving.</summary>
    [ObservableProperty] private decimal available;

    public decimal Amount => Money.Round(Qty * Rate);

    public string UnitLabel => Item?.BaseUnit ?? "";
    public bool ShortOfStock => Item is not null && Qty > Available;

    public Func<int, decimal>? StockLookup { get; set; }

    partial void OnItemChanged(Item? value)
    {
        if (value is null) return;
        if (Rate == 0) Rate = value.DefaultSalePrice;
        Available = StockLookup?.Invoke(value.Id) ?? 0m;
        OnPropertyChanged(nameof(UnitLabel));
        RecalculateFromPack();
        OnPropertyChanged(nameof(ShortOfStock));
    }

    partial void OnPackQtyChanged(decimal? value) => RecalculateFromPack();

    private void RecalculateFromPack()
    {
        if (Item is null || PackQty is not decimal packs) return;
        if (Item.IsVariableWeight) return;
        if (Item.ConversionFactor <= 0) return;
        Qty = UnitConverter.PackToBase(packs, Item.ConversionFactor);
    }

    partial void OnQtyChanged(decimal value)
    {
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(ShortOfStock));
    }

    partial void OnRateChanged(decimal value) => OnPropertyChanged(nameof(Amount));
}

public partial class SaleViewModel : ObservableObject
{
    private readonly SaleService _sales;
    private readonly ItemService _items;
    private readonly PartyService _parties;
    private readonly StockService _stock;
    private readonly InvoiceBuilder _invoices;

    [ObservableProperty] private PartyRow? customer;
    [ObservableProperty] private DateTime date = DateTime.Today;
    [ObservableProperty] private decimal discount;
    [ObservableProperty] private decimal paidNow;
    [ObservableProperty] private PaymentMode paymentMode = PaymentMode.Cash;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private decimal customerBalance;
    [ObservableProperty] private string nextInvoiceNo = "";

    // Print toggles, mirroring his Vyapar "Invoice Print" screen.
    [ObservableProperty] private bool showReceivedAmount;
    [ObservableProperty] private bool showBalanceAmount;
    [ObservableProperty] private bool showPartyCurrentBalance;
    [ObservableProperty] private bool showTotalItemQuantity;
    [ObservableProperty] private bool showDecimals;
    [ObservableProperty] private bool groupDigits = true;
    [ObservableProperty] private string paymentTypeLabel = "Credit";
    [ObservableProperty] private bool expandTableToWholePage = true;
    [ObservableProperty] private int minimumRowsInItemTable;

    /// <summary>0 = English (thousand/million), 1 = Indian (lakh/crore).</summary>
    [ObservableProperty] private int wordsFormatIndex;

    /// <summary>Live sample so he can see which wording he is choosing.</summary>
    public string WordsPreview =>
        NumberToWords.RupeesInWords(Total > 0 ? Total : 951250m, WordsFormatIndex == 1);

    public ObservableCollection<SaleLineViewModel> Lines { get; } = new();
    public ObservableCollection<PartyRow> Customers { get; } = new();
    public ObservableCollection<Item> AvailableItems { get; } = new();

    public IReadOnlyList<PaymentMode> PaymentModes { get; } =
        Enum.GetValues<PaymentMode>().ToList();

    public IReadOnlyList<string> PaymentTypes { get; } = new[] { "Credit", "Cash" };

    public decimal SubTotal => Money.Round(Lines.Sum(l => l.Amount));
    public decimal Total => Money.Round(SubTotal - Discount);
    public decimal Balance => Money.Round(Total - PaidNow);
    public string TotalInWords => NumberToWords.RupeesInWords(Total);

    public SaleViewModel(SaleService sales, ItemService items, PartyService parties,
                         StockService stock, InvoiceBuilder invoices)
    {
        _sales = sales;
        _items = items;
        _parties = parties;
        _stock = stock;
        _invoices = invoices;

        Lines.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (SaleLineViewModel l in e.NewItems)
                {
                    l.StockLookup = ItemOnHand;
                    l.PropertyChanged += OnLineChanged;
                }
            if (e.OldItems is not null)
                foreach (SaleLineViewModel l in e.OldItems)
                    l.PropertyChanged -= OnLineChanged;
            RefreshTotals();
        };

        LoadLookups();
        AddLine();
    }

    private decimal ItemOnHand(int itemId) => _items.OnHand(itemId);

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SaleLineViewModel.Amount)
                           or nameof(SaleLineViewModel.Qty)
                           or nameof(SaleLineViewModel.Rate))
            RefreshTotals();
    }

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(SubTotal));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Balance));
        OnPropertyChanged(nameof(TotalInWords));
        OnPropertyChanged(nameof(WordsPreview));
    }

    partial void OnDiscountChanged(decimal value) => RefreshTotals();
    partial void OnPaidNowChanged(decimal value)
    {
        RefreshTotals();
        // Paying in full at the counter is a cash sale; anything less is credit.
        PaymentTypeLabel = PaidNow >= Total && Total > 0 ? "Cash" : "Credit";
    }

    partial void OnCustomerChanged(PartyRow? value) =>
        CustomerBalance = value?.Balance ?? 0m;

    partial void OnWordsFormatIndexChanged(int value) => OnPropertyChanged(nameof(WordsPreview));

    public void LoadLookups()
    {
        Customers.Clear();
        foreach (var p in _parties.Search(null, null, includeInactive: false))
            Customers.Add(p);

        AvailableItems.Clear();
        foreach (var i in _items.Search(null, null, includeInactive: false))
            AvailableItems.Add(i);

        NextInvoiceNo = _sales.PeekNextInvoiceNo();
    }

    [RelayCommand]
    private void AddLine() => Lines.Add(new SaleLineViewModel { StockLookup = ItemOnHand });

    [RelayCommand]
    private void RemoveLine(SaleLineViewModel? line)
    {
        if (line is not null) Lines.Remove(line);
        if (Lines.Count == 0) AddLine();
    }

    private InvoicePrintOptions CurrentOptions() => new()
    {
        ShowReceivedAmount = ShowReceivedAmount,
        ShowBalanceAmount = ShowBalanceAmount,
        ShowPartyCurrentBalance = ShowPartyCurrentBalance,
        ShowTotalItemQuantity = ShowTotalItemQuantity,
        ShowDecimals = ShowDecimals,
        GroupDigits = GroupDigits,
        AmountInWordsIndianFormat = WordsFormatIndex == 1,
        ExpandTableToWholePage = ExpandTableToWholePage,
        MinimumRowsInItemTable = MinimumRowsInItemTable,
        PaymentType = PaymentTypeLabel
    };

    /// <summary>Back to the OFF state shown in his first screenshot.</summary>
    public void ResetPrintOptions()
    {
        ShowReceivedAmount = false;
        ShowBalanceAmount = false;
        ShowPartyCurrentBalance = false;
        ShowTotalItemQuantity = false;
        ShowDecimals = false;
        GroupDigits = true;
        ExpandTableToWholePage = true;
        MinimumRowsInItemTable = 0;
        WordsFormatIndex = 0;
    }

    /// <summary>Opens the Invoice Print dialog. Set by the view.</summary>
    public Action? OpenPrintSettings { get; set; }

    /// <summary>
    /// Raised after an invoice is written. Set by whatever is hosting the form
    /// so it can close the window and refresh the list behind it. Null when the
    /// form is shown on its own.
    /// </summary>
    public Action? OnSaved { get; set; }

    [RelayCommand]
    private void PrintSettings() => OpenPrintSettings?.Invoke();

    [RelayCommand]
    private void Save() => SaveInternal(printAfter: false);

    [RelayCommand]
    private void SaveAndPrint() => SaveInternal(printAfter: true);

    private void SaveInternal(bool printAfter)
    {
        if (Customer is null)
        {
            MessageBox.Show("Choose a party.", "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var filled = Lines.Where(l => l.Item is not null && l.Qty > 0).ToList();
        if (filled.Count == 0)
        {
            MessageBox.Show("Add at least one item with a quantity.", "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var short_ = filled.Where(l => l.ShortOfStock).ToList();
        if (short_.Count > 0)
        {
            var detail = string.Join("\n", short_.Select(l =>
                $"{l.Item!.Name}: asked {l.Qty:0.###}, available {l.Available:0.###} {l.UnitLabel}"));
            MessageBox.Show($"Not enough stock:\n\n{detail}", "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var input = new SaleInput(
            Customer.Id, Date, Discount, Notes,
            // BatchId null lets FEFO pick: soonest expiry goes out first.
            filled.Select(l => new SaleLineInput(
                l.Item!.Id, null, l.Qty, l.Rate, l.PackQty)).ToList(),
            PaidNow, PaymentMode, CurrentOptions());

        Sale sale;
        try
        {
            sale = _sales.Create(input);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = $"Invoice {sale.InvoiceNo} saved. Stock updated.";

        if (printAfter) PrintInvoice(sale.Id);

        NewSale();
        LoadLookups();

        OnSaved?.Invoke();
    }

    private void PrintInvoice(int saleId)
    {
        try
        {
            var built = _invoices.Build(saleId);
            if (built is null) return;

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ShopApp Invoices");
            Directory.CreateDirectory(folder);

            var path = Path.Combine(folder, $"Invoice-{built.Value.Data.InvoiceNo}.pdf");
            InvoicePrinter.SavePdf(built.Value.Data, built.Value.Options, path);

            // Opens in the default PDF viewer, where he can print or save.
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"The invoice was saved, but printing failed:\n{ex.Message}",
                "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void NewSale()
    {
        Customer = null;
        Date = DateTime.Today;
        Discount = 0m;
        PaidNow = 0m;
        Notes = null;
        PaymentTypeLabel = "Credit";
        Lines.Clear();
        AddLine();
    }
}
