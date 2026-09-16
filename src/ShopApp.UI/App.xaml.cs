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

        // Logging and the exception handlers go up before anything that could
        // fail. A crash during startup is exactly the one worth seeing, and
        // until this point there is nowhere to record it.
        AppLog.Start(Path.Combine(folder, "logs"));
        AppLog.Info($"---- started, version {GetType().Assembly.GetName().Version} ----");
        HookExceptionHandlers();

        try
        {
            Compose();
        }
        catch (Exception ex)
        {
            // Nothing is running yet, so there is nothing to continue into.
            AppLog.Error("Startup failed", ex);
            new CrashDialog(ex, fatal: true).ShowDialog();
            Shutdown(1);
        }
    }

    /// <summary>
    /// Three separate routes an exception can take out of a WPF application,
    /// and all three end with a silently closed window if nobody is listening.
    /// </summary>
    private void HookExceptionHandlers()
    {
        // Anything thrown on the UI thread: a click handler, a binding, a
        // command. Recoverable - the rest of the app is still fine.
        DispatcherUnhandledException += (_, args) =>
        {
            AppLog.Error("Unhandled exception on the UI thread", args.Exception);
            args.Handled = true;
            ShowCrash(args.Exception, fatal: false);
        };

        // A background thread. The process is going down whatever we do; the
        // most that can be done is record it and say so.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                AppLog.Error("Unhandled exception on a background thread", ex);
                ShowCrash(ex, fatal: true);
            }
        };

        // A Task nobody awaited. Logged but not shown: by the time the
        // finaliser notices, the user has moved on and a dialog would arrive
        // with no context.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    private void ShowCrash(Exception ex, bool fatal)
    {
        try
        {
            Dispatcher.Invoke(() => new CrashDialog(ex, fatal)
            {
                Owner = Current?.MainWindow?.IsLoaded == true ? Current.MainWindow : null
            }.ShowDialog());
        }
        catch
        {
            // If even the dialog fails, the log still has the original.
        }

        if (fatal) Shutdown(1);
    }

    private void Compose()
    {

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
        sc.AddTransient<DocumentBuilder>();
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

        AppLog.Info("Main window shown");
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
                    AppLog.Info($"Backup written to {result.FilePath}");
                }
                else
                {
                    AppLog.Error($"Backup failed: {result.Error}");
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
            AppLog.Error("Backup threw on close", ex);
            MessageBox.Show($"Backup error on close:\n{ex.Message}",
                "Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        AppLog.Info("---- closed ----");
        base.OnExit(e);
    }
}
