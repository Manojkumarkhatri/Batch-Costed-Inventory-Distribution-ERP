using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

/// <summary>One row of the item list: the item plus what is on the shelf.</summary>
public record ItemListRow(Item Item, decimal OnHand, decimal StockValue)
{
    public string Name => Item.Name;
    public string BaseUnit => Item.BaseUnit;
    public bool IsActive => Item.IsActive;
}

/// <summary>
/// What the item dialog hands back. Opening stock is separate from the item
/// because it cannot be written until the item has an id.
/// </summary>
public record ItemEditResult(bool Saved, (decimal Qty, decimal Cost, DateTime? Expiry)? OpeningStock);

/// <summary>What the adjustment dialog hands back when the user confirms.</summary>
public record AdjustRequest(int BatchId, decimal SignedQty, AdjustmentReason Reason, string? Note);

public partial class ItemsViewModel : ObservableObject
{
    private readonly ItemService _items;
    private readonly ItemImportService _import;
    private readonly StockService _stock;
    private readonly OpeningStockService _opening;

    [ObservableProperty] private string? searchTerm;
    [ObservableProperty] private ItemCategory? categoryFilter;
    [ObservableProperty] private bool showInactive;
    [ObservableProperty] private string statusMessage = "";

    [ObservableProperty] private ItemListRow? selectedRow;

    // Detail pane. Read from the database when a row is picked, so the panel
    // never shows a stale copy of something edited in a dialog.
    [ObservableProperty] private Item? selectedItem;
    [ObservableProperty] private decimal selectedOnHand;
    [ObservableProperty] private decimal selectedStockValue;

    public ObservableCollection<ItemListRow> Items { get; } = new();
    public ObservableCollection<ItemTxnRow> History { get; } = new();

    public IReadOnlyList<ItemCategory> Categories { get; } =
        Enum.GetValues<ItemCategory>().ToList();

    /// <summary>
    /// Set by the view. Opens the Add/Edit dialog on the item passed in and
    /// returns true if the user pressed Save. The dialog edits the object
    /// directly, so a rejected save can be reopened with the typed values intact.
    /// </summary>
    public Func<Item, ItemEditResult>? ShowEditor { get; set; }

    /// <summary>Set by the view. Returns null when the user cancels.</summary>
    public Func<Item, IReadOnlyList<ItemBatchRow>, AdjustRequest?>? ShowAdjust { get; set; }

    public ItemsViewModel(ItemService items, ItemImportService import, StockService stock,
                          OpeningStockService opening)
    {
        _items = items;
        _import = import;
        _stock = stock;
        _opening = opening;
        Load();
    }

    public bool HasSelection => SelectedItem is not null;

    // Typing in the search box or flipping a filter reloads the list.
    partial void OnSearchTermChanged(string? value) => Load();
    partial void OnCategoryFilterChanged(ItemCategory? value) => Load();
    partial void OnShowInactiveChanged(bool value) => Load();

    partial void OnSelectedRowChanged(ItemListRow? value)
    {
        if (value is null) { ClearDetail(); return; }
        ShowDetail(value.Item.Id);
    }

    private void ClearDetail()
    {
        SelectedItem = null;
        SelectedOnHand = 0m;
        SelectedStockValue = 0m;
        History.Clear();
        OnPropertyChanged(nameof(HasSelection));
    }

    private void ShowDetail(int itemId)
    {
        SelectedItem = _items.GetById(itemId);
        if (SelectedItem is null) { ClearDetail(); return; }

        var stock = _items.StockOf(itemId);
        SelectedOnHand = stock.OnHand;
        SelectedStockValue = stock.StockValue;

        History.Clear();
        foreach (var row in _items.History(itemId)) History.Add(row);

        OnPropertyChanged(nameof(HasSelection));
    }

    public void Load()
    {
        var keepId = SelectedRow?.Item.Id ?? SelectedItem?.Id ?? 0;

        var found = _items.Search(SearchTerm, CategoryFilter, ShowInactive);
        var stock = _items.StockFor(found.Select(i => i.Id).ToList());

        Items.Clear();
        foreach (var i in found)
        {
            var s = stock.GetValueOrDefault(i.Id, new ItemStock(0m, 0m));
            Items.Add(new ItemListRow(i, s.OnHand, s.StockValue));
        }

        // Setting SelectedRow re-reads the detail pane, which is what we want:
        // it picks up anything the dialog just changed.
        SelectedRow = keepId > 0 ? Items.FirstOrDefault(r => r.Item.Id == keepId) : null;
        if (SelectedRow is null) ClearDetail();

        StatusMessage = $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
    }

