using System.Reflection;
using ShopApp.Data;
using Velopack;
using Velopack.Sources;

namespace ShopApp.Services;

public record UpdateCheck(bool Available, string? Version, string? Notes, string? Error);

/// <summary>
/// Checks for a newer release and applies it.
///
/// The database is never touched by an update: it lives in AppData, the
/// program files are replaced around it, and EF applies any new migrations on
/// the next start. So an upgrade keeps everything he has recorded.
///
/// What an update cannot do is go backwards. If a release turns out to be
/// wrong, reinstalling the old version leaves a database whose schema the old
/// code does not understand. That is why a backup is taken immediately before
/// the update is applied, and why a failed backup stops the update rather than
/// being shrugged off.
/// </summary>
public class UpdateService
{
    private readonly AppDbContext _db;
    private readonly BackupService _backup;
    private readonly UpdateManager? _manager;

    public UpdateService(AppDbContext db, BackupService backup)
    {
        _db = db;
        _backup = backup;

        var feed = UpdateFeedUrl;

        // Running from `dotnet build` there is no Velopack install to update,
        // and no feed configured. The screen says so rather than failing.
        _manager = string.IsNullOrWhiteSpace(feed)
            ? null
            : new UpdateManager(new SimpleWebSource(feed));
    }

    /// <summary>
    /// Where releases are published. A plain folder served over HTTPS is
    /// enough - there is no server component to run.
    /// </summary>
    public static string UpdateFeedUrl { get; set; } = "";

    public string CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>True only when running from an installed copy with a feed set.</summary>
    public bool CanUpdate => _manager is { IsInstalled: true };

    public async Task<UpdateCheck> CheckAsync()
    {
        if (_manager is null)
            return new UpdateCheck(false, null, null,
                "No update source is configured in this build.");

        if (!_manager.IsInstalled)
            return new UpdateCheck(false, null, null,
                "This copy was not installed by the updater, so it cannot update itself.");

        try
        {
            var info = await _manager.CheckForUpdatesAsync();

            if (info is null)
            {
                AppLog.Info("Update check: already current");
                return new UpdateCheck(false, null, null, null);
            }

            var version = info.TargetFullRelease.Version.ToString();
            AppLog.Info($"Update check: {version} is available");

            return new UpdateCheck(true, version, info.TargetFullRelease.NotesMarkdown, null);
        }
        catch (Exception ex)
        {
            AppLog.Error("Update check failed", ex);
            return new UpdateCheck(false, null, null,
                "Could not reach the update server. Check the internet connection.");
        }
    }

    /// <summary>
    /// Downloads and applies, backing up first. Restarts the app on success
    /// and does not return.
    /// </summary>
    public async Task<string?> DownloadAndApplyAsync(IProgress<int>? progress = null)
    {
        if (_manager is null || !_manager.IsInstalled)
            return "This copy cannot update itself.";

        try
        {
            var info = await _manager.CheckForUpdatesAsync();
            if (info is null) return "Already on the latest version.";

            // Before the files change, not after. If this fails the update is
            // abandoned: an update with no way back is the one situation where
            // stopping is clearly better than continuing.
            var backupError = BackupBeforeUpdate();
            if (backupError is not null) return backupError;

            await _manager.DownloadUpdatesAsync(info, p => progress?.Report(p));

            AppLog.Info($"Applying update {info.TargetFullRelease.Version}");
            _manager.ApplyUpdatesAndRestart(info);

            return null;   // not reached: the process is replaced
        }
        catch (Exception ex)
        {
            AppLog.Error("Applying the update failed", ex);
            return $"The update could not be applied:\n{ex.Message}";
        }
    }

    /// <summary>
    /// A backup taken specifically because an update is about to happen.
    /// Returns null when it worked, or the reason it did not.
    /// </summary>
    private string? BackupBeforeUpdate()
    {
        var settings = _db.Settings.Single(s => s.Id == 1);

        if (string.IsNullOrWhiteSpace(settings.BackupFolder))
            return "Set a backup folder in Settings before updating. "
                 + "An update should never be the first time a backup is needed.";

        if (!SecureKeyStore.TryUnprotect(settings.ProtectedBackupKey, out var key))
            return "The backup passphrase is not available on this Windows account, "
                 + "so no backup can be taken. Set it in Settings first.";

        var result = _backup.CreateBackup(settings.BackupFolder, key, settings.BackupKeepCount);

        if (!result.Success)
            return $"The backup before updating failed, so the update was stopped:\n{result.Error}";

        settings.LastBackupAt = DateTime.Now;
        _db.SaveChanges();

        AppLog.Info($"Pre-update backup written to {result.FilePath}");
        return null;
    }
}
