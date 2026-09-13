using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Domain.Logic;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

/// <summary>One editable row on the purchase screen.</summary>
public partial class PurchaseLineViewModel : ObservableObject
{
    [ObservableProperty] private Item? item;
    [ObservableProperty] private string? batchNo;
    [ObservableProperty] private DateTime? mfgDate;
    [ObservableProperty] private DateTime? expiryDate;
    [ObservableProperty] private decimal? packQty;
    [ObservableProperty] private decimal qty;
    [ObservableProperty] private decimal rate;
    [ObservableProperty] private string? storageLocation;

    public decimal Amount => Money.Round(Qty * Rate);

    /// <summary>Shown next to the quantity box, e.g. "Kg" or "Kg (10 Sack)".</summary>
    public string UnitLabel => Item is null
        ? ""
        : Item.AltUnit is null
            ? Item.BaseUnit
            : $"{Item.BaseUnit} / {Item.AltUnit}";

    public bool ShowPackQty => Item?.AltUnit is not null;

    partial void OnItemChanged(Item? value)
    {
        if (value is null) return;

        if (Rate == 0) Rate = value.DefaultPurchasePrice;

        // Perishables get a sensible default expiry so it isn't silently skipped.
        if (ExpiryDate is null && value.Category is ItemCategory.Frozen or ItemCategory.Liquid)
            ExpiryDate = DateTime.Today.AddMonths(6);

        OnPropertyChanged(nameof(UnitLabel));
        OnPropertyChanged(nameof(ShowPackQty));
        RecalculateFromPack();
    }

    partial void OnPackQtyChanged(decimal? value) => RecalculateFromPack();

    /// <summary>
    /// Packs to base units, e.g. 10 sacks x 50kg = 500kg. Skipped for
    /// variable-weight goods, where the weighed amount is authoritative and
    /// the pack count is only a reference.
    /// </summary>
    private void RecalculateFromPack()
    {
        if (Item is null || PackQty is not decimal packs) return;
        if (Item.IsVariableWeight) return;
        if (Item.ConversionFactor <= 0) return;

        Qty = UnitConverter.PackToBase(packs, Item.ConversionFactor);
    }

    partial void OnQtyChanged(decimal value) => OnPropertyChanged(nameof(Amount));
    partial void OnRateChanged(decimal value) => OnPropertyChanged(nameof(Amount));
}

public partial class PurchaseViewModel : ObservableObject
{
    private readonly PurchaseService _purchases;
    private readonly ItemService _items;
    private readonly PartyService _parties;

    [ObservableProperty] private PartyRow? supplier;
    [ObservableProperty] private string? supplierBillNo;
    [ObservableProperty] private DateTime date = DateTime.Today;
    [ObservableProperty] private decimal discount;
    [ObservableProperty] private decimal otherCharges;
    [ObservableProperty] private decimal paidNow;
    [ObservableProperty] private PaymentMode paymentMode = PaymentMode.Cash;
    [ObservableProperty] private string? notes;
    [ObservableProperty] private string statusMessage = "";

    public ObservableCollection<PurchaseLineViewModel> Lines { get; } = new();
    public ObservableCollection<PartyRow> Suppliers { get; } = new();
    public ObservableCollection<Item> AvailableItems { get; } = new();
    public ObservableCollection<Purchase> RecentPurchases { get; } = new();

    public IReadOnlyList<PaymentMode> PaymentModes { get; } =
        Enum.GetValues<PaymentMode>().ToList();

    public decimal SubTotal => Money.Round(Lines.Sum(l => l.Amount));
    public decimal Total => Money.Round(SubTotal - Discount + OtherCharges);
    public decimal Balance => Money.Round(Total - PaidNow);

    public PurchaseViewModel(PurchaseService purchases, ItemService items, PartyService parties)
    {
        _purchases = purchases;
        _items = items;
        _parties = parties;

        Lines.CollectionChanged += (_, e) =>
        {
            // Watch each row so edits inside the grid update the totals.
            if (e.NewItems is not null)
                foreach (PurchaseLineViewModel l in e.NewItems)
                    l.PropertyChanged += OnLineChanged;
            if (e.OldItems is not null)
                foreach (PurchaseLineViewModel l in e.OldItems)
                    l.PropertyChanged -= OnLineChanged;
            RefreshTotals();
        };

        LoadLookups();
        AddLine();
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PurchaseLineViewModel.Amount)
                           or nameof(PurchaseLineViewModel.Qty)
                           or nameof(PurchaseLineViewModel.Rate))
            RefreshTotals();
    }

    private void RefreshTotals()
    {
        OnPropertyChanged(nameof(SubTotal));
        OnPropertyChanged(nameof(Total));
        OnPropertyChanged(nameof(Balance));
    }

    partial void OnDiscountChanged(decimal value) => RefreshTotals();
    partial void OnOtherChargesChanged(decimal value) => RefreshTotals();
    partial void OnPaidNowChanged(decimal value) => RefreshTotals();

    public void LoadLookups()
    {
        Suppliers.Clear();
        // No customer/supplier split any more - any party can supply him.
        foreach (var s in _parties.Search(null, null, includeInactive: false))
            Suppliers.Add(s);

        AvailableItems.Clear();
        foreach (var i in _items.Search(null, null, includeInactive: false))
            AvailableItems.Add(i);

        RecentPurchases.Clear();
        foreach (var p in _purchases.Recent(30))
            RecentPurchases.Add(p);
    }

    [RelayCommand]
    private void AddLine() => Lines.Add(new PurchaseLineViewModel { Qty = 0m, Rate = 0m });

    [RelayCommand]
    private void RemoveLine(PurchaseLineViewModel? line)
    {
        if (line is not null) Lines.Remove(line);
        if (Lines.Count == 0) AddLine();
    }

    [RelayCommand]
    private void Save()
    {
        if (Supplier is null)
        {
            MessageBox.Show("Choose a supplier.", "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var filled = Lines.Where(l => l.Item is not null).ToList();
        if (filled.Count == 0)
        {
            MessageBox.Show("Add at least one item.", "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var input = new PurchaseInput(
            Supplier.Id, SupplierBillNo, Date, Discount, OtherCharges, Notes,
            filled.Select(l => new PurchaseLineInput(
                l.Item!.Id, l.BatchNo, l.MfgDate, l.ExpiryDate,
                l.PackQty, l.Qty, l.Rate, l.StorageLocation)).ToList(),
            PaidNow, PaymentMode);

        var result = _purchases.Create(input);

        if (!result.Success)
        {
            MessageBox.Show(result.ErrorText, "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.WarningText))
            MessageBox.Show(result.WarningText, "Saved - please check",
                MessageBoxButton.OK, MessageBoxImage.Information);

        StatusMessage = $"Purchase saved. Stock updated.";
        NewPurchase();
        LoadLookups();

        OnSaved?.Invoke();
    }

    /// <summary>
    /// Raised after a bill is written. Set by whatever is hosting the form so
    /// it can close the window and refresh the list behind it. Null when the
    /// form is shown on its own.
    /// </summary>
    public Action? OnSaved { get; set; }

    [RelayCommand]
    private void NewPurchase()
    {
        Supplier = null;
        SupplierBillNo = null;
        Date = DateTime.Today;
        Discount = 0m;
        OtherCharges = 0m;
        PaidNow = 0m;
        Notes = null;
        Lines.Clear();
        AddLine();
    }
}
