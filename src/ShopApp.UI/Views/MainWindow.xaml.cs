using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using ShopApp.Services;
using ShopApp.UI.ViewModels;

namespace ShopApp.UI.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly PasscodeService _passcodes;

    // Idle locking. Reset by any keystroke or mouse movement rather than by a
    // screen change: reading a report for ten minutes without touching
    // anything is exactly when he is least likely to be at the counter.
    private readonly DispatcherTimer _idle = new() { Interval = TimeSpan.FromSeconds(30) };
    private DateTime _lastActivity = DateTime.Now;
    private bool _locking;

    public MainWindow(MainViewModel vm, PasscodeService passcodes)
    {
        InitializeComponent();
        DataContext = vm;
        _vm = vm;
        _passcodes = passcodes;

        // Handled events count too - typing into a text box is still activity,
        // and the normal events are marked handled before they reach here.
        AddHandler(PreviewKeyDownEvent,
            new KeyEventHandler((_, _) => _lastActivity = DateTime.Now), true);
        AddHandler(PreviewMouseDownEvent,
            new MouseButtonEventHandler((_, _) => _lastActivity = DateTime.Now), true);
        PreviewMouseMove += (_, _) => _lastActivity = DateTime.Now;

        _idle.Tick += (_, _) => CheckIdle();
        _idle.Start();

        Loaded += async (_, _) =>
        {
            await vm.LoadAsync();
            vm.NavigateCommand.Execute("home");
        };
    }

    /// <summary>
    /// Locks once the app has been left alone long enough. Zero minutes in
    /// Settings means he chose to be asked only at startup.
    /// </summary>
    private void CheckIdle()
    {
        if (_locking) return;
        if (!_passcodes.IsConfigured()) return;

        var minutes = _passcodes.AutoLockMinutes();
        if (minutes <= 0) return;

        if ((DateTime.Now - _lastActivity).TotalMinutes < minutes) return;

        Lock();
    }

    /// <summary>
    /// Hides rather than closes, so whatever he had open is still there when
    /// he comes back and nothing half-entered is lost.
    /// </summary>
    private void Lock()
    {
        _locking = true;
        _idle.Stop();

        try
        {
            Hide();
            AppLog.Info("Locked after idle");

            if (new LockWindow(_passcodes, relock: true).ShowDialog() == true)
            {
                Show();
                Activate();
                _lastActivity = DateTime.Now;
                _idle.Start();
            }
            else
            {
                Application.Current.Shutdown();
            }
        }
        finally
        {
            _locking = false;
        }
    }

    /// <summary>
    /// Opens the chart full size, on its own range and granularity. Double
    /// click, because a single click on a card that large would fire whenever
    /// somebody moved the mouse across the dashboard.
    /// </summary>
    private void SalesChart_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2) return;

        var sales = App.Services.GetRequiredService<SaleService>();
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
