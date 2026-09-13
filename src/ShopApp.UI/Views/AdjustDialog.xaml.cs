using System.Globalization;
using System.Windows;
using ShopApp.Domain.Entities;
using ShopApp.Domain.Enums;
using ShopApp.Services;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

/// <summary>
/// Records damage, spillage, expiry or a count correction against one batch.
/// The batch matters: quantity alone would not say which cost left the shelf,
/// and the shrinkage report is only useful if it can price what was lost.
/// </summary>
public partial class AdjustDialog : Window
{
    private readonly Item _item;
    private readonly IReadOnlyList<ItemBatchRow> _batches;

    /// <summary>Filled in once the user confirms; null if they cancelled.</summary>
    public AdjustRequest? Request { get; private set; }

    public AdjustDialog(Item item, IReadOnlyList<ItemBatchRow> batches)
    {
        InitializeComponent();
        _item = item;
        _batches = batches;

        HeaderText.Text = $"Adjust stock - {item.Name}";

        BatchBox.ItemsSource = batches;
        BatchBox.SelectedIndex = 0;

        ReasonBox.ItemsSource = Enum.GetValues<AdjustmentReason>().ToList();
        ReasonBox.SelectedItem = AdjustmentReason.Damage;

        Loaded += (_, _) => QtyBox.Focus();
    }

    private void BatchBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        BatchNote.Text = BatchBox.SelectedItem is ItemBatchRow b
            ? $"{b.OnHand:N3} {_item.BaseUnit} left in this batch."
            : "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (BatchBox.SelectedItem is not ItemBatchRow batch)
        {
            Warn("Choose which batch this affects.");
            return;
        }

        if (ReasonBox.SelectedItem is not AdjustmentReason reason)
        {
            Warn("Choose what happened.");
            return;
        }

        if (!decimal.TryParse(QtyBox.Text, NumberStyles.Number,
                              CultureInfo.CurrentCulture, out var qty) || qty <= 0)
        {
            Warn("Enter the quantity as a positive number. The direction is set above.");
            return;
        }

        var signed = DecreaseRadio.IsChecked == true ? -qty : qty;

        // Taking out more than the batch holds would drive it negative, and
        // on-hand is derived from these rows, so nothing downstream could
        // recover from it.
        if (signed < 0 && qty > batch.OnHand)
        {
            Warn($"That batch only has {batch.OnHand:N3} {_item.BaseUnit} left. " +
                 "Reduce the quantity, or split the adjustment across batches.");
            return;
        }

        var note = string.IsNullOrWhiteSpace(NoteBox.Text) ? null : NoteBox.Text.Trim();

        Request = new AdjustRequest(batch.BatchId, signed, reason, note);
        DialogResult = true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Check this first",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
