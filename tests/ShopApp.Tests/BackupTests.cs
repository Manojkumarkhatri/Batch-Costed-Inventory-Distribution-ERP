using System.Security.Cryptography;
using FluentAssertions;
using ShopApp.Services;
using Xunit;

namespace ShopApp.Tests;

/// <summary>
/// The tests that matter most. If encryption round-trips wrongly, nobody finds
/// out until the day the hard drive dies - which is the worst possible day.
/// </summary>
public class BackupTests : IDisposable
{
    private readonly string _dir;

    public BackupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"shopapp-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void Encrypt_then_decrypt_returns_identical_bytes()
    {
        var original = RandomNumberGenerator.GetBytes(150_000);
        var src = WriteFile("src.db", original);
        var enc = Path.Combine(_dir, "out.db.enc");
        var dec = Path.Combine(_dir, "out.db");

        var salt = BackupService.NewSalt();
        var key = BackupService.DeriveKey("correct horse battery staple", salt);

        BackupService.EncryptFile(src, enc, key);
        BackupService.DecryptFile(enc, dec, key);

        File.ReadAllBytes(dec).Should().Equal(original);
    }

    [Fact]
    public void Encrypted_file_does_not_contain_the_plaintext()
    {
        var marker = "TOTAL SALES 4500000"u8.ToArray();
        var src = WriteFile("src.db", marker);
        var enc = Path.Combine(_dir, "out.db.enc");

        var key = BackupService.DeriveKey("pass", BackupService.NewSalt());
        BackupService.EncryptFile(src, enc, key);

        var bytes = File.ReadAllBytes(enc);
        IndexOf(bytes, marker).Should().Be(-1);
    }

    [Fact]
    public void Wrong_passphrase_fails_loudly_rather_than_returning_garbage()
    {
        var src = WriteFile("src.db", RandomNumberGenerator.GetBytes(4096));
        var enc = Path.Combine(_dir, "out.db.enc");

        var salt = BackupService.NewSalt();
        BackupService.EncryptFile(src, enc, BackupService.DeriveKey("right", salt));

        var wrongKey = BackupService.DeriveKey("wrong", salt);
        var act = () => BackupService.DecryptFile(enc, Path.Combine(_dir, "bad.db"), wrongKey);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Tampered_backup_is_rejected_by_the_auth_tag()
    {
        var src = WriteFile("src.db", RandomNumberGenerator.GetBytes(4096));
        var enc = Path.Combine(_dir, "out.db.enc");
        var salt = BackupService.NewSalt();
        var key = BackupService.DeriveKey("pass", salt);
        BackupService.EncryptFile(src, enc, key);

        // flip one byte in the ciphertext
        var bytes = File.ReadAllBytes(enc);
        bytes[^40] ^= 0xFF;
        File.WriteAllBytes(enc, bytes);

        var act = () => BackupService.DecryptFile(enc, Path.Combine(_dir, "bad.db"), key);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Salt_is_recoverable_from_the_backup_so_a_new_pc_can_restore()
    {
        var src = WriteFile("src.db", RandomNumberGenerator.GetBytes(1024));
        var enc = Path.Combine(_dir, "out.db.enc");
        var key = BackupService.DeriveKey("pass", BackupService.NewSalt());
        BackupService.EncryptFile(src, enc, key);

        var salt = BackupService.ReadSalt(enc);
        salt.Should().HaveCount(16);
    }

    [Fact]
    public void Same_passphrase_and_salt_always_derive_the_same_key()
    {
        var salt = BackupService.NewSalt();
        BackupService.DeriveKey("shop-2026", salt)
            .Should().Equal(BackupService.DeriveKey("shop-2026", salt));
    }

    [Fact]
    public void Backup_fails_gracefully_when_the_drive_is_missing()
    {
        var svc = new BackupService(Path.Combine(_dir, "shop.db"));
        var key = BackupService.DeriveKey("pass", BackupService.NewSalt());

        var result = svc.CreateBackup(@"Z:\NotPluggedIn", key, 30);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not reachable");
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (int j = 0; j < needle.Length; j++)
                if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }
}
