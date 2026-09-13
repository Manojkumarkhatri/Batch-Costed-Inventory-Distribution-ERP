using Microsoft.Data.Sqlite;

namespace ShopApp.Services;

public record RestoreResult(bool Success, string? Error, string? PreRestoreCopy);

/// <summary>
/// The half of backup that people forget to build. A backup that has never
/// been restored is not a backup.
///
/// Safety rule: the current database is renamed, never overwritten. If the
/// restore turns out to be the wrong file, today's work is still on disk.
/// </summary>
public class RestoreService
{
    private readonly string _dbPath;

    public RestoreService(string dbPath) => _dbPath = dbPath;

    public RestoreResult Restore(string encBackupPath, byte[] key)
    {
        string? decrypted = null;
        try
        {
            if (!File.Exists(encBackupPath))
                return new RestoreResult(false, "Backup file not found.", null);

            decrypted = Path.Combine(Path.GetTempPath(), $"shoprestore-{Guid.NewGuid():N}.db");

            // Wrong passphrase or tampered file throws here, before anything is touched.
            BackupService.DecryptFile(encBackupPath, decrypted, key);

            // Verify the decrypted file is actually a sound database.
            if (!IsHealthySqlite(decrypted, out var reason))
                return new RestoreResult(false, $"Backup failed integrity check: {reason}", null);

            // Move the live DB aside rather than deleting it.
            string? preRestore = null;
            if (File.Exists(_dbPath))
            {
                preRestore = $"{_dbPath}.pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(_dbPath, preRestore);
                // WAL sidecars must go too, or SQLite will replay them over the
                // restored file and mix two different databases together.
                TryDelete(_dbPath + "-wal");
                TryDelete(_dbPath + "-shm");
            }

            File.Copy(decrypted, _dbPath, overwrite: false);
            return new RestoreResult(true, null, preRestore);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return new RestoreResult(false,
                "Could not open the backup. The passphrase is wrong, or the file is damaged.", null);
        }
        catch (Exception ex)
        {
            return new RestoreResult(false, ex.Message, null);
        }
        finally
        {
            if (decrypted is not null && File.Exists(decrypted))
                try { File.Delete(decrypted); } catch { }
        }
    }

    public static bool IsHealthySqlite(string path, out string reason)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar() as string;
            reason = result ?? "no result";
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
