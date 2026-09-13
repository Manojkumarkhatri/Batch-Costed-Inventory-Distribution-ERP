using System.Windows;
using System.Windows.Controls;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class SaleView : UserControl
{
    public SaleView(SaleViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        // Views are created once at startup, so the party and item lists would
        // otherwise be a snapshot taken before anything existed. Refresh every
        // time the tab is opened.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) vm.LoadLookups();
        };

        // The dialog binds to the same ViewModel, so ticking an option there
        // applies to the invoice currently being written.
        vm.OpenPrintSettings = () =>
        {
            var dialog = new InvoicePrintSettingsWindow(vm)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        };
    }
}
