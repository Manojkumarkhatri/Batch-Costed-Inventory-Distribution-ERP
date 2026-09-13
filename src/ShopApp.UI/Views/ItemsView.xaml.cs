using System.Windows;
using System.Windows.Controls;
using ShopApp.Domain.Entities;
using ShopApp.Services;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class ItemsView : UserControl
{
    public ItemsView(ItemsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // The view model decides when to edit; the view knows how to show a
        // window. Same split the Reports screen uses for its grid.
        vm.ShowEditor = ShowEditor;
        vm.ShowAdjust = ShowAdjust;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.Load();
        };
    }

    private bool ShowEditor(Item item)
    {
        var dialog = new ItemDialog(item) { Owner = Window.GetWindow(this) };
        return dialog.ShowDialog() == true;
    }

    private AdjustRequest? ShowAdjust(Item item, IReadOnlyList<ItemBatchRow> batches)
    {
        var dialog = new AdjustDialog(item, batches) { Owner = Window.GetWindow(this) };
        return dialog.ShowDialog() == true ? dialog.Request : null;
    }
}
