using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShopApp.Data;
using ShopApp.Services;
using ShopApp.UI.ViewModels;
using ShopApp.UI.Views;

namespace ShopApp.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string DbPath { get; private set; } = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Data lives in AppData, not Program Files - Windows blocks writes there.
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShopApp");
        Directory.CreateDirectory(folder);
        DbPath = Path.Combine(folder, "shop.db");

        var sc = new ServiceCollection();

        sc.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={DbPath}"),
            ServiceLifetime.Transient);

        sc.AddTransient<StockService>();
        sc.AddTransient<SaleService>();
        sc.AddTransient<ItemService>();
        sc.AddTransient<ItemImportService>();
        sc.AddTransient<PartyService>();
        sc.AddTransient<PurchaseService>();
        sc.AddTransient<InvoiceBuilder>();
        sc.AddTransient<OpeningStockService>();
        sc.AddTransient<ReportService>();
        sc.AddTransient<PaymentService>();
        sc.AddTransient<ExpenseService>();
        sc.AddTransient<BankService>();
        sc.AddSingleton(new BackupService(DbPath));
        sc.AddSingleton(new RestoreService(DbPath));

        sc.AddTransient<MainViewModel>();
        sc.AddTransient<ItemsViewModel>();
        sc.AddTransient<PartiesViewModel>();
        sc.AddTransient<PurchaseViewModel>();
        sc.AddTransient<SaleViewModel>();
        sc.AddTransient<SaleListViewModel>();
        sc.AddTransient<PurchaseListViewModel>();
        sc.AddTransient<PaymentInViewModel>();
        sc.AddTransient<PaymentOutViewModel>();
        sc.AddTransient<ExpensesViewModel>();
        sc.AddTransient<BanksViewModel>();
        sc.AddTransient<SettingsViewModel>();
        sc.AddTransient<OpeningStockViewModel>();
        sc.AddTransient<ReportsViewModel>();

        sc.AddTransient<MainWindow>();
        sc.AddTransient<ItemsView>();
        sc.AddTransient<PartiesView>();
        sc.AddTransient<PurchaseView>();
        sc.AddTransient<SaleView>();
        sc.AddTransient<SaleListView>();
        sc.AddTransient<PurchaseListView>();
        sc.AddTransient<PaymentInView>();
        sc.AddTransient<PaymentOutView>();
        sc.AddTransient<ExpensesView>();
        sc.AddTransient<BanksView>();
        sc.AddTransient<SettingsView>();
        sc.AddTransient<OpeningStockView>();
        sc.AddTransient<ReportsView>();

        Services = sc.BuildServiceProvider();

        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            DbBootstrapper.Initialise(db);
        }

        var window = Services.GetRequiredService<MainWindow>();
        window.Show();
    }

    /// <summary>
    /// Backup runs here, on every close. This is the whole disaster-recovery
    /// story for a single-machine deployment, so it must never be skipped.
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var settings = db.Settings.Single(s => s.Id == 1);

            if (!string.IsNullOrWhiteSpace(settings.BackupFolder) &&
                SecureKeyStore.TryUnprotect(settings.ProtectedBackupKey, out var key))
            {
                var backup = Services.GetRequiredService<BackupService>();
                var result = backup.CreateBackup(
                    settings.BackupFolder, key, settings.BackupKeepCount);

                if (result.Success)
                {
                    settings.LastBackupAt = DateTime.Now;
                    db.SaveChanges();
                }
                else
                {
                    // Loud, not silent. An unplugged USB must not pass unnoticed.
                    MessageBox.Show(
                        $"BACKUP FAILED\n\n{result.Error}\n\n" +
                        "Your work is saved on this PC, but there is no backup copy. " +
                        "Please check the backup drive.",
                        "Backup failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Backup error on close:\n{ex.Message}",
                "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        base.OnExit(e);
    }
}
