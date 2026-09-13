using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShopApp.Data;
using ShopApp.Domain.Entities;
using ShopApp.Services;

namespace ShopApp.UI.ViewModels;

/// <summary>One encrypted backup sitting in the folder.</summary>
public record BackupFileRow(string Path, string Name, DateTime Written, long Bytes)
{
    public string Size => Bytes < 1024 * 1024
        ? $"{Bytes / 1024.0:N0} KB"
        : $"{Bytes / (1024.0 * 1024.0):N1} MB";
}

/// <summary>
/// Business details, invoice numbering and - the part that actually matters -
/// backup and restore. Everything underneath this screen was built and tested
/// long ago; until now there was simply no way to reach it.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppDbContext _db;
    private readonly BackupService _backup;
    private readonly RestoreService _restore;

    private AppSettings _settings = null!;

    // ------------------------------------------------------------ business

    [ObservableProperty] private string businessName = "";
    [ObservableProperty] private string? address;
    [ObservableProperty] private string? phone;
    [ObservableProperty] private decimal openingCashInHand;

    // ------------------------------------------------------------ invoices

    [ObservableProperty] private string invoicePrefix = "";
    [ObservableProperty] private int invoiceNextNumber = 1;
    [ObservableProperty] private int invoiceNumberPadding = 1;
    [ObservableProperty] private bool invoiceResetYearly;

    // -------------------------------------------------------------- backup

    [ObservableProperty] private string? backupFolder;
    [ObservableProperty] private int backupKeepCount = 30;
    [ObservableProperty] private string lastBackupText = "never";
    [ObservableProperty] private bool passphraseIsSet;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private BackupFileRow? selectedBackup;

    public ObservableCollection<BackupFileRow> Backups { get; } = new();

    /// <summary>Told to the shell so the banner across the top updates at once.</summary>
    public Action? OnChanged { get; set; }

    /// <summary>
    /// Set by the view. Asks for a passphrase; confirm mode makes the user type
    /// it twice and tick that they have written it on paper. Null if cancelled.
    /// </summary>
    public Func<string, bool, string?>? AskPassphrase { get; set; }

    /// <summary>Set by the view. Returns a folder path, or null if cancelled.</summary>
    public Func<string?, string?>? PickFolder { get; set; }

    /// <summary>Set by the view. Returns a .enc file path, or null if cancelled.</summary>
    public Func<string?, string?>? PickBackupFile { get; set; }

    public SettingsViewModel(AppDbContext db, BackupService backup, RestoreService restore)
    {
        _db = db;
        _backup = backup;
        _restore = restore;
        Load();
    }

    public bool BackupIsConfigured =>
        !string.IsNullOrWhiteSpace(BackupFolder) && PassphraseIsSet;

    public void Load()
    {
        _db.ChangeTracker.Clear();
        _settings = _db.Settings.Single(s => s.Id == 1);

        BusinessName = _settings.BusinessName;
        Address = _settings.Address;
        Phone = _settings.Phone;
        OpeningCashInHand = _settings.OpeningCashInHand;

        InvoicePrefix = _settings.InvoicePrefix;
        InvoiceNextNumber = _settings.InvoiceNextNumber;
        InvoiceNumberPadding = _settings.InvoiceNumberPadding;
        InvoiceResetYearly = _settings.InvoiceResetYearly;

        BackupFolder = _settings.BackupFolder;
        BackupKeepCount = _settings.BackupKeepCount;
        PassphraseIsSet = _settings.ProtectedBackupKey is { Length: > 0 };

        LastBackupText = _settings.LastBackupAt is DateTime last
            ? last.ToString("dd-MMM-yyyy HH:mm")
            : "never";

        RefreshBackupList();
        OnPropertyChanged(nameof(BackupIsConfigured));
    }

    private void RefreshBackupList()
    {
        Backups.Clear();
        if (string.IsNullOrWhiteSpace(BackupFolder) || !Directory.Exists(BackupFolder)) return;

        foreach (var f in _backup.ListBackups(BackupFolder))
            Backups.Add(new BackupFileRow(f.FullName, f.Name, f.LastWriteTime, f.Length));
    }

    [RelayCommand]
    private void SaveBusiness()
    {
        if (string.IsNullOrWhiteSpace(BusinessName))
        {
            Warn("The business name is printed at the top of every invoice. It cannot be blank.");
            return;
        }

        _settings.BusinessName = BusinessName.Trim();
        _settings.Address = Blank(Address);
        _settings.Phone = Blank(Phone);
        _settings.OpeningCashInHand = OpeningCashInHand;
        _db.SaveChanges();

        StatusMessage = "Business details saved.";
    }

    [RelayCommand]
    private void SaveInvoiceNumbering()
    {
        if (InvoiceNextNumber < 1)
        {
            Warn("The next invoice number must be 1 or more.");
            return;
        }

        // Moving the counter backwards would hand out a number that is already
        // on a printed bill, and invoice numbers are how he finds a sale again.
        var issued = _db.Sales.Count();
        if (issued > 0 && InvoiceNextNumber <= issued)
        {
            var answer = MessageBox.Show(
                $"There are already {issued} invoices. Setting the next number to " +
                $"{InvoiceNextNumber} risks issuing a number that is on a bill a " +
                "customer already has.\n\nSet it anyway?",
                "Check this first", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        _settings.InvoicePrefix = InvoicePrefix?.Trim() ?? "";
        _settings.InvoiceNextNumber = InvoiceNextNumber;
        _settings.InvoiceNumberPadding = Math.Clamp(InvoiceNumberPadding, 1, 8);
        _settings.InvoiceResetYearly = InvoiceResetYearly;
        _db.SaveChanges();

        InvoiceNumberPadding = _settings.InvoiceNumberPadding;
        StatusMessage = $"Next invoice will be {Preview}.";
    }

    /// <summary>Live preview of the number the next bill will carry.</summary>
    public string Preview =>
        $"{InvoicePrefix}{InvoiceNextNumber.ToString().PadLeft(Math.Clamp(InvoiceNumberPadding, 1, 8), '0')}";

    partial void OnInvoicePrefixChanged(string value) => OnPropertyChanged(nameof(Preview));
    partial void OnInvoiceNextNumberChanged(int value) => OnPropertyChanged(nameof(Preview));
    partial void OnInvoiceNumberPaddingChanged(int value) => OnPropertyChanged(nameof(Preview));

    [RelayCommand]
    private void ChooseFolder()
    {
        var chosen = PickFolder?.Invoke(BackupFolder);
        if (string.IsNullOrWhiteSpace(chosen)) return;

        // Backups on the same disk as the database survive a deleted file but
        // not a dead disk, which is the failure he is actually exposed to.
        var dbRoot = Path.GetPathRoot(App.DbPath);
        var backupRoot = Path.GetPathRoot(chosen);

        if (string.Equals(dbRoot, backupRoot, StringComparison.OrdinalIgnoreCase))
        {
            var answer = MessageBox.Show(
                $"That folder is on the same drive as the data ({dbRoot}).\n\n" +
                "If that drive fails you lose the database and every backup together. " +
                "A USB stick or a second disk is far safer.\n\nUse this folder anyway?",
                "Same drive", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        _settings.BackupFolder = chosen;
        _db.SaveChanges();

        BackupFolder = chosen;
        RefreshBackupList();
        OnPropertyChanged(nameof(BackupIsConfigured));
        OnChanged?.Invoke();

        StatusMessage = PassphraseIsSet
            ? "Backup folder set."
            : "Backup folder set. Now set a passphrase.";
    }

    [RelayCommand]
    private void SetPassphrase()
    {
        if (AskPassphrase is null) return;

        // Old backups were encrypted with the old key and stay unreadable with
        // the new one. He has to know that before he changes it, not after.
        if (PassphraseIsSet)
        {
            var answer = MessageBox.Show(
                "Changing the passphrase does NOT re-encrypt the backups already " +
                "in the folder. They will only ever open with the old passphrase.\n\n" +
                "Keep the old one written down as well. Change it?",
                "This affects old backups", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        var passphrase = AskPassphrase("Set backup passphrase", true);
        if (string.IsNullOrEmpty(passphrase)) return;

        var salt = BackupService.NewSalt();
        var key = BackupService.DeriveKey(passphrase, salt);

        _settings.BackupKeySalt = salt;
        _settings.ProtectedBackupKey = SecureKeyStore.Protect(key);
        _db.SaveChanges();

        PassphraseIsSet = true;
        OnPropertyChanged(nameof(BackupIsConfigured));
        OnChanged?.Invoke();

        StatusMessage = "Passphrase set. Backups will run when the app closes, or press F9.";
    }

    [RelayCommand]
    private void SaveKeepCount()
    {
        _settings.BackupKeepCount = Math.Clamp(BackupKeepCount, 3, 365);
        _db.SaveChanges();
        BackupKeepCount = _settings.BackupKeepCount;
        StatusMessage = $"Keeping the newest {BackupKeepCount} backups.";
    }

    [RelayCommand]
    private void BackupNow()
    {
        if (string.IsNullOrWhiteSpace(_settings.BackupFolder))
        {
            Warn("Choose a backup folder first.");
            return;
        }

        if (!SecureKeyStore.TryUnprotect(_settings.ProtectedBackupKey, out var key))
        {
            Warn("No passphrase is set on this Windows account. Set one first.");
            return;
        }

        var result = _backup.CreateBackup(_settings.BackupFolder, key, _settings.BackupKeepCount);

        if (!result.Success)
        {
            Warn($"Backup failed: {result.Error}");
            return;
        }

        _settings.LastBackupAt = DateTime.Now;
        _db.SaveChanges();

        Load();
        OnChanged?.Invoke();
        StatusMessage = $"Backed up to {result.FilePath}";
    }

    [RelayCommand]
    private void Restore()
    {
        if (AskPassphrase is null || PickBackupFile is null) return;

        var file = SelectedBackup?.Path ?? PickBackupFile(BackupFolder);
        if (string.IsNullOrWhiteSpace(file)) return;

        var confirm = MessageBox.Show(
            $"Restore from {Path.GetFileName(file)}?\n\n" +
            "Everything recorded since that backup was taken will disappear from the app. " +
            "The current database is renamed rather than deleted, so it can be recovered " +
            "by hand if this turns out to be the wrong file.\n\n" +
            "The app will close afterwards and must be started again.",
            "Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        // The passphrase is always asked for and the key derived from the salt
        // inside the backup file itself. That is what makes a backup restorable
        // on a new PC, where the stored key - which is tied to this Windows
        // account - is worthless.
        var passphrase = AskPassphrase("Passphrase for this backup", false);
        if (string.IsNullOrEmpty(passphrase)) return;

        byte[] key;
        try
        {
            key = BackupService.DeriveKey(passphrase, BackupService.ReadSalt(file));
        }
        catch (Exception ex)
        {
            Warn($"That file could not be read as a backup: {ex.Message}");
            return;
        }

        var result = _restore.Restore(file, key);

        if (!result.Success)
        {
            Warn($"Restore failed: {result.Error}");
            return;
        }

        MessageBox.Show(
            "Restored.\n\n" +
            (result.PreRestoreCopy is null
                ? ""
                : $"The database as it was is saved at:\n{result.PreRestoreCopy}\n\n") +
            "The app will now close. Start it again to use the restored data.",
            "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);

        Application.Current.Shutdown();
    }

    [RelayCommand]
    private void OpenBackupFolder()
    {
        if (string.IsNullOrWhiteSpace(BackupFolder) || !Directory.Exists(BackupFolder))
        {
            Warn("The backup folder is not set, or the drive is not plugged in.");
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(BackupFolder)
        {
            UseShellExecute = true
        });
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void Warn(string message) =>
        MessageBox.Show(message, "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
}
