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

public partial class OpeningStockLineViewModel : ObservableObject
{
    [ObservableProperty] private Item? item;
    [ObservableProperty] private string? batchNo;
    [ObservableProperty] private decimal? packQty;
    [ObservableProperty] private decimal qty;
    [ObservableProperty] private decimal costPrice;
    [ObservableProperty] private DateTime? mfgDate;
    [ObservableProperty] private DateTime? expiryDate;
    [ObservableProperty] private string? storageLocation;
    [ObservableProperty] private bool alreadyEntered;

    public decimal Value => Money.Round(Qty * CostPrice);
    public string UnitLabel => Item is null ? "" :
        Item.AltUnit is null ? Item.BaseUnit : $"{Item.BaseUnit} / {Item.AltUnit}";

    public Func<int, bool>? OpeningLookup { get; set; }

    partial void OnItemChanged(Item? value)
    {
        if (value is null) return;

        if (CostPrice == 0) CostPrice = value.DefaultPurchasePrice;
        AlreadyEntered = OpeningLookup?.Invoke(value.Id) ?? false;

        if (ExpiryDate is null && value.Category is ItemCategory.Frozen or ItemCategory.Liquid)
            ExpiryDate = DateTime.Today.AddMonths(3);

        OnPropertyChanged(nameof(UnitLabel));
        RecalculateFromPack();
    }

    partial void OnPackQtyChanged(decimal? value) => RecalculateFromPack();

    private void RecalculateFromPack()
    {
        if (Item is null || PackQty is not decimal packs) return;
        if (Item.IsVariableWeight || Item.ConversionFactor <= 0) return;
        Qty = UnitConverter.PackToBase(packs, Item.ConversionFactor);
    }

    partial void OnQtyChanged(decimal value) => OnPropertyChanged(nameof(Value));
    partial void OnCostPriceChanged(decimal value) => OnPropertyChanged(nameof(Value));
}

public partial class OpeningStockViewModel : ObservableObject
{
    private readonly OpeningStockService _opening;
    private readonly ItemService _items;

    [ObservableProperty] private DateTime asOfDate = DateTime.Today;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private decimal existingOpeningValue;

    public ObservableCollection<OpeningStockLineViewModel> Lines { get; } = new();
    public ObservableCollection<Item> AvailableItems { get; } = new();

    public decimal TotalValue => Money.Round(Lines.Sum(l => l.Value));

    public OpeningStockViewModel(OpeningStockService opening, ItemService items)
    {
        _opening = opening;
        _items = items;

        Lines.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (OpeningStockLineViewModel l in e.NewItems)
                {
                    l.OpeningLookup = _opening.HasOpening;
                    l.PropertyChanged += OnLineChanged;
                }
            if (e.OldItems is not null)
                foreach (OpeningStockLineViewModel l in e.OldItems)
                    l.PropertyChanged -= OnLineChanged;
            OnPropertyChanged(nameof(TotalValue));
        };

        Load();
        AddLine();
    }

    private void OnLineChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OpeningStockLineViewModel.Value)
                           or nameof(OpeningStockLineViewModel.Qty)
                           or nameof(OpeningStockLineViewModel.CostPrice))
            OnPropertyChanged(nameof(TotalValue));
    }

    public void Load()
    {
        AvailableItems.Clear();
        foreach (var i in _items.Search(null, null, includeInactive: false))
            AvailableItems.Add(i);

        ExistingOpeningValue = _opening.OpeningStockValue();
    }

    [RelayCommand]
    private void AddLine() =>
        Lines.Add(new OpeningStockLineViewModel { OpeningLookup = _opening.HasOpening });

    /// <summary>
    /// Fills a row for every item at once. Faster than picking each from the
    /// dropdown when he is entering a whole godown in one sitting.
    /// </summary>
    [RelayCommand]
    private void AddAllItems()
    {
        var existing = Lines.Where(l => l.Item is not null).Select(l => l.Item!.Id).ToHashSet();
        foreach (var item in AvailableItems)
        {
            if (existing.Contains(item.Id)) continue;
            Lines.Add(new OpeningStockLineViewModel
            {
                OpeningLookup = _opening.HasOpening,
                Item = item
            });
        }
        StatusMessage = $"{Lines.Count} rows ready. Fill in quantity and cost, delete what he does not stock.";
    }

    [RelayCommand]
    private void RemoveLine(OpeningStockLineViewModel? line)
    {
        if (line is not null) Lines.Remove(line);
        if (Lines.Count == 0) AddLine();
    }

    [RelayCommand]
    private void RemoveEmptyRows()
    {
        foreach (var l in Lines.Where(x => x.Item is null || x.Qty <= 0).ToList())
            Lines.Remove(l);
        if (Lines.Count == 0) AddLine();
    }

    [RelayCommand]
    private void Save()
    {
        var filled = Lines.Where(l => l.Item is not null && l.Qty > 0).ToList();
        if (filled.Count == 0)
        {
            MessageBox.Show("Add at least one item with a quantity.", "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // This becomes the app's truth for ever, so make him confirm once.
        var confirm = MessageBox.Show(
            $"Save opening stock for {filled.Count} item(s), " +
            $"total value {TotalValue:N2}, as on {AsOfDate:dd-MM-yyyy}?\n\n" +
            "These figures become the starting point for every stock and profit report. " +
            "Make sure they come from a physical count, not from memory.",
            "Confirm opening stock", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var result = _opening.Save(AsOfDate, filled.Select(l => new OpeningStockLineInput(
            l.Item!.Id, l.BatchNo, l.Qty, l.CostPrice,
            l.MfgDate, l.ExpiryDate, l.StorageLocation)).ToList());

        if (!result.Success)
        {
            MessageBox.Show(result.ErrorText, "Cannot save",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.WarningText))
            MessageBox.Show(result.WarningText, "Saved - please check",
                MessageBoxButton.OK, MessageBoxImage.Information);

        StatusMessage = $"Opening stock saved for {result.RowsSaved} item(s).";
        Lines.Clear();
        AddLine();
        Load();
    }
}
