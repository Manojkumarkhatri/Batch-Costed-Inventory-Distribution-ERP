using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace ShopApp.Services;

public record BackupResult(bool Success, string? FilePath, string? Error);

/// <summary>
/// Encrypted backup to a folder the owner chooses: USB drive, second internal
/// disk, or a synced cloud folder. The app does not care which.
///
/// Flow on every app close:
///   1. SQLite online backup -> clean single-file snapshot in temp
///   2. AES-256-GCM encrypt that snapshot
///   3. write .enc into the backup folder
///   4. delete temp, prune old backups
///
/// The owner types a passphrase once at setup and never touches this again.
/// </summary>
public class BackupService
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;   // AesGcm standard
    private const int TagSize = 16;
    private const int KeySize = 32;     // AES-256
    private const int Iterations = 200_000;

    // File header so a future version can change format without guessing.
    private static readonly byte[] Magic = "SHOPBAK1"u8.ToArray();

    private readonly string _dbPath;

    public BackupService(string dbPath) => _dbPath = dbPath;

    /// <summary>Derives the AES key from the owner's passphrase.</summary>
    public static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            passphrase, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    /// <summary>
    /// Step 1: SQLite's online backup API. Safe on a live database and folds
    /// the -wal and -shm sidecars into one clean file. A raw File.Copy would
    /// not - that is the classic way to produce a corrupt backup.
    /// </summary>
    public void SnapshotTo(string destPath)
    {
        using var source = new SqliteConnection($"Data Source={_dbPath}");
        using var dest = new SqliteConnection($"Data Source={destPath}");
        source.Open();
        dest.Open();
        source.BackupDatabase(dest);
    }

    public BackupResult CreateBackup(string backupFolder, byte[] key, int keepCount)
    {
        string? temp = null;
        try
        {
            if (string.IsNullOrWhiteSpace(backupFolder))
                return new BackupResult(false, null, "No backup folder configured.");

            // Catches the unplugged USB - the single most likely failure.
            if (!Directory.Exists(backupFolder))
                return new BackupResult(false, null,
                    $"Backup folder not reachable: {backupFolder}. Is the USB drive plugged in?");

            temp = Path.Combine(Path.GetTempPath(), $"shopbak-{Guid.NewGuid():N}.db");
            SnapshotTo(temp);

            var name = $"shop-{DateTime.Now:yyyy-MM-dd-HHmm}.db.enc";
            var target = Path.Combine(backupFolder, name);

            EncryptFile(temp, target, key);
            Prune(backupFolder, keepCount);

            return new BackupResult(true, target, null);
        }
        catch (Exception ex)
        {
            return new BackupResult(false, null, ex.Message);
        }
        finally
        {
            if (temp is not null && File.Exists(temp))
                try { File.Delete(temp); } catch { /* temp cleanup is best-effort */ }
        }
    }

    /// <summary>
    /// Layout: MAGIC | SALT | NONCE | CIPHERTEXT | TAG
    /// Salt is stored so a backup can be opened on a fresh machine with only
    /// the passphrase - which is exactly the disaster-recovery case.
    /// </summary>
    public static void EncryptFile(string srcPath, string destPath, byte[] key)
    {
        var plaintext = File.ReadAllBytes(srcPath);
        var salt = NewSalt();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
            aes.Encrypt(nonce, plaintext, ciphertext, tag);

        using var outFs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        outFs.Write(Magic);
        outFs.Write(salt);
        outFs.Write(nonce);
        outFs.Write(ciphertext);
        outFs.Write(tag);
        outFs.Flush(true);   // force to physical disk before we call it done
    }

    /// <summary>Reads the salt from a backup so the key can be re-derived on a new PC.</summary>
    public static byte[] ReadSalt(string encPath)
    {
        using var fs = new FileStream(encPath, FileMode.Open, FileAccess.Read);
        var header = new byte[Magic.Length + SaltSize];
        if (fs.Read(header, 0, header.Length) != header.Length)
            throw new InvalidDataException("Backup file is truncated.");
        if (!header.Take(Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Not a ShopApp backup file.");
        return header.Skip(Magic.Length).Take(SaltSize).ToArray();
    }

    public static void DecryptFile(string srcPath, string destPath, byte[] key)
    {
        var all = File.ReadAllBytes(srcPath);
        var headerLen = Magic.Length + SaltSize + NonceSize;
        if (all.Length < headerLen + TagSize)
            throw new InvalidDataException("Backup file is truncated or not a ShopApp backup.");
        if (!all.Take(Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Not a ShopApp backup file.");

        var nonce = all.Skip(Magic.Length + SaltSize).Take(NonceSize).ToArray();
        var cipherLen = all.Length - headerLen - TagSize;
        var ciphertext = new byte[cipherLen];
        Buffer.BlockCopy(all, headerLen, ciphertext, 0, cipherLen);
        var tag = new byte[TagSize];
        Buffer.BlockCopy(all, headerLen + cipherLen, tag, 0, TagSize);

        var plaintext = new byte[cipherLen];
        using (var aes = new AesGcm(key, TagSize))
        {
            // Throws CryptographicException on a wrong passphrase OR a tampered
            // file. GCM authenticates, so a corrupted backup fails loudly here
            // rather than silently restoring garbage.
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        File.WriteAllBytes(destPath, plaintext);
    }

    private static void Prune(string folder, int keepCount)
    {
        if (keepCount <= 0) return;
        var files = new DirectoryInfo(folder)
            .GetFiles("shop-*.db.enc")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(keepCount);
        foreach (var f in files)
            try { f.Delete(); } catch { /* a locked old backup is not fatal */ }
    }

    public IReadOnlyList<FileInfo> ListBackups(string folder) =>
        !Directory.Exists(folder)
            ? Array.Empty<FileInfo>()
            : new DirectoryInfo(folder)
                .GetFiles("shop-*.db.enc")
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();
}
