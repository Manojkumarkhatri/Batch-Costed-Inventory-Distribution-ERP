using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Input;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;

        Loaded += async (_, _) =>
        {
            await vm.LoadAsync();
            vm.NavigateCommand.Execute("home");
        };
    }

    /// <summary>
    /// Opens the chart full size, on its own range and granularity. Double
    /// click, because a single click on a card that large would fire whenever
    /// somebody moved the mouse across the dashboard.
    /// </summary>
    private void SalesChart_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;

        var sales = App.Services.GetRequiredService<ShopApp.Services.SaleService>();
        var today = DateTime.Today;

        new SalesChartWindow(sales, new DateTime(today.Year, 1, 1), today)
        {
            Owner = this
        }.ShowDialog();
    }

    /// <summary>
    /// A flyout entry has done its navigating; drop the menu. The Command on
    /// the button has already run by the time Click fires, so this only has to
    /// untick the rail toggle that opened the popup.
    /// </summary>
    private void FlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        SaleMenu.IsChecked = false;
        PurchaseMenu.IsChecked = false;
        StockMenu.IsChecked = false;
    }
}
