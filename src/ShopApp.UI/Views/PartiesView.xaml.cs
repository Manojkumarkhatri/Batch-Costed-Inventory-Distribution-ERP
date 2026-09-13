using System.Windows;
using System.Windows.Controls;
using ShopApp.Domain.Entities;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class PartiesView : UserControl
{
    public PartiesView(PartiesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // The view model decides when to edit; the view knows how to show a
        // window. Same split the Items and Reports screens use.
        vm.ShowEditor = ShowEditor;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.Load();
        };
    }

    private bool ShowEditor(Party party, IReadOnlyList<string> groups)
    {
        var dialog = new PartyDialog(party, groups) { Owner = Window.GetWindow(this) };
        return dialog.ShowDialog() == true;
    }
}
