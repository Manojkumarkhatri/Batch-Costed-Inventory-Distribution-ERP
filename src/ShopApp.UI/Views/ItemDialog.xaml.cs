using System.ComponentModel;
using System.Windows;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;

namespace ShopApp.UI.Views;

/// <summary>
/// Add / edit one item. Edits the Item it is given directly, so a rejected
/// save can reopen the dialog with everything the user typed still in place.
/// The caller passes a detached copy, which is what makes Cancel safe.
/// </summary>
public partial class ItemDialog : Window
{
    private readonly Item _item;

    public IReadOnlyList<ItemCategory> Categories { get; } =
        Enum.GetValues<ItemCategory>().ToList();

    /// <summary>Suggestions only - the unit field stays free text.</summary>
    public IReadOnlyList<string> CommonUnits { get; } =
        new[] { "Kg", "Gram", "Litre", "ml", "Piece", "Dozen", "Sack", "Bag",
                "Carton", "Box", "Drum", "Can", "Bottle", "Crate", "Tray" };

    public ItemDialog(Item item)
    {
        InitializeComponent();
        _item = item;
        DataContext = item;

        var isNew = item.Id == 0;
        Title = isNew ? "Add Item" : "Edit Item";
        HeaderText.Text = isNew ? "Add Item" : $"Edit {item.Name}";

        item.PropertyChanged += OnItemChanged;
        Closed += (_, _) => item.PropertyChanged -= OnItemChanged;

        Loaded += (_, _) => { UpdateConversionPreview(); NameBox.Focus(); };
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Item.BaseUnit) or nameof(Item.AltUnit)
                           or nameof(Item.ConversionFactor))
            UpdateConversionPreview();
    }

    /// <summary>
    /// Live confirmation of the pack maths, e.g. "1 Carton = 10 Kg". Shown under
    /// the unit fields so a wrong factor is caught before it reaches a purchase.
    /// </summary>
    private void UpdateConversionPreview()
    {
        if (string.IsNullOrWhiteSpace(_item.AltUnit))
        {
            ConversionPreview.Text = "";
            return;
        }

        ConversionPreview.Text = _item.ConversionFactor <= 0
            ? $"Enter how many {_item.BaseUnit} are in one {_item.AltUnit}."
            : $"1 {_item.AltUnit} = {_item.ConversionFactor:0.###} {_item.BaseUnit}";
    }

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
