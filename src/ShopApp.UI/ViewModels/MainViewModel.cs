using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using ShopApp.Data;
using ShopApp.Domain.Logic;
using ShopApp.Services;
using ShopApp.UI.Views;

namespace ShopApp.UI.ViewModels;

// SalesPoint lives in its own file: the chart control and the expanded
// window both use it, and neither should depend on the dashboard.

public partial class MainViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly StockService _stock;
    private readonly BackupService _backup;
    private readonly PartyService _parties;

    /// <summary>Beyond this many days the chart switches from daily to monthly bars.</summary>
    private const int DailyBarLimit = 62;

    [ObservableProperty] private decimal todaySales;
    [ObservableProperty] private decimal totalReceivable;
    [ObservableProperty] private decimal totalPayable;
    [ObservableProperty] private int nearExpiryCount;
    [ObservableProperty] private int lowStockCount;
    [ObservableProperty] private string backupStatusText = "Checking backup...";
    [ObservableProperty] private Brush backupStatusBrush = Brushes.Gray;
    [ObservableProperty] private string statusMessage = "Ready";
    [ObservableProperty] private string? backupFolder;

    // ---------------------------------------------------------- sales chart

    [ObservableProperty] private string salesPeriod = "This Month";
    [ObservableProperty] private decimal periodSales;

    public ObservableCollection<SalesPoint> SalesTrend { get; } = new();

    /// <summary>
    /// The ranges he actually asks for. This year runs July to June, the way
    /// his books do.
    /// </summary>
    public IReadOnlyList<string> SalesPeriods { get; } =
        new[] { "This Week", "This Month", "Last Month", "This Quarter",
                "Half Year", "This Year" };

    partial void OnSalesPeriodChanged(string value) => _ = LoadAsync();

    // ----------------------------------------------------------- navigation

    /// <summary>Index into the chromeless TabControl that holds every screen.</summary>
    [ObservableProperty] private int selectedSection;

    /// <summary>Shown in the page header above the content.</summary>
    [ObservableProperty] private string sectionTitle = "Dashboard";

    /// <summary>
    /// Which rail button is lit. Sub-screens report their parent, so Near
    /// expiry keeps the Stock icon highlighted while it is open.
    /// </summary>
    [ObservableProperty] private string activeNav = "home";

    /// <summary>
    /// key -> (tab index, page title). The index must match the order of the
    /// TabItems in MainWindow.xaml; nothing else depends on it.
    /// </summary>
    private static readonly Dictionary<string, (int Index, string Title)> Sections = new()
    {
        ["home"] = (0, "Dashboard"),
        ["parties"] = (1, "Parties"),
        ["items"] = (2, "Items"),
        ["sale"] = (3, "Sale Invoices"),
        ["purchase"] = (4, "Purchase Bills"),
        ["reports"] = (5, "Reports"),
        ["stock"] = (6, "Stock Summary"),
        ["expiry"] = (7, "Near Expiry Stock"),
        ["opening"] = (8, "Opening Stock"),
        ["settings"] = (9, "Settings"),
        ["paymentin"] = (10, "Payment-In"),
        ["paymentout"] = (11, "Payment-Out"),
        ["expenses"] = (12, "Expenses"),
        ["banks"] = (13, "Banks")
    };

    /// <summary>Screens that show live figures and so need fresh data.</summary>
    private static readonly HashSet<string> NeedsRefresh = new() { "home", "stock", "expiry" };

    /// <summary>Which rail icon a screen belongs under.</summary>
    private static string RailGroup(string key) => key switch
    {
        "expiry" or "opening" => "stock",
        "paymentin" => "sale",
        "paymentout" => "purchase",
        _ => key
    };

    public ObservableCollection<StockRow> Stock { get; } = new();
    public ObservableCollection<ExpiryRow> NearExpiry { get; } = new();

    /// <summary>Hosted in the section host. Resolved via DI so each gets its own DbContext.</summary>
    public ItemsView ItemsView { get; }
    public PartiesView PartiesView { get; }
    public PurchaseListView PurchaseListView { get; }
    public SaleListView SaleListView { get; }
    public PaymentInView PaymentInView { get; }
    public PaymentOutView PaymentOutView { get; }
    public ExpensesView ExpensesView { get; }
    public BanksView BanksView { get; }
    public SettingsView SettingsView { get; }
    public OpeningStockView OpeningStockView { get; }
    public ReportsView ReportsView { get; }

    public MainViewModel(AppDbContext db, StockService stock, BackupService backup,
                         PartyService parties,
                         ItemsView itemsView, PartiesView partiesView,
                         PurchaseListView purchaseListView, SaleListView saleListView,
                         PaymentInView paymentInView, PaymentOutView paymentOutView,
                         ExpensesView expensesView, BanksView banksView,
                         SettingsView settingsView,
                         OpeningStockView openingStockView, ReportsView reportsView)
    {
        _db = db;
        _stock = stock;
        _backup = backup;
        _parties = parties;
        ItemsView = itemsView;
        PartiesView = partiesView;
        PurchaseListView = purchaseListView;
        SaleListView = saleListView;
        PaymentInView = paymentInView;
        PaymentOutView = paymentOutView;
        ExpensesView = expensesView;
        BanksView = banksView;
        SettingsView = settingsView;

        // The banner across the top reads the same settings this screen edits,
        // so it has to be told the moment a folder or passphrase is set -
        // otherwise it keeps saying the data is unprotected until a restart.
        if (settingsView.DataContext is SettingsViewModel svm)
            svm.OnChanged = RefreshBackupStatus;
        OpeningStockView = openingStockView;
        ReportsView = reportsView;
    }

    /// <summary>
    /// Every rail button, flyout entry and top-bar button routes through here.
    /// Screens that show live figures are refreshed on the way in - they used
    /// to load once at startup, so stock bought during the session never
    /// appeared until the app was restarted.
    /// </summary>
    [RelayCommand]
    private async Task Navigate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (!Sections.TryGetValue(key, out var section)) return;

        SelectedSection = section.Index;
        SectionTitle = section.Title;
        ActiveNav = RailGroup(key);

        if (NeedsRefresh.Contains(key)) await LoadAsync();
    }

    /// <summary>
    /// Dashboard shortcut: open Reports with one report already running.
    /// The Reports screen owns its own view model, so reach it through the view
    /// rather than resolving a second, unrelated instance from DI.
    /// </summary>
    [RelayCommand]
    private async Task OpenReport(string? reportId)
    {
        await Navigate("reports");

        if (string.IsNullOrWhiteSpace(reportId)) return;
        if (ReportsView.DataContext is not ReportsViewModel vm) return;

        var definition = ReportService.Catalogue.FirstOrDefault(r => r.Id == reportId);
        if (definition is not null) vm.SelectedReport = definition;
    }

    /// <summary>The span behind the chart and the Total Sale figure.</summary>
    private (DateTime From, DateTime To) PeriodRange()
    {
        var today = DateTime.Today;

        switch (SalesPeriod)
        {
            case "This Week":
                var monday = today.AddDays(-(int)today.DayOfWeek);
                return (monday, monday.AddDays(6));

            case "This Quarter":
                var qStart = new DateTime(today.Year, ((today.Month - 1) / 3) * 3 + 1, 1);
                return (qStart, qStart.AddMonths(3).AddDays(-1));

            case "Half Year":
                var hStart = today.AddMonths(-5);
                return (new DateTime(hStart.Year, hStart.Month, 1),
                        new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1));

            case "Last Month":
                var firstThis = new DateTime(today.Year, today.Month, 1);
                return (firstThis.AddMonths(-1), firstThis.AddDays(-1));

            case "This Year":
                // July to June, matching the financial year he reports on.
                var startYear = today.Month >= 7 ? today.Year : today.Year - 1;
                return (new DateTime(startYear, 7, 1), new DateTime(startYear + 1, 6, 30));

            default:
                var first = new DateTime(today.Year, today.Month, 1);
                return (first, first.AddMonths(1).AddDays(-1));
        }
    }

    /// <summary>
    /// Buckets the period into bars. A month gets one bar per day; a year would
    /// give 365 slivers, so anything longer than two months is rolled up by
    /// month instead. Quiet days keep their zero bar so the shape stays honest.
    /// </summary>
    private static List<(string Label, decimal Amount)> Buckets(
        DateTime from, DateTime to, Dictionary<DateTime, decimal> byDay)
    {
        var buckets = new List<(string, decimal)>();

        if ((to - from).Days <= DailyBarLimit)
        {
            for (var d = from; d <= to; d = d.AddDays(1))
                buckets.Add((d.ToString("d MMM"), byDay.GetValueOrDefault(d)));
            return buckets;
        }

        for (var m = new DateTime(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var total = byDay.Where(kv => kv.Key.Year == m.Year && kv.Key.Month == m.Month)
                             .Sum(kv => kv.Value);
            buckets.Add((m.ToString("MMM yy"), total));
        }

        return buckets;
    }

    public async Task LoadAsync()
    {
        await Task.Run(() =>
        {
            var today = DateTime.Today;
            var (from, to) = PeriodRange();

            // This context lives as long as the window, while purchases and
            // sales are written through separate short-lived ones. Clearing the
            // tracker guarantees the next reads come from disk, not from
            // entities this context happened to load earlier.
            _db.ChangeTracker.Clear();

            var sales = _db.Sales.AsNoTracking()
                .Where(s => !s.IsCancelled && s.Date >= from && s.Date <= to)
                .Select(s => new { s.Date, s.Total })
                .ToList();

            var todayTotals = _db.Sales.AsNoTracking()
                .Where(s => !s.IsCancelled && s.Date == today)
                .Select(s => s.Total)
                .ToList();

            TodaySales = Money.Round(todayTotals.Sum());
            PeriodSales = Money.Round(sales.Sum(s => s.Total));

            var byDay = sales.GroupBy(s => s.Date)
                             .ToDictionary(g => g.Key, g => Money.Round(g.Sum(x => x.Total)));

            var buckets = Buckets(from, to, byDay);
            var max = buckets.Count == 0 ? 0m : buckets.Max(b => b.Amount);

            var points = buckets.Select(b => new SalesPoint(b.Label, b.Amount)).ToList();

            // Positive balance means the party owes us; negative means we owe them.
            var allPartyIds = _db.Parties.AsNoTracking().Select(p => p.Id).ToList();
            var balances = _parties.BalancesFor(allPartyIds).Values.ToList();
            TotalReceivable = Money.Round(balances.Where(v => v > 0).Sum());
            TotalPayable = Money.Round(Math.Abs(balances.Where(v => v < 0).Sum()));

            var stockRows = _stock.StockSummary();
            var expiryRows = _stock.NearExpiry(30);

            LowStockCount = stockRows.Count(r => r.ReorderLevel > 0 && r.OnHand <= r.ReorderLevel);
            NearExpiryCount = expiryRows.Count;

            App.Current.Dispatcher.Invoke(() =>
            {
                Stock.Clear();
                foreach (var r in stockRows) Stock.Add(r);
                NearExpiry.Clear();
                foreach (var r in expiryRows) NearExpiry.Add(r);

                SalesTrend.Clear();
                foreach (var p in points) SalesTrend.Add(p);
            });
        });

        RefreshBackupStatus();
    }

    public async Task RefreshAsync() => await LoadAsync();

    /// <summary>
    /// The banner that stops a forgotten USB going unnoticed for three months.
    /// Green today, amber yesterday, red beyond that, red if never configured.
    /// </summary>
    public void RefreshBackupStatus()
    {
        var settings = _db.Settings.Single(s => s.Id == 1);
        BackupFolder = settings.BackupFolder;

        if (string.IsNullOrWhiteSpace(settings.BackupFolder))
        {
            BackupStatusText = "NO BACKUP SET UP - your data is not protected. Open Settings now.";
            BackupStatusBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
            return;
        }

        if (settings.LastBackupAt is not DateTime last)
        {
            BackupStatusText = "No backup has ever run. Press F9 to back up now.";
            BackupStatusBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
            return;
        }

        var days = (DateTime.Today - last.Date).Days;
        BackupStatusText = $"Last backup: {last:dd-MMM-yyyy HH:mm}" +
                           (days > 0 ? $"  ({days} day{(days == 1 ? "" : "s")} ago)" : "  (today)");

        BackupStatusBrush = days switch
        {
            0 => new SolidColorBrush(Color.FromRgb(0x15, 0x80, 0x3D)),   // green
            1 => new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09)),   // amber
            _ => new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C))    // red
        };

        // A configured folder that is not there almost always means unplugged USB.
        if (!Directory.Exists(settings.BackupFolder))
        {
            BackupStatusText = $"BACKUP DRIVE NOT FOUND ({settings.BackupFolder}). Plug it in.";
            BackupStatusBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
        }
    }

    [RelayCommand]
    private void BackupNow()
    {
        var settings = _db.Settings.Single(s => s.Id == 1);
        if (string.IsNullOrWhiteSpace(settings.BackupFolder))
        {
            StatusMessage = "Set a backup folder in Settings first.";
            return;
        }

        if (!SecureKeyStore.TryUnprotect(settings.ProtectedBackupKey, out var key))
        {
            StatusMessage = "Backup passphrase not set. Open Settings.";
            return;
        }

        var result = _backup.CreateBackup(settings.BackupFolder, key, settings.BackupKeepCount);
        if (result.Success)
        {
            settings.LastBackupAt = DateTime.Now;
            _db.SaveChanges();
            StatusMessage = $"Backed up to {result.FilePath}";
        }
        else
        {
            StatusMessage = $"Backup failed: {result.Error}";
        }

        RefreshBackupStatus();
    }

    // F2 and F3 now go where their labels say, instead of writing a status line.
    [RelayCommand] private void NewSale() => NavigateCommand.Execute("sale");
    [RelayCommand] private void NewPurchase() => NavigateCommand.Execute("purchase");

}
