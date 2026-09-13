using System.Windows;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

/// <summary>
/// Mirrors the Vyapar "Invoice Print" screen he already knows, so the options
/// sit where he expects them. Binds straight to the SaleViewModel, so whatever
/// is ticked here applies to the invoice he is currently writing.
/// </summary>
public partial class InvoicePrintSettingsWindow : Window
{
    private readonly SaleViewModel _vm;

    public InvoicePrintSettingsWindow(SaleViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void ResetDefaults_Click(object sender, RoutedEventArgs e) =>
        _vm.ResetPrintOptions();
}