    [RelayCommand]
    private void New() => Persist(new Item
    {
        BaseUnit = "Kg",
        ConversionFactor = 1m,
        Category = ItemCategory.Solid,
        NearExpiryDays = 30,
        IsActive = true
    });

    [RelayCommand]
    private void Edit()
    {
        if (SelectedItem is not { Id: > 0 } current) return;

        // A fresh copy from disk, so a cancelled dialog leaves nothing behind
        // and the form never edits the object the list is displaying.
        var draft = _items.GetById(current.Id);
        if (draft is not null) Persist(draft);
    }

    /// <summary>
    /// Opens the editor, saves, and reopens it on failure with the user's
    /// typing still in place rather than throwing the whole form away.
    /// </summary>
    private void Persist(Item draft)
    {
        if (ShowEditor is null) return;

        while (true)
        {
            var edit = ShowEditor(draft);
            if (!edit.Saved) return;                   // cancelled

            var outcome = _items.Save(draft);

            if (!outcome.Success)
            {
                MessageBox.Show(outcome.ErrorText, "Cannot save",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(outcome.WarningText))
                MessageBox.Show(outcome.WarningText, "Saved - please check",
                    MessageBoxButton.OK, MessageBoxImage.Information);

            // Opening stock goes in only once the item has an id. The service
            // refuses a second opening entry for the same item, so a failed
            // save followed by a retry cannot double it up.
            if (edit.OpeningStock is { } opening)
            {
                var stocked = _opening.Save(DateTime.Today, new[]
                {
                    new OpeningStockLineInput(outcome.Id, null, opening.Qty, opening.Cost,
                                              null, opening.Expiry, null)
                });

                if (!stocked.Success)
                    MessageBox.Show(
                        $"{draft.Name} was saved, but its opening stock was not:\n\n{stocked.ErrorText}\n\n" +
                        "Add it from Stock, Opening Stock.",
                        "Opening stock", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Load();
            SelectedRow = Items.FirstOrDefault(r => r.Item.Id == outcome.Id);
            StatusMessage = $"Saved {draft.Name}.  {Items.Count} item{(Items.Count == 1 ? "" : "s")}";
            return;
        }
    }

    [RelayCommand]
    private void Adjust()
    {
        if (SelectedItem is not { Id: > 0 } item) return;
        if (ShowAdjust is null) return;

        var batches = _items.BatchesOf(item.Id);
        if (batches.Count == 0)
        {
            MessageBox.Show(
                $"{item.Name} has no batch with stock left, so there is nothing to adjust.\n\n" +
                "Record a purchase or opening stock first.",
                "Nothing to adjust", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var request = ShowAdjust(item, batches);
        if (request is null) return;

        _stock.Adjust(request.BatchId, request.SignedQty, request.Reason, request.Note);

        Load();
        StatusMessage = $"Adjusted {item.Name} by {request.SignedQty:N3} ({request.Reason}).";
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedItem is not { Id: > 0 } item) return;

        var confirm = MessageBox.Show(
            $"Remove {item.Name}?", "Confirm",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var outcome = _items.Remove(item.Id);

        if (!outcome.Success)
        {
            MessageBox.Show(outcome.ErrorText, "Cannot remove",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(outcome.WarningText))
            MessageBox.Show(outcome.WarningText, "Done",
                MessageBoxButton.OK, MessageBoxImage.Information);

        SelectedRow = null;
        Load();
    }

    [RelayCommand]
    private void Import()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a CSV file of items",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        string text;
        try { text = File.ReadAllText(dialog.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not read the file:\n{ex.Message}", "Import",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var summary = _import.Import(text, updateExisting: false);

        var message = summary.Describe();
        if (summary.Errors.Count > 0)
        {
            var shown = string.Join("\n", summary.Errors.Take(15));
            if (summary.Errors.Count > 15)
                shown += $"\n...and {summary.Errors.Count - 15} more.";
            message += $"\n\nProblems:\n{shown}";
        }

        MessageBox.Show(message, "Import finished",
            MessageBoxButton.OK,
            summary.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

        Load();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchTerm = null;
        CategoryFilter = null;
        ShowInactive = false;
    }
}
